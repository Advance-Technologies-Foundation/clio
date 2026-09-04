using System.Text;
using System.Text.Json;
using Clio.Command.McpServer;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Results;

/// <summary>
/// Shared, redacted, bounded formatting of an MCP <see cref="CallToolResult"/> payload for embedding in
/// parse-failure messages across every result parser in this folder. Extracted from
/// <c>EntitySchemaStructuredResultParser</c> (GitHub issue #1384), whose <c>Extract</c> failure originally
/// carried this diagnostic alone, so every sibling parser's bare "Could not parse ... MCP result." message
/// can show what the tool actually returned — whether the call reported an error, the last
/// <see cref="JsonException"/> encountered while trying to parse the payload (when the caller tracked one),
/// and the actual <c>StructuredContent</c>/<c>Content</c> — instead of leaving the failure undiagnosable.
/// </summary>
internal static class McpResultDiagnostics {
    /// <summary>
    /// Maximum number of characters of the composed diagnostic text embedded in a parse-failure message.
    /// Keeps a huge tool result from flooding CI logs while still showing its beginning, where the
    /// diagnostic text (an auth rejection, an HTML login page, a serialized exception) actually lives.
    /// </summary>
    public const int PayloadDiagnosticLimit = 4_000;

    /// <summary>
    /// Describes an MCP tool result's payload for a parse-failure message: whether the call reported an
    /// error (<c>IsError</c>), the last <see cref="JsonException"/> encountered while trying to parse the
    /// payload (if the caller tracked one), and a redacted dump of both <c>StructuredContent</c> and every
    /// <c>Content</c> item's <c>type</c>/<c>text</c>. The caller composes its own "Could not parse X MCP
    /// result" prefix (and, if it tracks one, its own shape classification) around this text, then bounds
    /// the final composed message with <see cref="Truncate"/>.
    /// </summary>
    /// <param name="callResult">The tool result that could not be parsed, or <c>null</c> when none was available.</param>
    /// <param name="lastJsonException">
    /// The last <see cref="JsonException"/> raised while attempting to parse the payload, when the caller
    /// tracks one. Pass <c>null</c> when the caller discards parse exceptions rather than threading them
    /// through its (possibly recursive) parse attempts.
    /// </param>
    public static string Describe(CallToolResult? callResult, JsonException? lastJsonException = null) {
        if (callResult is null) {
            return "(no result)";
        }

        StringBuilder builder = new();
        builder.Append("IsError=").Append(callResult.IsError.ToString());

        if (lastJsonException is not null) {
            builder.Append(" LastJsonError=\"")
                .Append(SensitiveErrorTextRedactor.Redact(lastJsonException.Message))
                .Append('"');
        }

        bool hasStructuredContent = TrySerializeToJsonElement(callResult.StructuredContent, out JsonElement structuredContent);
        bool hasContent = TrySerializeToJsonElement(callResult.Content, out JsonElement content);

        builder.Append(" StructuredContent=").Append(DescribePayload(hasStructuredContent ? structuredContent : null));
        builder.Append(" Content=").Append(DescribeContentItems(hasContent ? content : null));

        return builder.ToString();
    }

    /// <summary>
    /// Truncates <paramref name="text"/> to <see cref="PayloadDiagnosticLimit"/> characters and appends the
    /// original total length, instead of embedding an unbounded tool result verbatim in an exception message.
    /// </summary>
    public static string Truncate(string text) {
        if (text.Length <= PayloadDiagnosticLimit) {
            return text;
        }

        int totalLength = text.Length;
        return string.Concat(
            text.AsSpan(0, PayloadDiagnosticLimit),
            $" … truncated, {totalLength} characters total");
    }

    private static string DescribePayload(JsonElement? element) {
        if (element is null) {
            return "(none)";
        }

        return SensitiveErrorTextRedactor.Redact(element.Value.GetRawText());
    }

    /// <summary>
    /// Renders each content item's <c>type</c> and, when present, its <c>text</c> — instead of skipping
    /// silently over any item that is not an object carrying a <c>text</c> string, as a parser's own parse
    /// path may do.
    /// </summary>
    private static string DescribeContentItems(JsonElement? content) {
        if (content is null) {
            return "(none)";
        }

        JsonElement contentElement = content.Value;
        if (contentElement.ValueKind != JsonValueKind.Array) {
            return SensitiveErrorTextRedactor.Redact(contentElement.GetRawText());
        }

        StringBuilder builder = new();
        builder.Append('[');
        bool isFirst = true;
        foreach (JsonElement item in contentElement.EnumerateArray()) {
            if (!isFirst) {
                builder.Append(", ");
            }

            isFirst = false;

            string itemType = item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty("type", out JsonElement typeElement) &&
                typeElement.ValueKind == JsonValueKind.String
                    ? typeElement.GetString() ?? "(unknown)"
                    : "(unknown)";

            string itemText = item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty("text", out JsonElement textElement) &&
                textElement.ValueKind == JsonValueKind.String
                    ? SensitiveErrorTextRedactor.Redact(textElement.GetString())
                    : "(no text)";

            builder.Append("{type=").Append(itemType).Append(", text=\"").Append(itemText).Append("\"}");
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static bool TrySerializeToJsonElement(object? value, out JsonElement element) {
        if (value is null) {
            element = default;
            return false;
        }

        element = JsonSerializer.SerializeToElement(value);
        return true;
    }
}
