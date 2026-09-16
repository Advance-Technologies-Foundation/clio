using System.Collections;
using System.Text;
using System.Text.Json;
using Clio.Common;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Results;

/// <summary>
/// Shared formatting of an MCP <see cref="CallToolResult"/> parse failure for every result parser in this
/// folder. Extracted from <c>EntitySchemaStructuredResultParser</c> (GitHub issue #1384), whose
/// <c>Extract</c> failure originally carried this diagnostic alone, so every sibling parser's bare
/// "Could not parse ... MCP result." message can say what the tool actually returned instead of leaving
/// the failure undiagnosable.
/// </summary>
/// <remarks>
/// The payload does NOT go into the message. It is written verbatim to its own file and the message names
/// that file (GitHub issue #1537).
/// <para>
/// The previous design embedded a bounded, redacted dump inline. Bounding it meant the reader saw a
/// fragment of a large answer; redacting it meant pushing up to 64 000 characters through
/// <see cref="SensitiveErrorTextRedactor.Redact"/>, which runs ten or more backtracking scans under a
/// one-second budget each and, on expiry, discards its whole input for the bare <c>[redacted]</c>
/// placeholder. Measured on the shipped rules, that pass costs ~118 ms at 64 000 characters and grows
/// superlinearly, so a loaded CI agent closed the remaining margin and the diagnostic this class exists to
/// produce collapsed into a placeholder — the very blindness issue #1384 is about, arriving through the
/// defence meant to prevent it.
/// </para>
/// <para>
/// Writing the payload to a file removes both costs at once: nothing payload-shaped reaches the build log,
/// so nothing about it needs bounding or redacting to get there, and the reader gets the answer whole
/// rather than its first few thousand characters.
/// </para>
/// </remarks>
internal static class McpResultDiagnostics {
	/// <summary>
	/// Longest message fragment this class composes from server-supplied text — the last
	/// <see cref="JsonException"/>'s message, and the excerpt emitted only when the dump could not be
	/// written.
	/// </summary>
	/// <remarks>
	/// These two, unlike the payload, DO reach the build log, so they are still both bounded and passed
	/// through <see cref="SensitiveErrorTextRedactor.Redact"/>. That is not a leftover of the design this
	/// class moved away from: at this size the redaction pass costs about a millisecond against its
	/// one-second budget — a margin of roughly a thousand, where the 64 000-character payload had about
	/// eight — so the timeout that made the old design flaky cannot be reached from here.
	/// </remarks>
	public const int LogFragmentLimit = 1_000;

	private const string DefaultLabel = "mcp-result";

	/// <summary>
	/// Where payload dumps go.
	/// </summary>
	/// <remarks>
	/// Deliberately <c>readonly</c> rather than a settable test seam. Some fixtures in this assembly carry
	/// <c>[Parallelizable(ParallelScope.Self)]</c>, so a test that swapped a shared static sink could be
	/// running while an unrelated e2e test hit a genuine parse failure, and that failure's payload would
	/// land in the swapped sink. Tests reach the seam through <see cref="DescribeWithSink"/> instead,
	/// which passes a sink down the call and shares nothing.
	/// </remarks>
	private static readonly IMcpPayloadDumpSink DefaultDumpSink = new TestResultsPayloadDumpSink();

	/// <summary>
	/// Describes an MCP tool result's parse failure: whether the call reported an error
	/// (<c>IsError</c>), the last <see cref="JsonException"/> encountered while trying to parse the
	/// payload (if the caller tracked one), the payload's shape, and the path of the file holding the
	/// payload itself.
	/// </summary>
	/// <remarks>
	/// This method never throws. Composing a diagnostic runs serialization and file IO over
	/// attacker-shaped input on a path whose whole job is to REPORT a failure, so an exception here would
	/// replace the parse failure the caller is trying to explain with an unrelated one.
	/// </remarks>
	/// <param name="callResult">The tool result that could not be parsed, or <c>null</c> when none was available.</param>
	/// <param name="lastJsonException">
	/// The last <see cref="JsonException"/> raised while attempting to parse the payload, when the caller
	/// tracks one. Pass <c>null</c> when no parse attempt ever raised one.
	/// </param>
	public static string Describe(CallToolResult? callResult, JsonException? lastJsonException = null) =>
		Describe(callResult, lastJsonException, DefaultLabel, DefaultDumpSink);

	/// <summary>
	/// Describes an MCP tool result's parse failure, taking the last <see cref="JsonException"/> from the
	/// diagnostics a parser accumulated while it tried every accepted shape.
	/// </summary>
	public static string Describe(CallToolResult? callResult, McpParseDiagnostics diagnostics) =>
		Describe(callResult, diagnostics.LastJsonException, DefaultLabel, DefaultDumpSink);

