namespace DynamicForms.Api.Domain;

public class IdentityDocument
{
    public Guid DocumentId { get; set; }
    public Guid InstanceId { get; set; }
    public string DocType { get; set; } = default!;
    public string? IssuingCountry { get; set; }
    public string? NumberRedacted { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string Side { get; set; } = "single"; // front|back|single
    public Guid FileId { get; set; }
    public string? OcrJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
