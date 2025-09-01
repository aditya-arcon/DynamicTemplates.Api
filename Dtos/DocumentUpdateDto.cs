namespace DynamicForms.Api.Dtos;

public sealed class DocumentUpdateRequest
{
    public string? DocType { get; set; }
    public string? IssuingCountry { get; set; }
    public string? NumberRedacted { get; set; }
    public DateTimeOffset? ExpiryDate { get; set; }
    public string? Side { get; set; }

    /// <summary>If provided, a NEW file will be created and linked to the document</summary>
    public string? StorageKey { get; set; }
    public string? MimeType { get; set; }
    public long? SizeBytes { get; set; }

    /// <summary>Optional OCR JSON (validated)</summary>
    public string? OcrJson { get; set; }
}
