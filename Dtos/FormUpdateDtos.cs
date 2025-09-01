using System.ComponentModel.DataAnnotations;

namespace DynamicForms.Api.Dtos;

public sealed class FormUpdateRequest
{
    /// <summary>in_progress | submitted | rejected | approved | canceled (free text in current model)</summary>
    public string? Status { get; set; }

    /// <summary>Assign to a user (Admin-only in these endpoints)</summary>
    public Guid? AssigneeUserId { get; set; }

    /// <summary>Applicant email (optional update)</summary>
    [EmailAddress]
    public string? Email { get; set; }

    /// <summary>E.164 phone format</summary>
    public string? PhoneE164 { get; set; }

    /// <summary>ISO country (free text currently)</summary>
    public string? Country { get; set; }

    /// <summary>Set explicitly (only meaningful when Status moves to 'submitted')</summary>
    public DateTimeOffset? SubmittedAt { get; set; }
}
