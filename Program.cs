using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using DynamicForms.Api.Auth;
using DynamicForms.Api.Data;
using DynamicForms.Api.Domain;
using DynamicForms.Api.Dtos;
using DynamicForms.Api.Extensions;
using DynamicForms.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// --- DB ---
var connStr = builder.Configuration.GetConnectionString("mysql")
               ?? Environment.GetEnvironmentVariable("MYSQL_CONNECTION")
               ?? "server=localhost;port=3306;database=dynamicforms;user=root;password=Adi@2003#J13;";
builder.Services.AddDbContext<AppDb>(opt =>
    opt.UseMySql(connStr, ServerVersion.AutoDetect(connStr)));

// --- JWT ---
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.PostConfigure<JwtSettings>(opts =>
{
    opts.Issuer ??= "dynamicforms";
    opts.Audience ??= "dynamicforms-clients";
    opts.Key ??= Environment.GetEnvironmentVariable("JWT_KEY") ?? "dev_super_secret_key_please_change";
    opts.ExpiryMinutes = opts.ExpiryMinutes == 0 ? 120 : opts.ExpiryMinutes;
});
builder.Services.AddScoped<JwtService>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

// Avoid building an extra ServiceProvider here: read configuration directly
var jwtCfg = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings
{
    Issuer = "dynamicforms",
    Audience = "dynamicforms-clients",
    Key = Environment.GetEnvironmentVariable("JWT_KEY") ?? "dev_super_secret_key_please_change",
    ExpiryMinutes = 120
};

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtCfg.Issuer,
            ValidAudience = jwtCfg.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtCfg.Key!))
        };
    });

builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("Admin", p => p.RequireRole(Roles.Admin));
    opts.AddPolicy("Reviewer", p => p.RequireRole(Roles.Reviewer, Roles.Admin));
    opts.AddPolicy("User", p => p.RequireRole(Roles.User, Roles.Admin));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "DynamicForms API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme.",
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Migrate & seed (demo)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    db.Database.Migrate();
    await Seed.EnsureAdminAsync(scope.ServiceProvider);
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

// Health
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

// ===== AUTH =====
app.MapPost("/auth/register",
    async (RegisterRequest req, AppDb db, IPasswordHasher<User> hasher) =>
    {
        if (await db.Users.AnyAsync(u => u.Email == req.Email))
            return Results.BadRequest(new { message = "Email already registered" });

        var user = new User
        {
            UserId = Guid.NewGuid(),
            Email = req.Email.Trim().ToLowerInvariant(),
            Role = Roles.User
        };
        user.PasswordHash = hasher.HashPassword(user, req.Password);

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return Results.Created($"/users/{user.UserId}", new { user.UserId, user.Email, user.Role });
    })
   .WithTags("Auth");

app.MapPost("/auth/login",
    async (LoginRequest req, AppDb db, IPasswordHasher<User> hasher, JwtService jwt) =>
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == req.Email.Trim().ToLowerInvariant());
        if (user == null) return Results.Unauthorized();

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash!, req.Password);
        if (result == PasswordVerificationResult.Failed) return Results.Unauthorized();

        var token = jwt.Generate(user);
        return Results.Ok(new { access_token = token, token_type = "Bearer", role = user.Role, user_id = user.UserId });
    })
   .WithTags("Auth");

// ===== TEMPLATES =====
app.MapPost("/templates",
    [Authorize(Policy = "Admin")] async (TemplateCreateRequest req, AppDb db) =>
    {
        var t = new Template
        {
            TemplateId = Guid.NewGuid(),
            Name = req.Name,
            Description = req.Description,
            Status = TemplateStatus.Draft,
            CreatedBy = req.CreatedBy
        };
        db.Templates.Add(t);
        await db.SaveChangesAsync();
        return Results.Created($"/templates/{t.TemplateId}", t);
    })
   .WithTags("Templates");

app.MapGet("/templates",
    [Authorize(Policy = "Reviewer")] async (AppDb db) =>
        await db.Templates.OrderByDescending(t => t.CreatedAt).ToListAsync())
   .WithTags("Templates");

