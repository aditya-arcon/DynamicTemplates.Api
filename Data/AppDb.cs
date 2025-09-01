using DynamicForms.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace DynamicForms.Api.Data;

public class AppDb : DbContext
{
    public AppDb(DbContextOptions<AppDb> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Template> Templates => Set<Template>();
    public DbSet<TemplateVersion> TemplateVersions => Set<TemplateVersion>();
    public DbSet<FormInstance> FormInstances => Set<FormInstance>();
    public DbSet<FormStepResponse> FormStepResponses => Set<FormStepResponse>();
    public DbSet<IdentityDocument> IdentityDocuments => Set<IdentityDocument>();
    public DbSet<BiometricCapture> BiometricCaptures => Set<BiometricCapture>();
    public DbSet<FileObject> Files => Set<FileObject>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<User>(e =>
        {
            e.HasKey(x => x.UserId);
            e.HasIndex(x => x.Email).IsUnique();
        });

        b.Entity<Template>(e =>
        {
            e.HasKey(x => x.TemplateId);
            e.Property(x => x.Status).HasMaxLength(20);

            // Fix: Use matching precision defaults for MySQL datetime(6)
            e.Property(x => x.CreatedAt)
                .HasColumnType("datetime(6)")
                .HasDefaultValueSql("CURRENT_TIMESTAMP(6)");

            e.Property(x => x.UpdatedAt)
                .HasColumnType("datetime(6)")
                .HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            // If you want auto-update on UPDATE statements, consider:
            // .ValueGeneratedOnAddOrUpdate()
            // .HasComputedColumnSql("CURRENT_TIMESTAMP(6)", stored: false);
        });

        b.Entity<TemplateVersion>(e =>
        {
            e.HasKey(x => x.TemplateVersionId);
            e.HasIndex(x => new { x.TemplateId, x.Version }).IsUnique();
            e.Property(x => x.DesignJson).HasColumnType("json");
            e.Property(x => x.JsonSchema).HasColumnType("json");
        });

        b.Entity<FormInstance>(e =>
        {
            e.HasKey(x => x.InstanceId);
            e.HasOne<Template>().WithMany().HasForeignKey(x => x.TemplateId);
            e.HasIndex(x => x.Email);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => new { x.TemplateId, x.TemplateVersion });
        });

        b.Entity<FormStepResponse>(e =>
        {
            e.HasKey(x => x.StepResponseId);
            e.HasIndex(x => new { x.InstanceId, x.StepKey }).IsUnique();
            e.Property(x => x.DataJson).HasColumnType("json");
            e.Property(x => x.ValidationErrors).HasColumnType("json");
        });

        b.Entity<IdentityDocument>(e =>
        {
            e.HasKey(x => x.DocumentId);
            e.Property(x => x.OcrJson).HasColumnType("json");
        });

        b.Entity<BiometricCapture>(e =>
        {
            e.HasKey(x => x.BiometricId);
        });

        b.Entity<FileObject>(e =>
        {
            e.HasKey(x => x.FileId);
        });
    }
}
