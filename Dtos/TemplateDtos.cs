namespace DynamicForms.Api.Dtos;

public record TemplateCreateRequest(string Name, string? Description, string? CreatedBy);
public record TemplateVersionCreateRequest(string DesignJson, string? JsonSchema);
