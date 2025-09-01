namespace DynamicForms.Api.Domain;

public class TemplateVersion
{
    public Guid TemplateVersionId { get; set; }
    public Guid TemplateId { get; set; }
    public int Version { get; set; }
    public bool IsPublished { get; set; }
    public string DesignJson { get; set; } = "{}"; // MySQL json
    public string? JsonSchema { get; set; }        // MySQL json
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
