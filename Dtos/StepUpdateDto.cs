namespace DynamicForms.Api.Dtos;

public sealed class StepUpdateRequest
{
    /// <summary>Raw JSON string payload for the step (validated)</summary>
    public string DataJson { get; set; } = "{}";
}
