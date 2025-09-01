namespace DynamicForms.Api.Dtos;

public sealed class BiometricUpdateRequest
{
    // Replace video file (optional)
    public string? VideoStorageKey { get; set; }
    public string? VideoMimeType { get; set; }
    public long? VideoSizeBytes { get; set; }

    // Replace selfie file (optional)
    public string? SelfieStorageKey { get; set; }
    public string? SelfieMimeType { get; set; }
    public long? SelfieSizeBytes { get; set; }

    public string? LivenessProvider { get; set; }
    public decimal? LivenessThreshold { get; set; }
    public decimal? LivenessScore { get; set; }
    public string? ChallengeType { get; set; }
    public int? FrameTimeMs { get; set; }
    public int? RetryCount { get; set; }
}
