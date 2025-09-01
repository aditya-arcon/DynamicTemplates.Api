using System.Security.Claims;

namespace DynamicForms.Api.Extensions;

public static class HttpContextUserExtensions
{
    public static Guid UserId(this ClaimsPrincipal user)
    {
        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return id is null ? Guid.Empty : Guid.Parse(id);
    }
}
