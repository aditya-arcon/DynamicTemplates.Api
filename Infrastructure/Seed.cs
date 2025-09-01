using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using DynamicForms.Api.Data;
using DynamicForms.Api.Domain;

namespace DynamicForms.Api.Infrastructure;

public static class Seed
{
    public static async Task EnsureAdminAsync(IServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        if (await db.Users.AnyAsync()) return;

        var email = Environment.GetEnvironmentVariable("ADMIN_EMAIL") ?? "admin@example.com";
        var password = Environment.GetEnvironmentVariable("ADMIN_PASSWORD") ?? "ChangeMe123!";

        var admin = new User { UserId = Guid.NewGuid(), Email = email.ToLowerInvariant(), Role = "Admin" };
        admin.PasswordHash = hasher.HashPassword(admin, password);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
        Console.WriteLine($"Seeded default admin {email} / {password}");
    }
}
