namespace DynamicForms.Api.Domain;

public class BiometricCapture
{
    public Guid BiometricId { get; set; }
    public Guid InstanceId { get; set; }
    public Guid? VideoFileId { get; set; }
    public Guid SelfieFileId { get; set; }
    public string? LivenessProvider { get; set; }
    public decimal? LivenessThreshold { get; set; }
    public decimal? LivenessScore { get; set; }
    public string? ChallengeType { get; set; }
    public int? FrameTimeMs { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
