namespace DynamicForms.Api.Domain;

public class FormInstance
{
    public Guid InstanceId { get; set; }
    public Guid TemplateId { get; set; }
    public int TemplateVersion { get; set; }
    public Guid? AssigneeUserId { get; set; }
    public string Status { get; set; } = "in_progress";

    // denormalized
    public string? Email { get; set; }
    public string? PhoneE164 { get; set; }
    public string? Country { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }
}