app.MapPost("/templates/{id:guid}/versions",
    [Authorize(Policy = "Admin")] async (Guid id, TemplateVersionCreateRequest req, AppDb db) =>
    {
        if (!JsonValidation.JsonIsValid(req.DesignJson))
            return Results.BadRequest(new { message = "design_json must be valid JSON" });

        var tmpl = await db.Templates.FindAsync(id);
        if (tmpl == null) return Results.NotFound();

        var latest = await db.TemplateVersions.Where(v => v.TemplateId == id)
                                              .OrderByDescending(v => v.Version)
                                              .FirstOrDefaultAsync();
        var version = (latest?.Version ?? 0) + 1;

        var tv = new TemplateVersion
        {
            TemplateVersionId = Guid.NewGuid(),
            TemplateId = id,
            Version = version,
            IsPublished = false,
            DesignJson = req.DesignJson,
            JsonSchema = req.JsonSchema
        };
        db.TemplateVersions.Add(tv);
        await db.SaveChangesAsync();
        return Results.Created($"/templates/{id}/versions/{version}", tv);
    })
   .WithTags("Templates");

app.MapPost("/templates/{id:guid}/versions/{version:int}/publish",
    [Authorize(Policy = "Admin")] async (Guid id, int version, AppDb db) =>
    {
        var tv = await db.TemplateVersions.SingleOrDefaultAsync(v => v.TemplateId == id && v.Version == version);
        if (tv == null) return Results.NotFound();
        tv.IsPublished = true;
        await db.SaveChangesAsync();
        return Results.NoContent();
    })
   .WithTags("Templates");

app.MapGet("/templates/{id:guid}/versions/latest",
    [Authorize] async (Guid id, bool published, AppDb db) =>
    {
        var q = db.TemplateVersions.Where(v => v.TemplateId == id);
        if (published) q = q.Where(v => v.IsPublished);
        var tv = await q.OrderByDescending(v => v.Version).FirstOrDefaultAsync();
        return tv is null ? Results.NotFound() : Results.Ok(tv);
    })
   .WithTags("Templates");

// UPDATE template
app.MapPut("/templates/{id:guid}",
    [Authorize(Policy = "Admin")] async (Guid id, TemplateUpdateRequest req, AppDb db) =>
    {
        var t = await db.Templates.FirstOrDefaultAsync(x => x.TemplateId == id);
        if (t is null) return Results.NotFound();

        if (req.Name is not null) t.Name = req.Name;
        if (req.Description is not null) t.Description = req.Description;
        if (req.Status is not null) t.Status = req.Status;

        t.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(t);
    })
   .WithTags("Templates");

// DELETE template (blocked if in use)
app.MapDelete("/templates/{id:guid}",
    [Authorize(Policy = "Admin")] async (Guid id, AppDb db) =>
    {
        var hasInstances = await db.FormInstances.AnyAsync(fi => fi.TemplateId == id);
        if (hasInstances) return Results.Conflict(new { message = "Template is in use by form instances." });

        var versions = db.TemplateVersions.Where(tv => tv.TemplateId == id);
        db.TemplateVersions.RemoveRange(versions);

        var t = await db.Templates.FirstOrDefaultAsync(x => x.TemplateId == id);
        if (t is null) return Results.NotFound();

        db.Templates.Remove(t);
        await db.SaveChangesAsync();
        return Results.NoContent();
    })
   .WithTags("Templates");

// UPDATE template version
app.MapPut("/templates/{id:guid}/versions/{version:int}",
    [Authorize(Policy = "Admin")] async (Guid id, int version, TemplateVersionUpdateRequest req, AppDb db) =>
    {
        var tv = await db.TemplateVersions.FirstOrDefaultAsync(x => x.TemplateId == id && x.Version == version);
        if (tv is null) return Results.NotFound();

        if (req.DesignJson is not null)
        {
            if (!req.DesignJson.IsValidJson()) return Results.BadRequest(new { message = "designJson is not valid JSON" });
            tv.DesignJson = req.DesignJson;
        }
        if (req.JsonSchema is not null)
        {
            if (!req.JsonSchema.IsValidJson()) return Results.BadRequest(new { message = "jsonSchema is not valid JSON" });
            tv.JsonSchema = req.JsonSchema;
        }
        await db.SaveChangesAsync();
        return Results.Ok(tv);
    })
   .WithTags("Templates");

