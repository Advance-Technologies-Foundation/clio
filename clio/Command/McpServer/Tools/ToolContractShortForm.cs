using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// ENG-100154: the SHORT form of a named <c>get-tool-contract</c> lookup, and the default rule that fits a
/// named lookup's reply inside one inline tool result.
/// </summary>
/// <remarks>
/// <para>
/// A named lookup used to return every requested contract in full. The process-designer contracts are
/// 31-35 KB each - almost all of it one description - and a single call asking for seven tools answered
/// with well over 100 KB. Agent CLIs do not show a tool result that large inline: they spill it to a file,
/// and the agent then spends several shell turns grepping it back in pieces (measured 3-9 per process run
/// in the CAADT gate, against 0-2 for other teams). One logical read became a dump and a scavenger hunt.
/// </para>
/// <para>
/// The default is therefore FIT, not "always short": the reply is returned in full whenever it fits the
/// budget, and only when it does not are the LARGEST contracts replaced by their short form, one at a
/// time, until it does. A small contract - which is most of them, and where examples earn their place -
/// is never touched. The short form keeps everything a caller decides at call time with: the purpose and
/// the safety lead the description opens with, the input schema with its required list and validators,
/// the error codes, preconditions, aliases, defaults, flows, deprecations and anti-patterns. It leaves out
/// the description's tail and the examples, trims each field description to its first sentence, and says
/// so in the description itself, together with how to get the rest (<c>detail="full"</c>).
/// </para>
/// </remarks>
internal static class ToolContractShortForm {

	/// <summary>The <c>detail</c> value that returns every named contract in full, however large.</summary>
	internal const string FullDetail = "full";

	/// <summary>The <c>detail</c> value that returns every named contract in its short form.</summary>
	internal const string ShortDetail = "short";

	/// <summary>
	/// Serialized size a default named lookup is fitted under, in UTF-8 bytes of the reply.
	/// </summary>
	/// <remarks>
	/// Below the smallest spill observed in the CAADT transcripts (21.3 KB, Copilot CLI) with room for the
	/// MCP envelope, and far below Claude Code's token-based limit. Seven process-designer contracts - the
	/// largest request measured in those transcripts - fit it in their short forms. Measured with the default JSON encoder,
	/// which escapes non-ASCII and so over-counts the real wire size - the safe direction for a ceiling.
	/// </remarks>
	internal const int InlineReplyBudgetBytes = 18 * 1024;

	/// <summary>
	/// Longest description lead the short form keeps for a DESTRUCTIVE tool. Such a contract opens with its
	/// purpose and then its call-time safety warning (the repository convention the compact index relies
	/// on), and the longest of those leads - modify-business-process's, overwrite and access-rights
	/// confirmation together - ends inside this bound.
	/// </summary>
	internal const int MaxLeadChars = 1500;

	/// <summary>
	/// Longest description lead the short form keeps for any OTHER tool: its purpose and the sentences right
	/// after it. A tool with no destructive effect has no confirmation to carry, so it gives up the room -
	/// which is what lets a request for several contracts fit at all.
	/// </summary>
	internal const int MaxNonDestructiveLeadChars = 600;

	/// <summary>Longest field description the short form keeps: its first sentence, capped here.</summary>
	internal const int MaxFieldDescriptionChars = 200;

	/// <summary>
	/// The phrase every clio tool description uses to state a confirmation duty - "... and get an explicit
	/// yes". A destructive tool's short form keeps each clause carrying it, wherever it stands.
	/// </summary>
	/// <remarks>
	/// The lead covers the warning a description OPENS with, but the process-designer descriptions state a
	/// second duty deep inside their element reference: a deleteData element deletes records, and the
	/// description tells the caller to count them, name the object and get a yes before building one. A
	/// short form that dropped that sentence would hand a caller the tool without the duty. The phrase is
	/// the marker because it is how the whole catalog words the duty; a duty worded otherwise is not seen,
	/// which ToolContractShortFormTests pins over the real catalog.
	/// </remarks>
	internal const string ConfirmationMarker = "explicit yes";

	/// <summary>Longest confirmation clause kept from outside the lead.</summary>
	internal const int MaxConfirmationClauseChars = 600;

