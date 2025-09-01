using System.ComponentModel.DataAnnotations;

namespace DynamicForms.Api.Dtos;

public sealed class TemplateUpdateRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }

    /// <summary>e.g., "draft", "archived"</summary>
    [MaxLength(20)]
    public string? Status { get; set; }
}

public sealed class TemplateVersionUpdateRequest
{
    /// <summary>Full JSON string for the template design; validated as JSON.</summary>
    public string? DesignJson { get; set; }

    /// <summary>Optional JSON Schema string; validated as JSON if present.</summary>
    public string? JsonSchema { get; set; }
}