// DELETE template version (blocked if in use)
app.MapDelete("/templates/{id:guid}/versions/{version:int}",
    [Authorize(Policy = "Admin")] async (Guid id, int version, AppDb db) =>
    {
        var inUse = await db.FormInstances.AnyAsync(fi => fi.TemplateId == id && fi.TemplateVersion == version);
        if (inUse) return Results.Conflict(new { message = "TemplateVersion is in use by form instances." });

        var tv = await db.TemplateVersions.FirstOrDefaultAsync(x => x.TemplateId == id && x.Version == version);
        if (tv is null) return Results.NotFound();

        db.TemplateVersions.Remove(tv);
        await db.SaveChangesAsync();
        return Results.NoContent();
    })
   .WithTags("Templates");

// ===== FORMS =====
app.MapPost("/forms",
    [Authorize(Policy = "User")] async (FormCreateRequest req, AppDb db, HttpContext ctx) =>
    {
        var uid = ctx.User.UserId();
        var user = await db.Users.FindAsync(uid);
        if (user == null) return Results.Unauthorized();

        TemplateVersion? tv = null;
        if (req.TemplateVersion.HasValue)
        {
            tv = await db.TemplateVersions.SingleOrDefaultAsync(v => v.TemplateId == req.TemplateId && v.Version == req.TemplateVersion);
            if (tv == null || !tv.IsPublished) return Results.BadRequest(new { message = "Specified version not found or not published" });
        }
        else
        {
            tv = await db.TemplateVersions.Where(v => v.TemplateId == req.TemplateId && v.IsPublished)
                                          .OrderByDescending(v => v.Version).FirstOrDefaultAsync();
            if (tv == null) return Results.BadRequest(new { message = "No published version found" });
        }

        var fi = new FormInstance
        {
            InstanceId = Guid.NewGuid(),
            TemplateId = req.TemplateId,
            TemplateVersion = tv.Version,
            AssigneeUserId = user.UserId,
            Status = FormStatus.InProgress,
            Email = req.Email,
            PhoneE164 = req.Phone,
            Country = req.Country
        };

        db.FormInstances.Add(fi);
        await db.SaveChangesAsync();
        return Results.Created($"/forms/{fi.InstanceId}", fi);
    })
   .WithTags("Forms");

app.MapGet("/forms/{id:guid}",
    [Authorize] async (Guid id, AppDb db, HttpContext ctx) =>
    {
        var fi = await db.FormInstances.FindAsync(id);
        if (fi == null) return Results.NotFound();
        if (!ctx.User.IsInRole(Roles.Admin) && fi.AssigneeUserId != ctx.User.UserId()) return Results.Forbid();
        return Results.Ok(fi);
    })
   .WithTags("Forms");