	/// <summary>
	/// Applies the requested <paramref name="detail"/> to a named lookup's resolved contracts.
	/// </summary>
	/// <param name="contracts">The resolved full contracts, in request order.</param>
	/// <param name="detail"><c>full</c>, <c>short</c>, or anything else (including null) for the default fit.</param>
	/// <param name="measureReply">Serialized size of the whole reply that would carry the given contracts.</param>
	/// <param name="isDestructive">
	/// Whether a tool is destructive; decides how much of its description the short form keeps. Answer
	/// <see langword="true"/> when unknown - keeping a warning is the safe mistake.
	/// </param>
	/// <returns>The contracts to return, in the same order.</returns>
	internal static IReadOnlyList<ToolContractDefinition> Apply(
		IReadOnlyList<ToolContractDefinition> contracts,
		string? detail,
		Func<IReadOnlyList<ToolContractDefinition>, int> measureReply,
		Func<string, bool> isDestructive) {
		if (IsDetail(detail, FullDetail)) {
			return contracts;
		}
		if (IsDetail(detail, ShortDetail)) {
			return contracts.Select(contract => Shorten(contract, isDestructive(contract.Name))).ToArray();
		}
		return Fit(contracts, measureReply, isDestructive);
	}

	/// <summary>
	/// The default: shortens the largest full contract, one at a time, until the reply fits
	/// <see cref="InlineReplyBudgetBytes"/> or nothing is left to shorten. Deterministic for a given request.
	/// </summary>
	internal static IReadOnlyList<ToolContractDefinition> Fit(
		IReadOnlyList<ToolContractDefinition> contracts,
		Func<IReadOnlyList<ToolContractDefinition>, int> measureReply,
		Func<string, bool> isDestructive) {
		ToolContractDefinition[] fitted = [.. contracts];
		while (measureReply(fitted) > InlineReplyBudgetBytes) {
			int largest = -1;
			int largestBytes = -1;
			for (int index = 0; index < fitted.Length; index++) {
				if (IsShort(fitted[index])) {
					continue;
				}
				int bytes = MeasureBytes(fitted[index]);
				if (bytes > largestBytes) {
					largest = index;
					largestBytes = bytes;
				}
			}
			if (largest < 0) {
				// Everything is already short and the reply still exceeds the budget: a request for that
				// many tools cannot fit, and returning less than was asked for would be worse than a spill.
				break;
			}
			fitted[largest] = Shorten(fitted[largest], isDestructive(fitted[largest].Name));
		}
		return fitted;
	}

	/// <summary>
	/// Builds the short form of one contract. Idempotent: a contract already short is returned unchanged.
	/// </summary>
	/// <param name="contract">The full contract.</param>
	/// <param name="destructive">Whether the tool is destructive: its lead keeps room for the safety warning.</param>
	internal static ToolContractDefinition Shorten(ToolContractDefinition contract, bool destructive) {
		if (IsShort(contract)) {
			return contract;
		}
		int fullBytes = MeasureBytes(contract);
		string description = contract.Description ?? string.Empty;
		string lead = BuildLead(description, destructive ? MaxLeadChars : MaxNonDestructiveLeadChars);
		if (destructive) {
			lead = AppendConfirmationClauses(description, lead);
		}
		IReadOnlyList<ToolContractField> inputFields = TrimFields(contract.InputSchema?.Properties);
		IReadOnlyList<ToolContractField> outputFields = TrimFields(contract.OutputContract?.Fields);
		bool fieldsTrimmed = !SameDescriptions(inputFields, contract.InputSchema?.Properties)
			|| !SameDescriptions(outputFields, contract.OutputContract?.Fields);
		string note = BuildNote(Math.Max(0, description.Length - lead.Length), contract.Examples?.Count ?? 0,
			fieldsTrimmed);
		return contract with {
			Description = note.Length == 0 ? lead : (lead.TrimEnd() + " " + note),
			InputSchema = contract.InputSchema is null ? null : contract.InputSchema with { Properties = inputFields },
			OutputContract = contract.OutputContract is null
				? null
				: contract.OutputContract with { Fields = outputFields },
			Examples = [],
			Detail = ShortDetail,
			FullContractBytes = fullBytes
		};
	}

	/// <summary>
	/// The longest prefix of <paramref name="description"/> that ends on a sentence boundary and is at most
	/// <paramref name="maxChars"/> long; a text already within the bound is returned whole. When even the
	/// first sentence is longer, the text is cut at the last word boundary inside the bound, with an ellipsis.
	/// </summary>
	/// <remarks>
	/// Sentence boundaries follow the compact index's rule (<see cref="ToolContractCatalog.FindSentenceEnd"/>):
	/// a period followed by whitespace, abbreviations such as <c>e.g.</c> excepted.
	/// </remarks>
	internal static string BuildLead(string description, int maxChars) {
		if (string.IsNullOrEmpty(description) || description.Length <= maxChars) {
			return description ?? string.Empty;
		}
		int cut = -1;
		int searchFrom = 0;
		while (true) {
			int sentenceEnd = ToolContractCatalog.FindSentenceEnd(description, searchFrom);
			if (sentenceEnd < 0 || sentenceEnd + 1 > maxChars) {
				break;
			}
			cut = sentenceEnd + 1;
			searchFrom = cut;
		}
		if (cut > 0) {
			return description[..cut];
		}
		int wordEnd = description.LastIndexOf(' ', maxChars - 1);
		return (wordEnd > 0 ? description[..wordEnd] : description[..(maxChars - 1)]).TrimEnd() + "…";
	}

