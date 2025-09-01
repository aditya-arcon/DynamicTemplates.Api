namespace DynamicForms.Api.Dtos;

public record DocumentSubmitRequest(
    string DocType,
    string? IssuingCountry,
    string? NumberRedacted,
    DateTime? ExpiryDate,
    string? Side,
    string? StorageKey,
    string? MimeType,
    long? SizeBytes,
    string? OcrJson
);