	/// <summary>
	/// Composes <paramref name="prefix"/> with the failure description. The prefix also names the dump
	/// file, so the artifact belonging to a given failure can be found without opening it.
	/// </summary>
	/// <param name="prefix">The caller's own sentence, already ending in whatever separator it wants.</param>
	/// <param name="callResult">The tool result that could not be parsed, or <c>null</c> when none was available.</param>
	/// <param name="lastJsonException">The last <see cref="JsonException"/> raised while parsing, when the caller tracks one.</param>
	public static string DescribePrefixed(
		string prefix,
		CallToolResult? callResult,
		JsonException? lastJsonException = null) =>
		prefix + Describe(callResult, lastJsonException, prefix, DefaultDumpSink);

	/// <summary>
	/// Composes <paramref name="prefix"/> with the failure description, taking the last
	/// <see cref="JsonException"/> from the diagnostics a parser accumulated.
	/// </summary>
	/// <param name="prefix">The caller's own sentence, already ending in whatever separator it wants.</param>
	/// <param name="callResult">The tool result that could not be parsed, or <c>null</c> when none was available.</param>
	/// <param name="diagnostics">The diagnostics accumulated while every accepted shape was tried.</param>
	public static string DescribePrefixed(
		string prefix,
		CallToolResult? callResult,
		McpParseDiagnostics diagnostics) =>
		DescribePrefixed(prefix, callResult, diagnostics.LastJsonException);

	/// <summary>
	/// The same description, written through a caller-supplied sink. The seam tests use so they can read
	/// what would be dumped without touching the filesystem.
	/// </summary>
	internal static string DescribeWithSink(
		CallToolResult? callResult,
		JsonException? lastJsonException,
		string label,
		IMcpPayloadDumpSink sink) =>
		Describe(callResult, lastJsonException, label, sink);

	private static string Describe(
		CallToolResult? callResult,
		JsonException? lastJsonException,
		string label,
		IMcpPayloadDumpSink sink) {
		if (callResult is null) {
			return "(no result)";
		}

		try {
			StringBuilder builder = new();
			builder.Append("IsError=").Append(callResult.IsError.ToString());

			if (lastJsonException is not null) {
				builder.Append(" LastJsonError=\"")
					.Append(RedactLogFragment(lastJsonException.Message))
					.Append('"');
			}

			builder.Append(" StructuredContent=")
				.Append(callResult.StructuredContent is null ? "(none)" : "present");
			builder.Append(" Content=").Append(DescribeContentShape(callResult.Content));
			builder.Append(' ').Append(DescribeDump(callResult, label, sink));

			return builder.ToString();
		}
		catch (Exception exception) {
			return $"(diagnostics unavailable: {exception.GetType().Name}: {SafeRedact(exception.Message)})";
		}
	}

	/// <summary>
	/// Serializes the whole result and writes it, returning either the dump's path or — when the write
	/// failed — the reason plus a bounded excerpt.
	/// </summary>
	/// <remarks>
	/// The excerpt exists because a failed write would otherwise leave the reader with no payload at all,
	/// which is exactly the state issue #1384 removed. A read-only directory, a full disk or a path length
	/// the Windows agent rejects must degrade the diagnostic, not erase it.
	/// <para>
	/// ACCEPTED LIMIT: the serialized result is held whole in memory and written in one call, with no size
	/// bound at all — the 64 000-character bound the old design used is gone on purpose, because a bounded
	/// dump is not a full-fidelity record. A pathological result (hundreds of blocks of hundreds of
	/// kilobytes) therefore costs its own size twice over on a path that is only reporting someone else's
	/// failure. That is deliberate and has not been observed: e2e payloads are tool answers from a sandbox
	/// stand, and the alternative reintroduces the very trade-off issue #1537 removed.
	/// </para>
	/// <para>
	/// "Raw" here means the result as it stands after the MCP SDK deserialized it: the original bytes are
	/// no longer available at this layer, and re-serializing the <see cref="CallToolResult"/> is the
	/// closest faithful record of what arrived. Nothing is bounded, filtered or redacted on the way to the
	/// file.
	/// </para>
	/// </remarks>
	private static string DescribeDump(CallToolResult callResult, string label, IMcpPayloadDumpSink sink) {
		string rawPayload = JsonSerializer.Serialize(callResult, RawDumpOptions);
		McpPayloadDumpResult dump = sink.Write(label, rawPayload);

		if (dump.Succeeded) {
			// Quoted, and therefore self-delimiting. A caller may append its own text after this
			// description (ApplicationToolE2ETests re-throws as "{message} Raw result: {description}"),
			// so a path that ended at the next whitespace-delimited token or at end-of-string could not
			// be recovered from the wrapped form. See PayloadDumpReader.
			return $"Payload=\"{dump.Path}\"";
		}

		return $"Payload=(dump failed: {dump.FailureReason}) "
			+ $"PayloadExcerpt=\"{RedactLogFragment(rawPayload)}\"";
	}