// PATCH form (Admin)
app.MapPatch("/forms/{id:guid}",
    [Authorize(Policy = "Admin")] async (Guid id, FormUpdateRequest req, AppDb db) =>
    {
        var fi = await db.FormInstances.FirstOrDefaultAsync(x => x.InstanceId == id);
        if (fi is null) return Results.NotFound();

        if (req.Status is not null) fi.Status = req.Status;
        if (req.AssigneeUserId is not null) fi.AssigneeUserId = req.AssigneeUserId.Value;
        if (req.Email is not null) fi.Email = req.Email;
        if (req.PhoneE164 is not null) fi.PhoneE164 = req.PhoneE164;
        if (req.Country is not null) fi.Country = req.Country;

        if (req.SubmittedAt is not null)
        {
            fi.SubmittedAt = req.SubmittedAt.Value.UtcDateTime;
        }
        else if (req.Status?.Equals("submitted", StringComparison.OrdinalIgnoreCase) == true && fi.SubmittedAt is null)
        {
            fi.SubmittedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        return Results.Ok(fi);
    })
   .WithTags("Forms");

// DELETE form (Admin) + optional file cleanup
app.MapDelete("/forms/{id:guid}",
    [Authorize(Policy = "Admin")] async (Guid id, bool deleteFiles, AppDb db) =>
    {
        var fi = await db.FormInstances.FirstOrDefaultAsync(x => x.InstanceId == id);
        if (fi is null) return Results.NotFound();

        var steps = db.FormStepResponses.Where(x => x.InstanceId == id);
        var docs = db.IdentityDocuments.Where(x => x.InstanceId == id);
        var bios = db.BiometricCaptures.Where(x => x.InstanceId == id);

        // Collect file ids for optional deletion
        var fileIds = new HashSet<Guid>();
        if (deleteFiles)
        {
            fileIds.UnionWith(await docs.Select(d => d.FileId).ToListAsync());
            fileIds.UnionWith(await bios.Where(b => b.SelfieFileId != Guid.Empty).Select(b => b.SelfieFileId).ToListAsync());
            fileIds.UnionWith(await bios.Where(b => b.VideoFileId != null).Select(b => b.VideoFileId!.Value).ToListAsync());
        }

        db.FormStepResponses.RemoveRange(steps);
        db.IdentityDocuments.RemoveRange(docs);
        db.BiometricCaptures.RemoveRange(bios);
        db.FormInstances.Remove(fi);

        if (deleteFiles && fileIds.Count > 0)
        {
            var stillUsed = new HashSet<Guid>(
                await db.IdentityDocuments.Where(d => fileIds.Contains(d.FileId)).Select(d => d.FileId).ToListAsync()
            );
            stillUsed.UnionWith(await db.BiometricCaptures.Where(b => b.SelfieFileId != Guid.Empty && fileIds.Contains(b.SelfieFileId))
                                                         .Select(b => b.SelfieFileId).ToListAsync());
            stillUsed.UnionWith(await db.BiometricCaptures.Where(b => b.VideoFileId != null && fileIds.Contains(b.VideoFileId.Value))
                                                         .Select(b => b.VideoFileId!.Value).ToListAsync());

            var toDelete = fileIds.Except(stillUsed).ToList();
            if (toDelete.Count > 0)
            {
                var files = db.Files.Where(f => toDelete.Contains(f.FileId));
                db.Files.RemoveRange(files);
            }
        }

        await db.SaveChangesAsync();
        return Results.NoContent();
    })
   .WithTags("Forms");

// Submit/Upsert a step (User)
app.MapPost("/forms/{id:guid}/steps/{stepKey}",
    [Authorize(Policy = "User")] async (Guid id, string stepKey, StepSubmitRequest req, AppDb db, HttpContext ctx) =>
    {
        var fi = await db.FormInstances.FindAsync(id);
        if (fi == null) return Results.NotFound();
        if (!ctx.User.IsInRole(Roles.Admin) && fi.AssigneeUserId != ctx.User.UserId()) return Results.Forbid();

        if (!JsonValidation.JsonIsValid(req.DataJson))
            return Results.BadRequest(new { message = "data_json must be valid JSON" });

        var existing = await db.FormStepResponses.SingleOrDefaultAsync(s => s.InstanceId == id && s.StepKey == stepKey);
        if (existing == null)
        {
            existing = new FormStepResponse
            {
                StepResponseId = Guid.NewGuid(),
                InstanceId = id,
                StepKey = stepKey,
                StepOrder = stepKey switch
                {
                    "personal_info" => 1,
                    "identity_documents" => 2,
                    "biometric" => 3,
                    _ => 99
                },
                DataJson = req.DataJson
            };
            db.FormStepResponses.Add(existing);
        }
        else
        {
            existing.DataJson = req.DataJson;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        return Results.Ok(existing);
    })
   .WithTags("Forms");

// Upload a new identity document (User)
app.MapPost("/forms/{id:guid}/documents",
    [Authorize(Policy = "User")] async (Guid id, DocumentSubmitRequest req, AppDb db, HttpContext ctx) =>
    {
        var fi = await db.FormInstances.FindAsync(id);
        if (fi == null) return Results.NotFound();
        if (!ctx.User.IsInRole(Roles.Admin) && fi.AssigneeUserId != ctx.User.UserId()) return Results.Forbid();

        var file = new FileObject
        {
            FileId = Guid.NewGuid(),
            StorageKey = req.StorageKey ?? $"doc:{Guid.NewGuid()}",
            MimeType = req.MimeType ?? "application/octet-stream",
            SizeBytes = req.SizeBytes ?? 0
        };
        db.Files.Add(file);

        var doc = new IdentityDocument
        {
            DocumentId = Guid.NewGuid(),
            InstanceId = id,
            DocType = req.DocType,
            IssuingCountry = req.IssuingCountry,
            NumberRedacted = req.NumberRedacted,
            ExpiryDate = req.ExpiryDate,
            Side = req.Side ?? "single",
            FileId = file.FileId,
            OcrJson = req.OcrJson
        };
        db.IdentityDocuments.Add(doc);

        await db.SaveChangesAsync();
        return Results.Created($"/forms/{id}/documents/{doc.DocumentId}", doc);
    })
   .WithTags("Forms");

// Upload a new biometric capture (User)
app.MapPost("/forms/{id:guid}/biometric",
    [Authorize(Policy = "User")] async (Guid id, BiometricSubmitRequest req, AppDb db, HttpContext ctx) =>
    {
        var fi = await db.FormInstances.FindAsync(id);
        if (fi == null) return Results.NotFound();
        if (!ctx.User.IsInRole(Roles.Admin) && fi.AssigneeUserId != ctx.User.UserId()) return Results.Forbid();

        var selfieFile = new FileObject
        {
            FileId = Guid.NewGuid(),
            StorageKey = req.SelfieStorageKey ?? $"selfie:{Guid.NewGuid()}",
            MimeType = req.SelfieMimeType ?? "image/jpeg",
            SizeBytes = req.SelfieSizeBytes ?? 0
        };
        db.Files.Add(selfieFile);

        FileObject? videoFile = null;
        if (!string.IsNullOrWhiteSpace(req.VideoStorageKey))
        {
            videoFile = new FileObject
            {
                FileId = Guid.NewGuid(),
                StorageKey = req.VideoStorageKey!,
                MimeType = req.VideoMimeType ?? "video/mp4",
                SizeBytes = req.VideoSizeBytes ?? 0
            };
            db.Files.Add(videoFile);
        }

        var bio = new BiometricCapture
        {
            BiometricId = Guid.NewGuid(),
            InstanceId = id,
            VideoFileId = videoFile?.FileId,
            SelfieFileId = selfieFile.FileId,
            LivenessProvider = req.LivenessProvider,
            LivenessThreshold = req.LivenessThreshold,
            LivenessScore = req.LivenessScore,
            ChallengeType = req.ChallengeType,
            FrameTimeMs = req.FrameTimeMs,
            RetryCount = req.RetryCount
        };
        db.BiometricCaptures.Add(bio);

        await db.SaveChangesAsync();
        return Results.Created($"/forms/{id}/biometric/{bio.BiometricId}", bio);
    })
   .WithTags("Forms");

// PUT step (idempotent, any authenticated user on the instance)
app.MapPut("/forms/{id:guid}/steps/{stepKey}",
    [Authorize] async (Guid id, string stepKey, StepUpdateRequest req, AppDb db, HttpContext ctx) =>
    {
        var inst = await db.FormInstances.FirstOrDefaultAsync(x => x.InstanceId == id);
        if (inst is null) return Results.NotFound();
        if (!ctx.User.IsInRole(Roles.Admin) && inst.AssigneeUserId != ctx.User.UserId()) return Results.Forbid();

        if (!req.DataJson.IsValidJson())
            return Results.BadRequest(new { message = "dataJson is not valid JSON" });

        var step = await db.FormStepResponses.FirstOrDefaultAsync(x => x.InstanceId == id && x.StepKey == stepKey);
        if (step is null)
        {
            step = new FormStepResponse
            {
                StepResponseId = Guid.NewGuid(),
                InstanceId = id,
                StepKey = stepKey,
                StepOrder = 0,
                DataJson = req.DataJson,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.FormStepResponses.Add(step);
        }
        else
        {
            step.DataJson = req.DataJson;
            step.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        return Results.Ok(step);
    })
   .WithTags("Forms");

// DELETE step
app.MapDelete("/forms/{id:guid}/steps/{stepKey}",
    [Authorize] async (Guid id, string stepKey, AppDb db, HttpContext ctx) =>
    {
        var inst = await db.FormInstances.FirstOrDefaultAsync(x => x.InstanceId == id);
        if (inst is null) return Results.NotFound();
        if (!ctx.User.IsInRole(Roles.Admin) && inst.AssigneeUserId != ctx.User.UserId()) return Results.Forbid();

        var step = await db.FormStepResponses.FirstOrDefaultAsync(x => x.InstanceId == id && x.StepKey == stepKey);
        if (step is null) return Results.NotFound();

        db.FormStepResponses.Remove(step);
        await db.SaveChangesAsync();
        return Results.NoContent();
    })
   .WithTags("Forms");

// ===== Documents: UPDATE + DELETE =====
app.MapPut("/forms/{id:guid}/documents/{docId:guid}",
    [Authorize] async (Guid id, Guid docId, DocumentUpdateRequest req, AppDb db, HttpContext ctx) =>
    {
        var inst = await db.FormInstances.FirstOrDefaultAsync(x => x.InstanceId == id);
        if (inst is null) return Results.NotFound();
        if (!ctx.User.IsInRole(Roles.Admin) && inst.AssigneeUserId != ctx.User.UserId()) return Results.Forbid();

        var doc = await db.IdentityDocuments.FirstOrDefaultAsync(x => x.InstanceId == id && x.DocumentId == docId);
        if (doc is null) return Results.NotFound();

        if (req.DocType is not null) doc.DocType = req.DocType;
        if (req.IssuingCountry is not null) doc.IssuingCountry = req.IssuingCountry;
        if (req.NumberRedacted is not null) doc.NumberRedacted = req.NumberRedacted;
        if (req.ExpiryDate is not null) doc.ExpiryDate = req.ExpiryDate.Value.UtcDateTime;
        if (req.Side is not null) doc.Side = req.Side;

        if (req.OcrJson is not null)
        {
            if (!req.OcrJson.IsValidJson()) return Results.BadRequest(new { message = "ocrJson is not valid JSON" });
            doc.OcrJson = req.OcrJson;
        }

        // Optional file replacement
        if (!string.IsNullOrWhiteSpace(req.StorageKey) &&
            !string.IsNullOrWhiteSpace(req.MimeType) &&
            req.SizeBytes is not null)
        {
            var newFile = new FileObject
            {
                FileId = Guid.NewGuid(),
                StorageKey = req.StorageKey!,
                MimeType = req.MimeType!,
                SizeBytes = req.SizeBytes.Value,
                EncryptedAtRest = false,
                CreatedAt = DateTime.UtcNow
            };
            db.Files.Add(newFile);
            doc.FileId = newFile.FileId;
        }

        await db.SaveChangesAsync();
        return Results.Ok(doc);
    })
   .WithTags("Forms");

app.MapDelete("/forms/{id:guid}/documents/{docId:guid}",
    [Authorize] async (Guid id, Guid docId, bool deleteFile, AppDb db, HttpContext ctx) =>
    {
        var inst = await db.FormInstances.FirstOrDefaultAsync(x => x.InstanceId == id);
        if (inst is null) return Results.NotFound();
        if (!ctx.User.IsInRole(Roles.Admin) && inst.AssigneeUserId != ctx.User.UserId()) return Results.Forbid();

        var doc = await db.IdentityDocuments.FirstOrDefaultAsync(x => x.InstanceId == id && x.DocumentId == docId);
        if (doc is null) return Results.NotFound();

        Guid fileId = doc.FileId;

        db.IdentityDocuments.Remove(doc);

        if (deleteFile)
        {
            var stillUsed =
                await db.IdentityDocuments.AnyAsync(d => d.FileId == fileId && d.DocumentId != docId) ||
                await db.BiometricCaptures.AnyAsync(b => b.SelfieFileId == fileId || b.VideoFileId == fileId);
            if (!stillUsed)
            {
                var f = await db.Files.FirstOrDefaultAsync(f => f.FileId == fileId);
                if (f is not null) db.Files.Remove(f);
            }
        }

        await db.SaveChangesAsync();
        return Results.NoContent();
    })
   .WithTags("Forms");

// ===== Biometric: UPDATE + DELETE =====
app.MapPut("/forms/{id:guid}/biometric/{bioId:guid}",
    [Authorize] async (Guid id, Guid bioId, BiometricUpdateRequest req, AppDb db, HttpContext ctx) =>
    {
        var inst = await db.FormInstances.FirstOrDefaultAsync(x => x.InstanceId == id);
        if (inst is null) return Results.NotFound();
        if (!ctx.User.IsInRole(Roles.Admin) && inst.AssigneeUserId != ctx.User.UserId()) return Results.Forbid();

        var bio = await db.BiometricCaptures.FirstOrDefaultAsync(x => x.InstanceId == id && x.BiometricId == bioId);
        if (bio is null) return Results.NotFound();

        // optional replace selfie
        if (!string.IsNullOrWhiteSpace(req.SelfieStorageKey) &&
            !string.IsNullOrWhiteSpace(req.SelfieMimeType) &&
            req.SelfieSizeBytes is not null)
        {
            var newSelfie = new FileObject
            {
                FileId = Guid.NewGuid(),
                StorageKey = req.SelfieStorageKey!,
                MimeType = req.SelfieMimeType!,
                SizeBytes = req.SelfieSizeBytes.Value,
                EncryptedAtRest = false,
                CreatedAt = DateTime.UtcNow
            };
            db.Files.Add(newSelfie);
            bio.SelfieFileId = newSelfie.FileId;
        }

        // optional replace video
        if (!string.IsNullOrWhiteSpace(req.VideoStorageKey) &&
            !string.IsNullOrWhiteSpace(req.VideoMimeType) &&
            req.VideoSizeBytes is not null)
        {
            var newVideo = new FileObject
            {
                FileId = Guid.NewGuid(),
                StorageKey = req.VideoStorageKey!,
                MimeType = req.VideoMimeType!,
                SizeBytes = req.VideoSizeBytes.Value,
                EncryptedAtRest = false,
                CreatedAt = DateTime.UtcNow
            };
            db.Files.Add(newVideo);
            bio.VideoFileId = newVideo.FileId;
        }

        if (req.LivenessProvider is not null) bio.LivenessProvider = req.LivenessProvider;
        if (req.LivenessThreshold is not null) bio.LivenessThreshold = req.LivenessThreshold;
        if (req.LivenessScore is not null) bio.LivenessScore = req.LivenessScore;
        if (req.ChallengeType is not null) bio.ChallengeType = req.ChallengeType;
        if (req.FrameTimeMs is not null) bio.FrameTimeMs = req.FrameTimeMs;
        if (req.RetryCount is not null) bio.RetryCount = req.RetryCount.Value;

        await db.SaveChangesAsync();
        return Results.Ok(bio);
    })
   .WithTags("Forms");

app.MapDelete("/forms/{id:guid}/biometric/{bioId:guid}",
    [Authorize] async (Guid id, Guid bioId, bool deleteFiles, AppDb db, HttpContext ctx) =>
    {
        var inst = await db.FormInstances.FirstOrDefaultAsync(x => x.InstanceId == id);
        if (inst is null) return Results.NotFound();
        if (!ctx.User.IsInRole(Roles.Admin) && inst.AssigneeUserId != ctx.User.UserId()) return Results.Forbid();

        var bio = await db.BiometricCaptures.FirstOrDefaultAsync(x => x.InstanceId == id && x.BiometricId == bioId);
        if (bio is null) return Results.NotFound();

        Guid? selfieId = bio.SelfieFileId;
        Guid? videoId = bio.VideoFileId;

        db.BiometricCaptures.Remove(bio);

        if (deleteFiles)
        {
            async Task MaybeDeleteFile(Guid? fid)
            {
                if (fid is null) return;
                var usedElsewhere =
                    await db.IdentityDocuments.AnyAsync(d => d.FileId == fid.Value) ||
                    await db.BiometricCaptures.AnyAsync(b => b.SelfieFileId == fid.Value || b.VideoFileId == fid.Value);
                if (!usedElsewhere)
                {
                    var f = await db.Files.FirstOrDefaultAsync(x => x.FileId == fid.Value);
                    if (f is not null) db.Files.Remove(f);
                }
            }
            await MaybeDeleteFile(selfieId);
            await MaybeDeleteFile(videoId);
        }

        await db.SaveChangesAsync();
        return Results.NoContent();
    })
   .WithTags("Forms");

app.Run();
