using System.Globalization;
using System.Text.Json;

namespace Vault.Core.Rpc;

/// <summary>
/// Tolerant readers for OMV's JSON: it comes from PHP, so a number can arrive as text and a field can be missing in one
/// OMV version. A missing or odd field is <c>null</c>, never an exception.
/// </summary>
public static class JsonReading
{
    public static string? ReadString(this JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value))
            {
                if (value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }

                if (value.ValueKind == JsonValueKind.Number)
                {
                    return value.GetRawText();
                }
            }
        }

        return null;
    }

    public static double? ReadDouble(this JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    public static long? ReadLong(this JsonElement element, string name) => element.ReadDouble(name) is { } number ? (long)number : null;
}
