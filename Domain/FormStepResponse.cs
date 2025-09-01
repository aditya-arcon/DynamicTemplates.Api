namespace DynamicForms.Api.Domain;

public class FormStepResponse
{
    public Guid StepResponseId { get; set; }
    public Guid InstanceId { get; set; }
    public string StepKey { get; set; } = default!;
    public int StepOrder { get; set; }
    public string DataJson { get; set; } = "{}";
    public string? ValidationErrors { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
