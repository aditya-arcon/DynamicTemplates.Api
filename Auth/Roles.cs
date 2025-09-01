namespace DynamicForms.Api.Auth;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Reviewer = "Reviewer";
    public const string User = "User";
}

public static class FormStatus
{
    public const string InProgress = "in_progress";
    public const string Submitted = "submitted";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Abandoned = "abandoned";
}

public static class TemplateStatus
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Archived = "archived";
}