	/// <summary>
	/// Appends to <paramref name="lead"/> every confirmation clause of <paramref name="description"/> that the
	/// lead does not already contain: from the start of the sentence carrying <see cref="ConfirmationMarker"/>
	/// to the end of the marker's own clause (the next <c>.</c>, <c>;</c> or <c>)</c>), capped at
	/// <see cref="MaxConfirmationClauseChars"/>.
	/// </summary>
	internal static string AppendConfirmationClauses(string description, string lead) {
		List<string> clauses = [];
		int searchFrom = lead.Length;
		while (searchFrom < description.Length) {
			int marker = description.IndexOf(ConfirmationMarker, searchFrom, StringComparison.OrdinalIgnoreCase);
			if (marker < 0) {
				break;
			}
			int end = description.IndexOfAny(['.', ';', ')'], marker + ConfirmationMarker.Length);
			end = end < 0 ? description.Length : end + 1;
			int start = FindClauseStart(description, marker, end);
			clauses.Add(description[start..end].Trim().TrimEnd('.', ';', ')') + ".");
			searchFrom = end;
		}
		return clauses.Count == 0 ? lead : lead.TrimEnd() + " Also: " + string.Join(" ", clauses);
	}

	/// <summary>Serialized size of one contract, in UTF-8 bytes, measured the way the budget is.</summary>
	internal static int MeasureBytes<T>(T value) =>
		Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(value));

	private static int FindClauseStart(string description, int marker, int end) {
		int start = marker;
		while (start > 0 && !IsSentenceEndBefore(description, start)) {
			start--;
		}
		int earliest = Math.Max(0, end - MaxConfirmationClauseChars);
		if (start < earliest) {
			int wordStart = description.IndexOf(' ', earliest);
			start = wordStart < 0 || wordStart > marker ? earliest : wordStart + 1;
		}
		return start;
	}

	private static bool IsSentenceEndBefore(string description, int index) =>
		index >= 2 && char.IsWhiteSpace(description[index - 1])
		&& ToolContractCatalog.FindSentenceEnd(description, index - 2) == index - 2;

	private static bool IsShort(ToolContractDefinition contract) =>
		string.Equals(contract.Detail, ShortDetail, StringComparison.Ordinal);

	private static bool IsDetail(string? detail, string expected) =>
		string.Equals(detail?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

	private static IReadOnlyList<ToolContractField> TrimFields(IReadOnlyList<ToolContractField>? fields) =>
		fields is null
			? []
			: fields.Select(field => field with { Description = FirstSentence(field.Description) }).ToArray();

	private static string FirstSentence(string? text) {
		if (string.IsNullOrEmpty(text)) {
			return text ?? string.Empty;
		}
		int sentenceEnd = ToolContractCatalog.FindSentenceEnd(text, 0);
		string sentence = sentenceEnd >= 0 ? text[..(sentenceEnd + 1)] : text;
		return BuildLead(sentence, MaxFieldDescriptionChars);
	}

	private static bool SameDescriptions(IReadOnlyList<ToolContractField> trimmed,
		IReadOnlyList<ToolContractField>? original) =>
		original is null || trimmed.Select(field => field.Description)
			.SequenceEqual(original.Select(field => field.Description ?? string.Empty));

	private static string BuildNote(int omittedChars, int omittedExamples, bool fieldsTrimmed) {
		List<string> omitted = [];
		if (omittedChars > 0) {
			omitted.Add($"the last {omittedChars} description characters");
		}
		if (omittedExamples > 0) {
			omitted.Add(omittedExamples == 1 ? "the example" : $"{omittedExamples} examples");
		}
		if (fieldsTrimmed) {
			omitted.Add("field descriptions past their first sentence");
		}
		if (omitted.Count == 0) {
			return string.Empty;
		}
		return $"[Short form - left out: {string.Join("; ", omitted)}. detail=\"full\" returns the complete contract.]";
	}
}
