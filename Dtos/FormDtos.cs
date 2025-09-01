namespace DynamicForms.Api.Dtos;

public record FormCreateRequest(Guid TemplateId, int? TemplateVersion, string? Email, string? Phone, string? Country);
public record StepSubmitRequest(string DataJson);
