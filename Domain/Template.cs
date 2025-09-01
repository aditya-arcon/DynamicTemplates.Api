namespace DynamicForms.Api.Domain;

public class Template
{
    public Guid TemplateId { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string Status { get; set; } = "draft";
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
