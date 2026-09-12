using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clio.Command.McpServer;
using Clio.Common;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Results;

/// <summary>
/// Shared, redacted, bounded formatting of an MCP <see cref="CallToolResult"/> payload for embedding in
/// parse-failure messages across every result parser in this folder. Extracted from
/// <c>EntitySchemaStructuredResultParser</c> (GitHub issue #1384), whose <c>Extract</c> failure originally
/// carried this diagnostic alone, so every sibling parser's bare "Could not parse ... MCP result." message
/// can show what the tool actually returned — whether the call reported an error, the last
/// <see cref="JsonException"/> encountered while trying to parse the payload, and the actual
/// <c>StructuredContent</c>/<c>Content</c> — instead of leaving the failure undiagnosable.
/// </summary>
internal static partial class McpResultDiagnostics {
    /// <summary>
    /// Default maximum number of characters of the composed diagnostic text embedded in a parse-failure
    /// message. Keeps a huge tool result from flooding CI logs while still showing its beginning, where
    /// the diagnostic text (an auth rejection, an HTML login page, a serialized exception) actually lives.
    /// A caller that prepends its own prefix passes a smaller budget, so the final message stays inside
    /// this number rather than exceeding it by the prefix and then reporting a second, meaningless
    /// "total".
    /// </summary>
    public const int PayloadDiagnosticLimit = 4_000;

    /// <summary>
    /// Maximum number of characters of a single RAW payload fragment handed to the redaction rules,
    /// applied before they run rather than only after. Whatever is cut here is reported inline as
    /// <c>…(+N more characters)</c>, so the true size of the payload is stated instead of being silently
    /// dropped.
    /// </summary>
    /// <remarks>
    /// <see cref="SensitiveErrorTextRedactor.Redact"/> runs ten or more backtracking scans, each with its
    /// own one-second timeout, and on timeout it discards its whole input and returns the bare
    /// <c>[redacted]</c> placeholder. A multi-megabyte tool result therefore did not merely take a long
    /// time — it replaced the diagnostic this class exists to produce with a single placeholder, which is
    /// exactly the blindness issue #1384 is about.
    /// <para>
    /// The cut boundary IS reachable in the rendered text: redaction SHRINKS its input (60 000 characters
    /// of URIs collapse to a few thousand <c>[redacted-uri]</c> placeholders), so a fragment cut at this
    /// limit can still end up inside the display budget. That is why both credential rules also match an
    /// unterminated quoted value running to the end of the input — a secret sliced as
    /// <c>…{"password":"s3c</c> would otherwise match neither rule, since both of the terminated forms
    /// require the closing quote, and half a secret would ship.
    /// </para>
    /// </remarks>
    public const int RawPayloadInputLimit = 64_000;

    private const string RedactedValue = "[redacted]";

    /// <summary>
    /// The secret-key words this harness redacts, mirroring
    /// <c>SensitiveErrorTextRedactor.CredentialPairRegex</c>'s own alternation. Kept <c>internal</c> so
    /// <c>McpResultDiagnosticsTests</c> can compare it against the production pattern and fail when a key
    /// is added there and not here.
    /// </summary>
    internal const string CredentialKeyCore =
        @"password|pwd|pass|secret|token|api[_-]?key|client[_-]?secret|client[_-]?id|private[_-]?key|" +
        @"access[_-]?key|connection ?string|data ?source|server|host|hostname|initial ?catalog|database|" +
        @"uid|user ?id|authorization|auth|bearer|set-cookie|cookie|asp\.net_sessionid|aspxauth|bpmcsrf|" +
        @"jsessionid|phpsessid|session[_-]?id|[xc]srf[_-]?token";

    // Keys matched EXACTLY, with no prefix/suffix tolerance. They exist only so a secret nested one level
    // deep under a secret key — {"token":{"value":"abc123"}} — is still redacted, since no regex here
    // matches a balanced object as a value. Expanding them fuzzily would redact "defaultValue",
    // "displayValue", "values" and most of a normal Creatio payload, which would cost more diagnostic
    // signal than the nesting case is worth.
    private const string ExactCredentialKeys = @"value|credentials|payload";

