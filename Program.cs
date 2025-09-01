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

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        var sp = builder.Services.BuildServiceProvider();
        var jwt = sp.GetRequiredService<IOptions<JwtSettings>>().Value;
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key!))
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
app.MapPost("/auth/register", async (RegisterRequest req, AppDb db, IPasswordHasher<User> hasher) =>
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
}).WithTags("Auth");

app.MapPost("/auth/login", async (LoginRequest req, AppDb db, IPasswordHasher<User> hasher, JwtService jwt) =>
{
    var user = await db.Users.SingleOrDefaultAsync(u => u.Email == req.Email.Trim().ToLowerInvariant());
    if (user == null) return Results.Unauthorized();

    var result = hasher.VerifyHashedPassword(user, user.PasswordHash!, req.Password);
    if (result == PasswordVerificationResult.Failed) return Results.Unauthorized();

    var token = jwt.Generate(user);
    return Results.Ok(new { access_token = token, token_type = "Bearer", role = user.Role, user_id = user.UserId });
}).WithTags("Auth");

// ===== TEMPLATES =====
app.MapPost("/templates", [Authorize(Policy = "Admin")] async (TemplateCreateRequest req, AppDb db) =>
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
}).WithTags("Templates");

app.MapGet("/templates", [Authorize(Policy = "Reviewer")] async (AppDb db) =>
    await db.Templates.OrderByDescending(t => t.CreatedAt).ToListAsync()
).WithTags("Templates");

app.MapPost("/templates/{id:guid}/versions", [Authorize(Policy = "Admin")] async (Guid id, TemplateVersionCreateRequest req, AppDb db) =>
{
    if (!JsonValidation.JsonIsValid(req.DesignJson))
        return Results.BadRequest(new { message = "design_json must be valid JSON" });

    var tmpl = await db.Templates.FindAsync(id);
    if (tmpl == null) return Results.NotFound();

    var latest = await db.TemplateVersions.Where(v => v.TemplateId == id).OrderByDescending(v => v.Version).FirstOrDefaultAsync();
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
}).WithTags("Templates");

app.MapPost("/templates/{id:guid}/versions/{version:int}/publish", [Authorize(Policy = "Admin")] async (Guid id, int version, AppDb db) =>
{
    var tv = await db.TemplateVersions.SingleOrDefaultAsync(v => v.TemplateId == id && v.Version == version);
    if (tv == null) return Results.NotFound();
    tv.IsPublished = true;
    await db.SaveChangesAsync();
    return Results.NoContent();
}).WithTags("Templates");

app.MapGet("/templates/{id:guid}/versions/latest", [Authorize] async (Guid id, bool published, AppDb db) =>
{
    var q = db.TemplateVersions.Where(v => v.TemplateId == id);
    if (published) q = q.Where(v => v.IsPublished);
    var tv = await q.OrderByDescending(v => v.Version).FirstOrDefaultAsync();
    return tv is null ? Results.NotFound() : Results.Ok(tv);
}).WithTags("Templates");

// ===== FORMS =====
app.MapPost("/forms", [Authorize(Policy = "User")] async (FormCreateRequest req, AppDb db, HttpContext ctx) =>
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
}).WithTags("Forms");

app.MapGet("/forms/{id:guid}", [Authorize] async (Guid id, AppDb db, HttpContext ctx) =>
{
    var fi = await db.FormInstances.FindAsync(id);
    if (fi == null) return Results.NotFound();
    if (!ctx.User.IsInRole(Roles.Admin) && fi.AssigneeUserId != ctx.User.UserId()) return Results.Forbid();
    return Results.Ok(fi);
}).WithTags("Forms");

app.MapPost("/forms/{id:guid}/steps/{stepKey}", [Authorize(Policy = "User")] async (Guid id, string stepKey, StepSubmitRequest req, AppDb db, HttpContext ctx) =>
{
    var fi = await db.FormInstances.FindAsync(id);
    if (fi == null) return Results.NotFound();
    if (!ctx.User.IsInRole(Roles.Admin) && fi.AssigneeUserId != ctx.User.UserId()) return Results.Forbid();

    if (!JsonValidation.JsonIsValid(req.DataJson)) return Results.BadRequest(new { message = "data_json must be valid JSON" });

    var existing = await db.FormStepResponses.SingleOrDefaultAsync(s => s.InstanceId == id && s.StepKey == stepKey);
    if (existing == null)
    {
        existing = new FormStepResponse
        {
            StepResponseId = Guid.NewGuid(),
            InstanceId = id,
            StepKey = stepKey,
            StepOrder = stepKey switch { "personal_info" => 1, "identity_documents" => 2, "biometric" => 3, _ => 99 },
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
}).WithTags("Forms");

app.MapPost("/forms/{id:guid}/documents", [Authorize(Policy = "User")] async (Guid id, DocumentSubmitRequest req, AppDb db, HttpContext ctx) =>
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
}).WithTags("Forms");

app.MapPost("/forms/{id:guid}/biometric", [Authorize(Policy = "User")] async (Guid id, BiometricSubmitRequest req, AppDb db, HttpContext ctx) =>
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
}).WithTags("Forms");

app.Run();