	/// <summary>
	/// Compact on purpose. Indenting inflated an already-unbounded payload — materially larger in memory,
	/// on disk and in the published artifact — for no gain: the dump is a file people open with <c>jq</c>
	/// or an editor, both of which pretty-print it themselves.
	/// </summary>
	private static readonly JsonSerializerOptions RawDumpOptions = new();

	/// <summary>
	/// Renders how many content blocks arrived, rather than their text. The count is what distinguishes
	/// "the tool answered nothing" from "the tool answered something this parser could not read", and it
	/// is the one part of the payload's description that is safe in a build log by construction.
	/// </summary>
	private static string DescribeContentShape(IList<ContentBlock>? content) {
		if (content is null) {
			return "(none)";
		}

		int count = content.Count;
		return count == 0 ? "(none)" : $"{count} block(s)";
	}

	/// <summary>
	/// Bounds a fragment destined for the build log, drops a value the bound cut through, and passes the
	/// result through the production redaction rules.
	/// </summary>
	/// <remarks>
	/// The middle step is not cosmetic. Bounding has to come FIRST — redacting megabytes is the cost this
	/// class exists to avoid — but the production rule states as an accepted limit that "a value sliced by
	/// an input cap before its closing quote is NOT matched"
	/// (<see cref="SensitiveErrorTextRedactor"/>'s <c>JsonCredentialPropertyRegex</c>). So a fragment cut
	/// as <c>…{"password":"s3c</c> would reach the log with the first characters of a real secret in it,
	/// matching no rule at all. Discarding the unterminated value closes that without asking the redactor
	/// to reason about a truncated document.
	/// </remarks>
	private static string RedactLogFragment(string? text) =>
		string.IsNullOrEmpty(text)
			? string.Empty
			: SensitiveErrorTextRedactor.Redact(
				Truncate(text, LogFragmentLimit, DropValueCutByTheBound));

	/// <summary>
	/// Removes a trailing quoted value the bound cut through, so half a secret cannot ship where a whole
	/// one would have been redacted.
	/// </summary>
	/// <remarks>
	/// Counts unescaped quotes rather than matching a pattern: an odd count means the fragment ends INSIDE
	/// a JSON string, and everything from that opening quote on is an unterminated value. A single scan,
	/// with no backtracking and therefore none of the timeout exposure that motivated this whole redesign.
	/// </remarks>
	private static string DropValueCutByTheBound(string fragment) {
		int lastOpeningQuote = -1;
		bool insideString = false;
		for (int index = 0; index < fragment.Length; index++) {
			char character = fragment[index];
			if (character == '\\') {
				index++;
				continue;
			}

			if (character != '"') {
				continue;
			}

			insideString = !insideString;
			if (insideString) {
				lastOpeningQuote = index;
			}
		}

		return insideString && lastOpeningQuote >= 0
			? fragment[..lastOpeningQuote] + "\"(value cut by the excerpt bound, not shown)"
			: fragment;
	}

	/// <summary>Redaction that cannot itself throw, for the last-resort message composed in a catch block.</summary>
	private static string SafeRedact(string? text) {
		try {
			return RedactLogFragment(text);
		}
		catch (Exception) {
			return "[redacted]";
		}
	}