    // The key as it is actually spelled in an OAuth or connection payload: access_token, refreshToken,
    // idToken, dbPassword, clientId. Anchoring the alternation to the WHOLE quoted key matched none of
    // them, and OAuth envelopes are a real path through this harness. The prefix/suffix classes exclude
    // the quote character, so a match cannot run past the key.
    //
    // This over-redacts on purpose and the redactor's own policy says to: "tokenCount", "author",
    // "guid" and "serverName" will read [redacted]. Over-redacting a field that only helps a reader is
    // acceptable; leaking a credential into a build log everyone on the build can read is not.
    private const string CredentialKeyPattern =
        $@"[\w.-]*?(?:{CredentialKeyCore})[\w.-]*|{ExactCredentialKeys}";

    /// <summary>
    /// Characters held back from the content-block budget for the fields composed around it and for the
    /// "N more blocks" tail, so neither is pushed past the display cap.
    /// </summary>
    private const int TailHeadroom = 200;

    private const int RegexTimeoutMilliseconds = 1_000;

    // A JSON-quoted credential property — "password": "s3cr3t" — which the production redactor does NOT
    // reach: its CredentialPairRegex requires \b(key)\b\s*[=:], and the key's own CLOSING quote sits
    // between the key and the colon, so the pair never matches. Measured against the shipped rules:
    // Redact("{\"password\":\"s3cr3t\"}") returns that input verbatim.
    //
    // This post-pass is deliberately LOCAL to the e2e harness rather than a change to the production
    // redactor: that file is being moved and edited by two open pull requests (#1473, #1493), and issue
    // #1384 is a test-infrastructure change. The production gap is reported separately.
    //
    // The value alternation takes the terminated string first, then an UNTERMINATED one running to the
    // end of the input (a secret sliced by RawPayloadInputLimit), then the bare JSON literals. The whole
    // pair is rewritten in its JSON shape — "key":"[redacted]" — rather than the key=value shape the
    // production rule uses, so the surrounding dump stays readable as JSON instead of losing a quote.
    [GeneratedRegex(
        $@"""(?<key>{CredentialKeyPattern})""\s*:\s*(?:""[^""]*""|""[^""]*\z|null|true|false|-?\d+(?:\.\d+)?)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
    private static partial Regex JsonCredentialPropertyRegex();

    // The escaped-quote spellings of a JSON quote, captured into a group so the replacement puts back the
    // SAME form it found rather than mixing the two inside one document.
    private const string EscapedQuote = @"\\""|\\u0022";

    // The SAME pair after one level of JSON escaping. This is not a hypothetical shape — StructuredContent
    // routinely holds a string property whose value is itself serialized JSON (a nested envelope, a tool's
    // raw response body), and GetRawText() renders that inner document with every quote escaped, so the
    // plain rule above (which requires a literal quote immediately after the key) matches nothing at all
    // and the secret ships in the clear. BOTH escaped spellings are covered: a hand-written \"password\"
    // and the "password" that System.Text.Json's default encoder actually emits — measured, not
    // assumed: SerializeToElement(new { body = "{\"password\":\"s3cr3t\"}" }).GetRawText() produces the
    // " form, so a rule that knew only the backslash-quote spelling redacted nothing at all.
    [GeneratedRegex(
        $@"(?<q>{EscapedQuote})(?<key>{CredentialKeyPattern})\k<q>\s*:\s*(?:\k<q>[^""]*?\k<q>|\k<q>[^""]*\z|null|true|false|-?\d+(?:\.\d+)?)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeoutMilliseconds)]
    private static partial Regex EscapedJsonCredentialPropertyRegex();

    /// <summary>
    /// Describes an MCP tool result's payload for a parse-failure message: whether the call reported an
    /// error (<c>IsError</c>), the last <see cref="JsonException"/> encountered while trying to parse the
    /// payload (if the caller tracked one), and a redacted dump of both <c>StructuredContent</c> and every
    /// <c>Content</c> block's <c>type</c> plus its <c>text</c> — or, for a block that carries no
    /// <c>text</c> string at all (an image, an audio or an embedded-resource block), that block's own raw
    /// JSON, so a non-text answer is named rather than silently skipped.
    /// </summary>
    /// <remarks>
    /// The returned text is ALREADY redacted and already bounded to <paramref name="limit"/>. Bounding
    /// happens here, not at the call sites, so a caller that forgets cannot flood a CI log; and because
    /// every fragment is redacted BEFORE the composed text is capped, the cap can never cut a secret out
    /// of a redaction rule's reach. A caller that prepends its own prefix passes a REDUCED
    /// <paramref name="limit"/> instead of truncating the composed message a second time — truncating
    /// twice made the length note report the already-truncated size rather than the payload's.
    /// <para>
    /// This method never throws. Composing a diagnostic runs serialization and ten timed regexes over
    /// attacker-shaped input on a path whose whole job is to REPORT a failure, so an exception here would
    /// replace the parse failure the caller is trying to explain with an unrelated one.
    /// </para>
    /// </remarks>
    /// <param name="callResult">The tool result that could not be parsed, or <c>null</c> when none was available.</param>
    /// <param name="lastJsonException">
    /// The last <see cref="JsonException"/> raised while attempting to parse the payload, when the caller
    /// tracks one. Pass <c>null</c> when no parse attempt ever raised one.
    /// </param>
    /// <param name="limit">Maximum characters of the returned text. Defaults to <see cref="PayloadDiagnosticLimit"/>.</param>
    public static string Describe(
        CallToolResult? callResult,
        JsonException? lastJsonException = null,
        int limit = PayloadDiagnosticLimit) {
        if (callResult is null) {
            return "(no result)";
        }

        try {
            StringBuilder builder = new();
            builder.Append("IsError=").Append(callResult.IsError.ToString());

            if (lastJsonException is not null) {
                builder.Append(" LastJsonError=\"")
                    .Append(RedactBounded(lastJsonException.Message))
                    .Append('"');
            }

            bool hasStructuredContent = TrySerializeToJsonElement(callResult.StructuredContent, out JsonElement structuredContent);
            bool hasContent = TrySerializeToJsonElement(callResult.Content, out JsonElement content);

            builder.Append(" StructuredContent=").Append(DescribePayload(hasStructuredContent ? structuredContent : null));
            builder.Append(" Content=").Append(DescribeContentItems(hasContent ? content : null, limit));

            return Truncate(builder.ToString(), limit);
        }
        catch (Exception exception) {
            return $"(diagnostics unavailable: {exception.GetType().Name}: {SafeRedact(exception.Message)})";
        }
    }

    /// <summary>
    /// Describes an MCP tool result's payload, taking the last <see cref="JsonException"/> from the
    /// diagnostics a parser accumulated while it tried every accepted shape.
    /// </summary>
    public static string Describe(
        CallToolResult? callResult,
        McpParseDiagnostics diagnostics,
        int limit = PayloadDiagnosticLimit) =>
        Describe(callResult, diagnostics.LastJsonException, limit);

    /// <summary>
    /// Truncates <paramref name="text"/> to <paramref name="limit"/> characters and states both the kept
    /// and the original length, instead of embedding an unbounded tool result verbatim in an exception
    /// message. The cut never splits a surrogate pair, so an emoji near the boundary cannot leave invalid
    /// UTF-16 that breaks whatever serializes the message next.
    /// </summary>
    public static string Truncate(string text, int limit = PayloadDiagnosticLimit) {
        if (limit <= 0) {
            return string.Empty;
        }

        if (text.Length <= limit) {
            return text;
        }

        // The note is part of the budget, not an addition to it: appending it AFTER cutting to the limit
        // made the result limit + note characters, so a caller that subtracted its own prefix from the
        // budget still overshot. The note's own text depends only on the original length and the budget,
        // never on the kept count, so it can be measured before the cut.
        string note = $" … {text.Length} characters total, truncated to fit {limit}";
        return note.Length >= limit
            ? note[..limit]
            : TextUtilities.TruncateWithoutSplittingSurrogatePair(text, limit - note.Length) + note;
    }

    /// <summary>
    /// Bounds a raw payload fragment, then redacts it with the production rules, then closes the
    /// JSON-quoted credential gap those rules do not cover. Every string this class emits goes through
    /// here — a payload dump reaches a TeamCity build log, readable by everyone who can see the build.
    /// What the bound cut away is stated rather than dropped silently.
    /// </summary>
    private static string RedactBounded(string? rawText) {
        if (string.IsNullOrEmpty(rawText)) {
            return string.Empty;
        }

        if (rawText.Length <= RawPayloadInputLimit) {
            return RedactJsonCredentialProperties(SensitiveErrorTextRedactor.Redact(rawText));
        }

        // The marker goes in FRONT. Appended after the fragment it was correct and useless: it sat sixty
        // thousand characters into a four-thousand-character display budget, so the reader saw a cut
        // fragment and no statement that anything had been cut.
        string bounded = TextUtilities.TruncateWithoutSplittingSurrogatePair(rawText, RawPayloadInputLimit);
        return $"…({rawText.Length} characters, first {bounded.Length} shown) "
            + RedactJsonCredentialProperties(SensitiveErrorTextRedactor.Redact(bounded));
    }

    /// <summary>
    /// Applies the two JSON-quoted credential rules on top of an already production-redacted text. The
    /// escaped form runs first so the plain rule cannot match a fragment of it and leave a dangling
    /// backslash behind. A regex timeout collapses the fragment to the placeholder rather than returning
    /// text whose credential rules never finished running.
    /// </summary>
    private static string RedactJsonCredentialProperties(string text) =>
        SensitiveErrorTextRedactor.ExecuteRegex(() => {
            string result = EscapedJsonCredentialPropertyRegex().Replace(text, match => {
                string quote = match.Groups["q"].Value;
                return $"{quote}{match.Groups["key"].Value}{quote}:{quote}{RedactedValue}{quote}";
            });
            return JsonCredentialPropertyRegex().Replace(result,
                match => $"\"{match.Groups["key"].Value}\":\"{RedactedValue}\"");
        });

    /// <summary>Redaction that cannot itself throw, for the last-resort message composed in a catch block.</summary>
    private static string SafeRedact(string? text) {
        try {
            return RedactBounded(text);
        }
        catch (Exception) {
            return RedactedValue;
        }
    }

    private static string DescribePayload(JsonElement? element) {
        if (element is null) {
            return "(none)";
        }

        return RedactBounded(element.Value.GetRawText());
    }

    /// <summary>
    /// Renders each content block's <c>type</c> and, when present, its <c>text</c>. A block that carries
    /// no <c>text</c> string — an image, an audio or an embedded-resource block, or any shape this
    /// harness does not know — is dumped as its own raw JSON instead of being reported as "(no text)",
    /// which named the block's existence while discarding everything that said what the tool answered.
    /// </summary>
    /// <remarks>
    /// The per-fragment bound is not enough on its own: five hundred blocks of a hundred kilobytes each
    /// are five hundred fragments, so the builder would still reach tens of megabytes and pay ten timed
    /// regex scans per block — on the path that is supposed to REPORT a failure quickly. Blocks are
    /// therefore appended only while the rendered text stays under <paramref name="limit"/>, and the rest
    /// are counted rather than rendered.
    /// </remarks>
    private static string DescribeContentItems(JsonElement? content, int limit) {
        if (content is null) {
            return "(none)";
        }

        JsonElement contentElement = content.Value;
        if (contentElement.ValueKind != JsonValueKind.Array) {
            return RedactBounded(contentElement.GetRawText());
        }

        // Headroom for the fields composed around this section and for the "more blocks" tail, so both
        // stay inside the caller's budget and are actually READ. A tail that lands past the display cap
        // is the same as no tail at all.
        int blockBudget = Math.Max(limit - TailHeadroom, TailHeadroom);

        StringBuilder builder = new();
        builder.Append('[');
        bool isFirst = true;
        int skippedBlocks = 0;
        foreach (JsonElement item in contentElement.EnumerateArray()) {
            if (skippedBlocks > 0) {
                skippedBlocks++;
                continue;
            }

            StringBuilder itemBuilder = new();
            AppendContentItem(itemBuilder, item);

            // The FIRST block is always rendered even when it alone blows the budget: the outer cap and
            // the fragment's own size marker already describe that case, and rendering nothing at all
            // would leave the reader with no payload whatsoever - the failure this class exists to fix.
            if (!isFirst && builder.Length + itemBuilder.Length > blockBudget) {
                skippedBlocks = 1;
                continue;
            }

            if (!isFirst) {
                builder.Append(", ");
            }

            isFirst = false;
            builder.Append(itemBuilder);
        }

        if (skippedBlocks > 0) {
            builder.Append(", …, ").Append(skippedBlocks).Append(" more blocks");
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static void AppendContentItem(StringBuilder builder, JsonElement item) {
        bool isObject = item.ValueKind == JsonValueKind.Object;

        // The type is server-supplied text like everything else on this path, so it is redacted too
        // rather than being trusted because the protocol says it should be a short enum-like word.
        string itemType = isObject &&
            item.TryGetProperty("type", out JsonElement typeElement) &&
            typeElement.ValueKind == JsonValueKind.String
                ? RedactBounded(typeElement.GetString() ?? "(unknown)")
                : "(unknown)";

        if (isObject &&
            item.TryGetProperty("text", out JsonElement textElement) &&
            textElement.ValueKind == JsonValueKind.String) {
            builder.Append("{type=").Append(itemType)
                .Append(", text=\"").Append(RedactBounded(textElement.GetString())).Append("\"}");
            return;
        }

        // No text string: show the block itself rather than the fact that it had none. An image or
        // embedded-resource block, or a shape this harness has not seen, is exactly the case the bare
        // "(no text)" made undiagnosable.
        builder.Append("{type=").Append(itemType)
            .Append(", raw=").Append(RedactBounded(item.GetRawText())).Append('}');
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

/// <summary>
/// Accumulates what a result parser's parse attempt actually observed, so its failure message can name the
/// mismatch precisely — and, above all, can name the last <see cref="JsonException"/> instead of
/// discarding it inside a <c>catch (JsonException) { }</c> block (GitHub issue #1384). Shared by every
/// parser in this folder, so there is one definition of "what did we see" rather than one per envelope.
/// </summary>
internal sealed class McpParseDiagnostics {
    /// <summary>Whether any content item carried a non-blank <c>text</c> string.</summary>
    public bool SawTextPayload { get; set; }

    /// <summary>
    /// Whether a well-formed JSON value of ANY kind was handed to a deserialize attempt — an array
    /// included.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="SawValidJson"/> on purpose. The array-wrapper suppression below is about
    /// whose exception to BLAME; it must not make the parser claim there was no JSON. Without this flag a
    /// result whose StructuredContent is a JSON array reported "no structured content and no text content
    /// at all" directly beside a dump of that very array.
    /// </remarks>
    public bool SawAnyJson { get; private set; }

    /// <summary>Whether a JSON value that is a plausible candidate for the expected type was handed to a deserialize attempt.</summary>
    public bool SawValidJson { get; private set; }

    /// <summary>The last <see cref="JsonException"/> raised while parsing text as JSON or deserializing JSON as the expected type.</summary>
    public JsonException? LastJsonException { get; private set; }

    /// <summary>
    /// Records that <paramref name="element"/> is about to be deserialized as the expected type, and
    /// reports whether it is a MEANINGFUL candidate.
    /// </summary>
    /// <remarks>
    /// A JSON array reaching a deserialize call is, for every envelope type in this folder that is not
    /// itself array-shaped, the raw MCP content-item wrapper falling through (already unpacked, and known
    /// not to match) rather than a genuine candidate. The attempt is still made — it is the only path
    /// that could recognize a genuinely array-shaped type, and what counts as a successful parse must not
    /// change — but its doomed exception must not be blamed for a mismatch the real payload caused.
    /// The cost is deliberate and small: for the two list-returning parsers an array IS the expected
    /// shape, so a genuine array-shaped mismatch there yields no <c>LastJsonError</c> — the payload dump,
    /// which is what issue #1384 is actually about, still shows what came back.
    /// </remarks>
    public bool RecordDeserializeAttempt(JsonElement element) {
        bool isMeaningfulJsonCandidate = element.ValueKind != JsonValueKind.Array;
        // An EMPTY array is the "no content at all" case, not a payload: Content = [] serializes to [],
        // and counting it as JSON would make an empty result claim a shape mismatch it never saw.
        SawAnyJson |= isMeaningfulJsonCandidate || element.GetArrayLength() > 0;
        if (isMeaningfulJsonCandidate) {
            SawValidJson = true;
        }

        return isMeaningfulJsonCandidate;
    }

    /// <summary>Keeps <paramref name="exception"/> as the last parse failure, unless the attempt was the doomed array-wrapper one.</summary>
    public void RecordJsonException(JsonException exception, bool isMeaningfulJsonCandidate = true) {
        if (isMeaningfulJsonCandidate) {
            LastJsonException = exception;
        }
    }
}
