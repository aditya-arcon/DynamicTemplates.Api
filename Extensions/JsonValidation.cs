// src/DynamicForms.Api/Extensions/JsonValidation.cs
using System.Text.Json;

namespace DynamicForms.Api.Extensions
{
    /// <summary>
    /// JSON validation helpers + extension method for strings.
    /// </summary>
    public static class JsonValidation
    {
        /// <summary>
        /// Extension: returns true if the string is non-empty and parses as JSON.
        /// </summary>
        public static bool IsValidJson(this string? json)
            => JsonIsValid(json);

        /// <summary>
        /// Static helper: returns true if the string is non-empty and parses as JSON.
        /// </summary>
        public static bool JsonIsValid(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                using var _ = JsonDocument.Parse(json);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
