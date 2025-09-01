using System.Text.Json;

namespace DynamicForms.Api.Extensions;

public static class JsonValidation
{
    public static bool JsonIsValid(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try { JsonDocument.Parse(json); return true; } catch { return false; }
    }
}
