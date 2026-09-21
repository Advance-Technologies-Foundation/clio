using System.Text.Json;
using System.Text.Json.Serialization;
using Clio10.Contracts;
namespace Clio10.AdapterShared;

// Compiled into each adapter: JSON conversion stays outside the reusable workflow contracts.
internal static class JsonArguments {
    internal static readonly JsonSerializerOptions Output = new() { Converters = { new JsonStringEnumConverter(), new EnvironmentOutputConverter() } };
    internal static IReadOnlyDictionary<string, object?> Read(string json) {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Arguments must be an object.");
        return (IReadOnlyDictionary<string, object?>)Convert(document.RootElement)!;
    }
    internal static object? Convert(JsonElement value) => value.ValueKind switch {
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(x => x.Name, x => Convert(x.Value), StringComparer.Ordinal),
        JsonValueKind.Array => value.EnumerateArray().Select(Convert).ToArray(),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => Number(value),
        JsonValueKind.Null => null,
        _ => throw new JsonException("Unsupported argument value.")
    };
    private static object Number(JsonElement value) {
        if (value.TryGetInt64(out long integer)) return integer;
        if (value.TryGetDecimal(out decimal precise)) return precise;
        if (value.TryGetDouble(out double number) && double.IsFinite(number)) return number;
        throw new JsonException("Numbers must be finite and representable.");
    }
}

// Presentation-only redaction. Core's settings deserialization and serializer-neutral contracts stay unchanged.
internal sealed class EnvironmentOutputConverter : JsonConverter<ClioEnvironment> {
    public override ClioEnvironment Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException("This converter is for output only.");
    public override void Write(Utf8JsonWriter writer, ClioEnvironment value, JsonSerializerOptions options) {
        writer.WriteStartObject();
        writer.WriteString(nameof(value.BaseUri), value.BaseUri?.AbsoluteUri);
        writer.WriteString(nameof(value.UserName), value.UserName);
        writer.WriteBoolean(nameof(value.IsNetCore), value.IsNetCore);
        writer.WriteBoolean(nameof(value.AllowUntrustedCertificate), value.AllowUntrustedCertificate);
        writer.WriteEndObject();
    }
}
