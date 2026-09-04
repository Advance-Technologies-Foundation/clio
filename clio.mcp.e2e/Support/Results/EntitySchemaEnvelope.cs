using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Results;

internal static class EntitySchemaStructuredResultParser {
    public static T Extract<T>(CallToolResult callResult) {
        ParseDiagnostics diagnostics = new();

        bool hasStructuredContent = TrySerializeToJsonElement(callResult.StructuredContent, out JsonElement structuredContent);
        if (hasStructuredContent &&
            TryExtractEnvelope(structuredContent, out T? structuredEnvelope, diagnostics)) {
            return structuredEnvelope!;
        }

        bool hasContent = TrySerializeToJsonElement(callResult.Content, out JsonElement content);
        if (hasContent &&
            TryExtractEnvelope(content, out T? contentEnvelope, diagnostics)) {
            return contentEnvelope!;
        }

        string message = BuildParseFailureMessage(typeof(T).Name, callResult, diagnostics);

        throw new InvalidOperationException(message, diagnostics.LastJsonException);
    }

    /// <summary>
    /// Composes the parse-failure diagnostic: what shape was expected, and — via
    /// <see cref="McpResultDiagnostics.Describe"/> — whether the call reported an error and a bounded,
    /// redacted dump of the actual payload, so an authentication rejection, an HTML login page, a
    /// serialized unhandled exception, and a plain DTO-shape mismatch are no longer indistinguishable
    /// from a bare "could not parse" message.
    /// </summary>
    private static string BuildParseFailureMessage(
        string expectedTypeName,
        CallToolResult callResult,
        ParseDiagnostics diagnostics) {
        StringBuilder builder = new();
        builder.Append("Could not parse ").Append(expectedTypeName).Append(" MCP result: ")
            .Append(DescribeFailureShape(diagnostics)).Append('.');
        builder.Append(' ').Append(McpResultDiagnostics.Describe(callResult, diagnostics.LastJsonException));

        return McpResultDiagnostics.Truncate(builder.ToString());
    }

    private static string DescribeFailureShape(ParseDiagnostics diagnostics) {
        if (diagnostics.SawValidJson) {
            return "JSON present but not shaped like the expected type";
        }

        if (diagnostics.SawTextPayload) {
            return "text content present but not JSON";
        }

        return "no structured content and no text content at all";
    }

    private static bool TrySerializeToJsonElement(object? value, out JsonElement element) {
        if (value is null) {
            element = default;
            return false;
        }

        element = JsonSerializer.SerializeToElement(value);
        return true;
    }

    private static bool TryExtractEnvelope<T>(JsonElement element, out T? envelope, ParseDiagnostics diagnostics) {
        if (element.ValueKind == JsonValueKind.Array) {
            foreach (JsonElement item in element.EnumerateArray()) {
                if (TryGetTextPayload(item, out string? textPayload) &&
                    !string.IsNullOrWhiteSpace(textPayload)) {
                    diagnostics.SawTextPayload = true;
                    if (TryParseJson(textPayload, out JsonElement parsedPayload, diagnostics) &&
                        TryDeserializeEnvelope(parsedPayload, out envelope, diagnostics)) {
                        return true;
                    }
                }
            }
        }

        if (element.ValueKind == JsonValueKind.String) {
            string? textPayload = element.GetString();
            if (!string.IsNullOrWhiteSpace(textPayload)) {
                diagnostics.SawTextPayload = true;
                if (TryParseJson(textPayload, out JsonElement parsedPayload, diagnostics) &&
                    TryDeserializeEnvelope(parsedPayload, out envelope, diagnostics)) {
                    return true;
                }
            }
        }

        if (TryDeserializeEnvelope(element, out envelope, diagnostics)) {
            return true;
        }

        envelope = default;
        return false;
    }

    private static bool TryDeserializeEnvelope<T>(JsonElement element, out T? envelope, ParseDiagnostics diagnostics) {
        // A JSON array is, at this call site, always the raw MCP content-item wrapper falling through to this
        // last-resort attempt (already unpacked, and known not to match) rather than a genuine JSON candidate for
        // T — attempting to deserialize it is kept (it must still be tried, since it is the ONLY path that could
        // ever recognize a genuinely array-shaped T and this must not change what counts as a successful parse),
        // but it must not be recorded as "JSON was present" or contribute its always-doomed JsonException, or the
        // diagnostic would misreport a bare array wrapper as "JSON present but not shaped like T".
        bool isMeaningfulJsonCandidate = element.ValueKind != JsonValueKind.Array;
        if (isMeaningfulJsonCandidate) {
            diagnostics.SawValidJson = true;
        }
        try {
            envelope = JsonSerializer.Deserialize<T>(
                element.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return envelope is not null;
        }
        catch (JsonException exception) {
            if (isMeaningfulJsonCandidate) {
                diagnostics.LastJsonException = exception;
            }
            envelope = default;
            return false;
        }
    }

    private static bool TryGetTextPayload(JsonElement element, out string? textPayload) {
        textPayload = null;
        if (element.ValueKind != JsonValueKind.Object) {
            return false;
        }

        if (element.TryGetProperty("text", out JsonElement textElement) &&
            textElement.ValueKind == JsonValueKind.String) {
            textPayload = textElement.GetString();
            return true;
        }

        return false;
    }

    private static bool TryParseJson(string value, out JsonElement element, ParseDiagnostics diagnostics) {
        try {
            element = JsonSerializer.SerializeToElement(JsonSerializer.Deserialize<JsonElement>(value));
            return true;
        }
        catch (JsonException exception) {
            diagnostics.LastJsonException = exception;
            element = default;
            return false;
        }
    }

    /// <summary>
    /// Accumulates what the parse attempt actually observed, so the failure message can name the
    /// expected shape mismatch precisely instead of guessing beyond what the code knows.
    /// </summary>
    private sealed class ParseDiagnostics {
        /// <summary>Whether any content item carried a non-blank <c>text</c> string.</summary>
        public bool SawTextPayload { get; set; }

        /// <summary>Whether a well-formed JSON value was ever handed to a deserialize attempt against the expected type.</summary>
        public bool SawValidJson { get; set; }

        /// <summary>The last <see cref="JsonException"/> raised while parsing text as JSON or deserializing JSON as the expected type.</summary>
        public JsonException? LastJsonException { get; set; }
    }
}
