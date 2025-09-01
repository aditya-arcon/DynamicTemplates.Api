namespace DynamicForms.Api.Dtos;

public record RegisterRequest(string Email, string Password);
public record LoginRequest(string Email, string Password);