	/// <summary>
	/// Truncates <paramref name="text"/> to <paramref name="limit"/> characters and states both the kept
	/// and the original length, instead of embedding an unbounded fragment in an exception message. The
	/// cut never splits a surrogate pair, so an emoji near the boundary cannot leave invalid UTF-16 that
	/// breaks whatever serializes the message next.
	/// </summary>
	/// <param name="text">The text to bound.</param>
	/// <param name="limit">Maximum length of the result, the note included.</param>
	/// <param name="sanitizeCutText">
	/// Applied to the kept text BEFORE the note is appended, for a caller that must also remove something
	/// the cut itself created. Runs before the note so it cannot discard it.
	/// </param>
	public static string Truncate(
		string text,
		int limit = LogFragmentLimit,
		Func<string, string>? sanitizeCutText = null) {
		if (limit <= 0) {
			return string.Empty;
		}

		if (text.Length <= limit) {
			return text;
		}

		// The note is part of the budget, not an addition to it: appending it AFTER cutting to the limit
		// made the result limit + note characters. The note's own text depends only on the original length
		// and the budget, never on the kept count, so it can be measured before the cut.
		string note = $" … {text.Length} characters total, truncated to fit {limit}";
		if (note.Length >= limit) {
			return note[..limit];
		}

		int keptBudget = limit - note.Length;
		string kept = TextUtilities.TruncateWithoutSplittingSurrogatePair(text, keptBudget);
		if (sanitizeCutText is not null) {
			// Re-applied AFTER sanitizing, because a sanitizer may GROW the text rather than only shrink
			// it: DropValueCutByTheBound replaces the tail from an unterminated opening quote with a
			// 42-character marker, which overflows the documented limit when the cut quote sits near the
			// end of the fragment. Cutting again keeps the invariant this method states for itself.
			kept = TextUtilities.TruncateWithoutSplittingSurrogatePair(sanitizeCutText(kept), keptBudget);
		}

		return kept + note;
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
	/// The array-wrapper suppression below is about whose exception to BLAME; it must not make the
	/// parser claim there was no JSON. Without this flag a result whose StructuredContent is a JSON
	/// array reported "no structured content and no text content at all" directly beside a dump of that
	/// very array.
	/// </remarks>
	public bool SawAnyJson { get; private set; }

	/// <summary>
	/// Whether a JSON value that is a plausible candidate FOR THE EXPECTED TYPE was handed to a
	/// deserialize attempt.
	/// </summary>
	/// <remarks>
	/// Narrower than <see cref="SawAnyJson"/>, and not redundant with it: the MCP content-item wrapper is
	/// itself a non-empty array, so <see cref="SawAnyJson"/> is true on every result that carries any
	/// content at all - including a plain text block that is not JSON. Only this flag can tell a caller
	/// that the shape mismatch is about the payload rather than about the wrapper, which is why the
	/// failure-shape description tests it before the text-payload branch.
	/// </remarks>
	public bool SawValidJson { get; private set; }

	/// <summary>The last <see cref="JsonException"/> raised while parsing text as JSON or deserializing JSON as the expected type.</summary>
	public JsonException? LastJsonException { get; private set; }

	/// <summary>
	/// Records that <paramref name="element"/> is about to be deserialized as <paramref name="expectedType"/>,
	/// and reports whether it is a MEANINGFUL candidate.
	/// </summary>
	/// <remarks>
	/// A JSON array reaching a deserialize call is, for an OBJECT-shaped expected type, the raw MCP
	/// content-item wrapper falling through (already unpacked, and known not to match) rather than a
	/// genuine candidate. The attempt is still made — it is the only path that could recognize a
	/// genuinely array-shaped type, and what counts as a successful parse must not change — but its
	/// doomed exception must not be blamed for a mismatch the real payload caused.
	/// <para>
	/// When the expected type IS array-shaped (<c>ShowWebAppListEnvelope.TryDeserialize</c>, and
	/// <c>EntitySchemaStructuredResultParser.Extract&lt;T&gt;</c> with a collection <c>T</c>) an array is
	/// exactly the shape that parser wants, so its exception is the real one and is kept. Suppressing it
	/// there reproduced, for those two parsers, the very swallowed-exception state issue #1384 exists to
	/// remove: a malformed environment entry in <c>show-webApp-list</c> failed with no <c>LastJsonError=</c>.
	/// </para>
	/// </remarks>
	/// <param name="element">The JSON value about to be deserialized.</param>
	/// <param name="expectedType">The type the caller is deserializing into.</param>
	public bool RecordDeserializeAttempt(JsonElement element, Type expectedType) {
		bool isMeaningfulJsonCandidate =
			element.ValueKind != JsonValueKind.Array || IsArrayShaped(expectedType);
		// An EMPTY array is the "no content at all" case, not a payload: Content = [] serializes to [],
		// and counting it as JSON would make an empty result claim a shape mismatch it never saw.
		SawAnyJson |= element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > 0;
		SawValidJson |= isMeaningfulJsonCandidate;

		return isMeaningfulJsonCandidate;
	}

	/// <summary>
	/// Whether <paramref name="type"/> deserializes FROM a JSON array — an array, or any non-string
	/// enumerable such as <c>IReadOnlyList&lt;T&gt;</c>. <see cref="string"/> is excluded because it is
	/// enumerable but deserializes from a JSON string.
	/// </summary>
	private static bool IsArrayShaped(Type type) {
		Type target = Nullable.GetUnderlyingType(type) ?? type;
		return target != typeof(string) && typeof(IEnumerable).IsAssignableFrom(target);
	}

	/// <summary>Keeps <paramref name="exception"/> as the last parse failure, unless the attempt was the doomed array-wrapper one.</summary>
	public void RecordJsonException(JsonException exception, bool isMeaningfulJsonCandidate = true) {
		if (isMeaningfulJsonCandidate) {
			LastJsonException = exception;
		}
	}
}
