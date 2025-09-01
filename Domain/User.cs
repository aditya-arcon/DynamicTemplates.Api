namespace DynamicForms.Api.Domain;

public class User
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = default!;
    public string Role { get; set; } = "User";
    public string? PasswordHash { get; set; }
}
