namespace DynamicForms.Api.Domain;

public class FileObject
{
    public Guid FileId { get; set; }
    public string StorageKey { get; set; } = default!;
    public string MimeType { get; set; } = default!;
    public long SizeBytes { get; set; }
    public string? Sha256Hex { get; set; }
    public bool EncryptedAtRest { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
