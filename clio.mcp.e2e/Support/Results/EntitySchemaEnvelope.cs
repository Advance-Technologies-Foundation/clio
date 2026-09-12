using System.Text.Json;
using Clio.Command.McpServer;
using Clio.Common;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Results;

internal static class EntitySchemaStructuredResultParser {
    public static T Extract<T>(CallToolResult callResult) {
        McpParseDiagnostics diagnostics = new();

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

        throw new InvalidOperationException(message, RedactedCopyOf(diagnostics.LastJsonException));
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
        McpParseDiagnostics diagnostics) {
        string prefix = $"Could not parse {expectedTypeName} MCP result: {DescribeFailureShape(diagnostics)}. ";

        // The payload description is asked for a REDUCED budget rather than being truncated a second time
        // after the prefix is prepended. Truncating twice made the length note describe the already-cut
        // text — a three-megabyte payload was reported as "4114 characters" — and a note that lies about
        // the size is worse than no note.
        return prefix + McpResultDiagnostics.Describe(
            callResult,
            diagnostics.LastJsonException,
            McpResultDiagnostics.PayloadDiagnosticLimit - prefix.Length);
    }

    /// <summary>
    /// A copy of <paramref name="exception"/> whose message has been through the redactor, used as the
    /// thrown exception's inner exception.
    /// </summary>
    /// <remarks>
    /// The outer message is redacted; the inner exception was not, and NUnit prints inner exceptions in
    /// full. System.Text.Json failure messages carry a JSON path and an offset rather than values, so the
    /// risk is small — but "small" is not a property worth relying on for the one string on this path
    /// that skipped the rule everything else obeys. The exception TYPE is preserved, so a caller (and the
    /// existing assertion) still sees a <see cref="JsonException"/>.
    /// </remarks>
    private static JsonException? RedactedCopyOf(JsonException? exception) =>
        exception is null ? null : new JsonException(SensitiveErrorTextRedactor.Redact(exception.Message));

    private static string DescribeFailureShape(McpParseDiagnostics diagnostics) {
        if (diagnostics.SawValidJson) {
            return "JSON present but not shaped like the expected type";
        }

        if (diagnostics.SawTextPayload) {
            return "text content present but not JSON";
        }

        // SawAnyJson, not SawValidJson: an array-shaped StructuredContent sets only the former, and
        // claiming "no structured content ... at all" directly beside a dump of that array contradicts
        // the very text printed next to it.
        if (diagnostics.SawAnyJson) {
            return "JSON present but not shaped like the expected type";
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

    private static bool TryExtractEnvelope<T>(JsonElement element, out T? envelope, McpParseDiagnostics diagnostics) {
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

    private static bool TryDeserializeEnvelope<T>(JsonElement element, out T? envelope, McpParseDiagnostics diagnostics) {
        // The array-wrapper rule lives in McpParseDiagnostics.RecordDeserializeAttempt, which every sibling
        // parser now shares: the attempt is still made, but a bare content-item array must not be recorded
        // as "JSON was present" nor contribute its always-doomed JsonException.
        bool isMeaningfulJsonCandidate = diagnostics.RecordDeserializeAttempt(element);
        try {
            envelope = JsonSerializer.Deserialize<T>(
                element.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return envelope is not null;
        }
        catch (JsonException exception) {
            diagnostics.RecordJsonException(exception, isMeaningfulJsonCandidate);
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

    private static bool TryParseJson(string value, out JsonElement element, McpParseDiagnostics diagnostics) {
        try {
            element = JsonSerializer.SerializeToElement(JsonSerializer.Deserialize<JsonElement>(value));
            return true;
        }
        catch (JsonException exception) {
            diagnostics.RecordJsonException(exception);
            element = default;
            return false;
        }
    }

}
