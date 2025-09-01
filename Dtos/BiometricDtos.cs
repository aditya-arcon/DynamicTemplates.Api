namespace DynamicForms.Api.Dtos;

public record BiometricSubmitRequest(
    string? VideoStorageKey,
    string? VideoMimeType,
    long? VideoSizeBytes,
    string? SelfieStorageKey,
    string? SelfieMimeType,
    long? SelfieSizeBytes,
    string? LivenessProvider,
    decimal? LivenessThreshold,
    decimal? LivenessScore,
    string? ChallengeType,
    int? FrameTimeMs,
    int RetryCount
);
