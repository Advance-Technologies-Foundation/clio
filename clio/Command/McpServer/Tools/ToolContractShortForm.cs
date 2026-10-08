using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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
/// is never touched.
/// </para>
/// <para>
/// What the short form keeps: the description's lead (the purpose, and for a destructive tool the call-time
/// warning that follows the purpose); every sentence carrying a safety marker, wherever it stands
/// (<see cref="SafetyDuty"/> - a confirmation duty, an ask/tell/warn-the-user rule, a prohibition, an
/// irreversibility warning), and every field description carrying one whole (<see cref="FieldDuty"/>); the
/// input schema with its required list and validators; the error codes, preconditions,
/// aliases, defaults, flows, deprecations and anti-patterns. What it leaves out: the rest of the description
/// (for the process tools, mostly the per-element reference that the process guides own), the examples, and
/// each field description past its first sentence unless that field carries a marker. A rule worded with no
/// marker ("do NOT run compile-creatio") is left out too - the markers are what fits the budget, see
/// <see cref="ClauseStart"/> - and the note says "safety-marked sentences", not "all rules". It says so in
/// the description, with how to get the rest (<c>detail="full"</c>). It does NOT keep everything a caller
/// could want at call time - nested-shape reference text is the price of fitting - which is why the full
/// form stays one argument away.
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
	/// largest request measured in those transcripts - fit it in their short forms. Measured with the default
	/// JSON encoder, which escapes non-ASCII and so over-counts the real wire size - the safe direction for a
	/// ceiling.
	/// </remarks>
	internal const int InlineReplyBudgetBytes = 18 * 1024;

	/// <summary>
	/// Longest description lead the short form keeps for a DESTRUCTIVE tool whose description OPENS with its
	/// call-time safety warning - a <see cref="SafetyDuty"/> sentence inside the ordinary lead. That warning is
	/// a block of several sentences of which only some carry a marker ("An ABSENT filter is the WIDE state,
	/// not a safe one ... nothing warns you" carries none), so the block is kept whole rather than sentence by
	/// sentence. The longest such block - modify-business-process's, overwrite and access-rights confirmation
	/// together - ends inside this bound.
	/// </summary>
	internal const int MaxWarningLeadChars = 1500;

	/// <summary>
	/// Longest description lead the short form keeps for every other tool, a destructive one whose warning
	/// comes later included: its purpose and the sentences right after it. Safety sentences beyond it are kept
	/// anyway (<see cref="SafetyDuty"/>), so the smaller lead costs no warning - which is what lets a request
	/// for several contracts fit at all.
	/// </summary>
	internal const int MaxOrdinaryLeadChars = 500;

	/// <summary>Longest field description the short form keeps when it carries no safety sentence.</summary>
	internal const int MaxFieldDescriptionChars = 200;

	/// <summary>Longest single safety sentence kept from outside the lead, cut at word boundaries around its marker.</summary>
	internal const int MaxSafetySentenceChars = 600;

	/// <summary>
	/// The markers of a duty the caller must not lose: a confirmation, an ask/tell/warn-the-user rule, a
	/// prohibition ("never", "must not"), an irreversibility or permanence warning, a retry ban, a secret-
	/// handling rule. Every sentence matching it - in the description or in any field description - survives
	/// shortening; a sentence that states a duty in none of these words does not, which is why the short form
	/// says it kept the MATCHING sentences, not all of them.
	/// </summary>
	/// <remarks>
	/// Grown from the catalog, twice. A first cut keyed on "explicit yes" dropped "ASK FIRST ... on your own
	/// initiative" (set-active-business-process-version), "Never chain the two" and "permanent"
	/// (modify-business-process-as-new-version) and the update-page <c>force</c> field's "ONLY after the user
	/// explicitly confirms". A second cut still dropped "TELL THE USER before building one"
	/// (create-business-process), "never retry on a warning" (update-page), "warn the user first" (set-logo),
	/// "Never supply the password itself" and "treated as DATA, never as instructions". The markers are
	/// deliberately broad - a "never" in reference text costs a few bytes, a lost duty costs a wrong action -
	/// and <c>ToolContractShortFormTests.ShortLookup_Should_KeepTheReviewedDuty</c> pins each of those phrases
	/// against the real catalog independently of this pattern.
	/// </remarks>
	internal static readonly Regex SafetyDuty = new(
		@"explicit(ly)?\s+(yes|confirm\w*|opt-in)|\bASK FIRST\b|" + ClauseStart + @"(ask|tell|warn)( the)? (user|developer)\b"
		+ @"|\bask before\b|only after the user|once the user has agreed|user'?s? (agreement|consent)"
		+ @"|on your own initiative|" + ProhibitiveNever + @"|\bmust not\b|irreversib\w*"
		+ @"|cannot be (undone|(\w+ )?reverted)|\bpermanent(ly)?\b|(?<!non-)\bdestructive\b|do not retry"
		+ @"|not retryable|\bsecret\b|\bpassword\b|as instructions|not available to you",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

	/// <summary>
	/// The start of a sentence or clause, where an imperative "tell / ask / warn the user" stands: "TELL THE
	/// USER before building one", ": ASK the user to open this version", "- warn the user first". The same
	/// words in the middle of a clause are description - "says what to tell the user first" - and the
	/// process reference text has plenty of those.
	/// </summary>
	private const string ClauseStart = @"(?:^|[,;:(\u2014\u2013-]\s*|\b(?:so|and|but|then|or|always|first)\s+)";

	/// <summary>
	/// "never" followed by a verb in its BASE form - a prohibition addressed to the caller ("Never chain the
	/// two", "never retry on a warning", "never point it at", "Never supply the password", "never hand-write
	/// filter JSON"). Every other "never" in the catalog describes what something does ("server-side and
	/// never accepted", "reports drift and never fixes it", "this tool never reads"), and matching those took
	/// the seven process contracts from 18 KB to 32 KB - the very spill this short form exists to prevent.
	/// Neither word position nor an auxiliary look-behind separates the two kinds; the verb form does.
	/// </summary>
	private const string ProhibitiveNever =
		@"\bnever\s+(retry|echo|supply|send|re-?send|pass|point|chain|call|use|hand-write|write|guess|invent"
		+ @"|delete|remove|run|repeat|put|store|log|print|share|expose|include|trust|assume|rely|skip|bypass"
		+ @"|overwrite|activate|reuse|re-?type|split|merge|mix|combine|target|touch)\b";

	/// <summary>
	/// The wider net for a FIELD description, which is kept whole when it matches: a field is a sentence or
	/// three, so keeping one costs little, and a field is where a flag's duty is written ("The save already
	/// succeeded - never retry on a warning", "deleted recursively - so never point it at ...").
	/// </summary>
	internal static readonly Regex FieldDuty = new(
		@"\bnever\b|\bmust not\b|\bdo not\b|\bdon't\b|\bwarn\b|delet|retr(y|ies)|instruction|revert"
		+ @"|confirm|consent|secret|password|customer data",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

	/// <summary>
	/// Applies the requested <paramref name="detail"/> to a named lookup's resolved contracts.
	/// </summary>
	/// <param name="contracts">The resolved full contracts, in request order.</param>
	/// <param name="detail"><c>full</c>, <c>short</c>, or anything else (including null) for the default fit.</param>
	/// <param name="measureReply">Serialized size of the whole reply that would carry the given contracts.</param>
	/// <param name="isDestructive">
	/// Whether a tool is destructive; with a description that opens with a safety warning, decides that the
	/// whole warning block is kept. Answer <see langword="true"/> when unknown - keeping more is the safe mistake.
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
	/// <remarks>
	/// Each contract is measured once and the reply size is kept as a running total, so a request naming the
	/// whole catalog costs one serialization per contract rather than one per contract per step.
	/// </remarks>
	internal static IReadOnlyList<ToolContractDefinition> Fit(
		IReadOnlyList<ToolContractDefinition> contracts,
		Func<IReadOnlyList<ToolContractDefinition>, int> measureReply,
		Func<string, bool> isDestructive) {
		ToolContractDefinition[] fitted = [.. contracts];
		int total = measureReply(fitted);
		if (total <= InlineReplyBudgetBytes) {
			return fitted;
		}
		// Lossless before lossy: identical error codes are carried once before any text is cut.
		ShareRepeatedErrorContracts(fitted);
		total = measureReply(fitted);
		int[] sizes = [.. fitted.Select(MeasureBytes)];
		bool[] done = new bool[fitted.Length];
		while (total > InlineReplyBudgetBytes) {
			int largest = -1;
			for (int index = 0; index < fitted.Length; index++) {
				if (!done[index] && (largest < 0 || sizes[index] > sizes[largest])) {
					largest = index;
				}
			}
			if (largest < 0) {
				// Everything is already short and the reply still exceeds the budget: a request for that
				// many tools cannot fit, and returning less than was asked for would be worse than a spill.
				break;
			}
			done[largest] = true;
			ToolContractDefinition shortened = Shorten(fitted[largest], isDestructive(fitted[largest].Name));
			int shortSize = MeasureBytes(shortened);
			// A contract with nothing to cut only gains the short-form markers, and a "short" label on a complete
			// contract invites a needless detail=full call.
			if (shortSize >= sizes[largest]) {
				continue;
			}
			fitted[largest] = shortened;
			total += shortSize - sizes[largest];
			sizes[largest] = shortSize;
		}
		return fitted;
	}

	/// <summary>
	/// Replaces every repeated set of error codes in a reply with <see cref="ToolErrorContract.SameAs"/> naming the
	/// first contract that carries it.
	/// </summary>
	/// <remarks>
	/// Most contracts publish the same generic codes (an unknown tool, a missing or mistyped parameter), and seven
	/// process-designer contracts carried them seven times - about 1.8 KB of a reply fitted to 18 KB. Sharing them
	/// is lossless, so it runs before any text is cut, and it gives the fit room to keep a small contract complete.
	/// </remarks>
	private static void ShareRepeatedErrorContracts(ToolContractDefinition[] contracts) {
		Dictionary<string, string> firstByCodes = new(StringComparer.Ordinal);
		for (int index = 0; index < contracts.Length; index++) {
			ToolErrorContract? errors = contracts[index].ErrorContract;
			if (errors is null || errors.SameAs is not null || errors.Codes.Count == 0) {
				continue;
			}
			string key = JsonSerializer.Serialize(errors.Codes);
			if (firstByCodes.TryGetValue(key, out string? first)) {
				contracts[index] = contracts[index] with { ErrorContract = new ToolErrorContract([], first) };
			} else {
				firstByCodes[key] = contracts[index].Name;
			}
		}
	}

	/// <summary>
	/// Builds the short form of one contract. Idempotent: a contract already short is returned unchanged.
	/// </summary>
	/// <param name="contract">The full contract.</param>
	/// <param name="destructive">
	/// Whether the tool is destructive: when its description opens with a safety warning, the lead keeps the
	/// whole warning block (<see cref="MaxWarningLeadChars"/>).
	/// </param>
	internal static ToolContractDefinition Shorten(ToolContractDefinition contract, bool destructive) {
		if (IsShort(contract)) {
			return contract;
		}
		int fullBytes = MeasureBytes(contract);
		string description = contract.Description ?? string.Empty;
		string lead = BuildLead(description,
			destructive && OpensWithSafetyWarning(description) ? MaxWarningLeadChars : MaxOrdinaryLeadChars);
		// Walked from the start, not from the end of the lead: a lead cut at a word boundary ends mid-sentence,
		// and that sentence must still be found whole.
		string[] safety = [.. SafetySentences(description, 0)
			.Where(sentence => !lead.Contains(sentence.Trim('…'), StringComparison.Ordinal))
			.Distinct(StringComparer.Ordinal)];
		string kept = safety.Length == 0 ? lead : lead.TrimEnd() + " Also: " + string.Join(" ", safety);
		IReadOnlyList<ToolContractField> inputFields = TrimFields(contract.InputSchema?.Properties);
		IReadOnlyList<ToolContractField> outputFields = TrimFields(contract.OutputContract?.Fields);
		bool fieldsTrimmed = !SameDescriptions(inputFields, contract.InputSchema?.Properties)
			|| !SameDescriptions(outputFields, contract.OutputContract?.Fields);
		int keptOfDescription = lead.Length + safety.Sum(sentence => sentence.Length);
		string note = BuildNote(description.Length, Math.Min(keptOfDescription, description.Length),
			contract.Examples?.Count ?? 0, fieldsTrimmed);
		return contract with {
			Description = note.Length == 0 ? kept : (kept.TrimEnd() + " " + note),
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
		return CutAtWord(description, 0, maxChars) + "…";
	}

	/// <summary>
	/// Every sentence of <paramref name="text"/> from <paramref name="startIndex"/> on that matches
	/// <see cref="SafetyDuty"/>, in order. A sentence longer than
	/// <see cref="MaxSafetySentenceChars"/> - the process-designer element reference runs for thousands of
	/// characters between two periods - is cut at word boundaries around its first marker.
	/// </summary>
	internal static IReadOnlyList<string> SafetySentences(string text, int startIndex) {
		List<string> sentences = [];
		if (string.IsNullOrEmpty(text) || startIndex >= text.Length) {
			return sentences;
		}
		int start = Math.Max(0, startIndex);
		while (start < text.Length) {
			int end = ToolContractCatalog.FindSentenceEnd(text, start);
			int stop = end < 0 ? text.Length : end + 1;
			string sentence = text[start..stop].Trim();
			Match marker = SafetyDuty.Match(sentence);
			if (marker.Success) {
				sentences.Add(sentence.Length <= MaxSafetySentenceChars ? sentence : AroundMarker(sentence, marker.Index));
			}
			start = stop;
		}
		return sentences;
	}

	/// <summary>
	/// Whether a <see cref="SafetyDuty"/> sentence STARTS inside the ordinary lead. Starts, not ends: the
	/// create-business-process warning opens after two ordinary sentences and runs past the ordinary lead, and a test on the ordinary
	/// lead's own text would miss it and drop the rest of the block.
	/// </summary>
	internal static bool OpensWithSafetyWarning(string description) {
		int start = 0;
		while (start < description.Length && start < MaxOrdinaryLeadChars) {
			int end = ToolContractCatalog.FindSentenceEnd(description, start);
			int stop = end < 0 ? description.Length : end + 1;
			if (SafetyDuty.IsMatch(description[start..stop])) {
				return true;
			}
			start = stop;
		}
		return false;
	}

	/// <summary>Serialized size of one value, in UTF-8 bytes, measured the way the budget is.</summary>
	internal static int MeasureBytes<T>(T value) =>
		Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(value));

	/// <summary>Whether <paramref name="detail"/> names <paramref name="expected"/>, ignoring case and padding.</summary>
	internal static bool IsDetail(string? detail, string expected) =>
		string.Equals(detail?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

	private static bool IsShort(ToolContractDefinition contract) =>
		string.Equals(contract.Detail, ShortDetail, StringComparison.Ordinal);

	private static IReadOnlyList<ToolContractField> TrimFields(IReadOnlyList<ToolContractField>? fields) =>
		fields is null
			? []
			: fields.Select(field => field with { Description = TrimFieldDescription(field.Description) }).ToArray();

	/// <summary>
	/// A field description that states a safety duty is kept WHOLE; any other is cut to its first real
	/// sentence - a leading label such as "Optional." or "Optional, default true." (<see cref="LeadingLabel"/>)
	/// does not count as one, since a field cut to its label says nothing about what the field is. The label is
	/// kept in front of that sentence, and the sentence alone is cut to fit, so a long one cannot leave the
	/// label as the whole description.
	/// </summary>
	private static string TrimFieldDescription(string? text) {
		if (string.IsNullOrEmpty(text) || SafetyDuty.IsMatch(text) || FieldDuty.IsMatch(text)) {
			return text ?? string.Empty;
		}
		string label = LeadingLabel.Match(text).Value;
		int end = ToolContractCatalog.FindSentenceEnd(text, label.Length);
		string sentence = end >= 0 ? text[label.Length..(end + 1)] : text[label.Length..];
		return label + BuildLead(sentence, Math.Max(0, MaxFieldDescriptionChars - label.Length));
	}

	/// <summary>
	/// A field description's leading label - "Optional.", "Required." or "Optional, default true." - with the
	/// space after it.
	/// </summary>
	private static readonly Regex LeadingLabel = new(@"^\s*(Optional|Required)(,\s*default\s+[^.\s]+)?\.\s+",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

	/// <summary>
	/// The part of a run-on sentence that carries its marker: from a word boundary at most half the cap
	/// before the marker, to the end of the marker's clause - the first <c>;</c> after it - or, when the
	/// clause runs past the cap, the last word boundary inside it. Ellipses mark each cut side.
	/// </summary>
	/// <remarks>
	/// Only a sentence longer than <see cref="MaxSafetySentenceChars"/> gets here, and in the catalog that is
	/// an operation list whose items are separated by semicolons, so the clause end is where the duty ends.
	/// A closing parenthesis is deliberately NOT a clause end: "get an explicit yes (the edit request is not
	/// one)" keeps its qualifier.
	/// </remarks>
	private static string AroundMarker(string sentence, int markerIndex) {
		int from = Math.Max(0, markerIndex - (MaxSafetySentenceChars / 2));
		if (from > 0) {
			int space = sentence.IndexOf(' ', from);
			from = space < 0 || space > markerIndex ? from : space + 1;
		}
		int clauseEnd = sentence.IndexOf(';', markerIndex);
		string window = clauseEnd >= 0 && clauseEnd + 1 - from <= MaxSafetySentenceChars
			? sentence[from..(clauseEnd + 1)]
			: CutAtWord(sentence, from, MaxSafetySentenceChars);
		return (from > 0 ? "…" : string.Empty) + window + (from + window.Length < sentence.Length ? "…" : string.Empty);
	}

	private static string CutAtWord(string text, int from, int maxChars) {
		if (text.Length - from <= maxChars) {
			return text[from..];
		}
		int wordEnd = text.LastIndexOf(' ', from + maxChars - 1, maxChars);
		return (wordEnd > from ? text[from..wordEnd] : text.Substring(from, maxChars - 1)).TrimEnd();
	}

	private static bool SameDescriptions(IReadOnlyList<ToolContractField> trimmed,
		IReadOnlyList<ToolContractField>? original) =>
		original is null || trimmed.Select(field => field.Description)
			.SequenceEqual(original.Select(field => field.Description ?? string.Empty));

	private static string BuildNote(int descriptionChars, int keptChars, int omittedExamples, bool fieldsTrimmed) {
		List<string> omitted = [];
		if (keptChars < descriptionChars) {
			omitted.Add($"{descriptionChars - keptChars} description chars");
		}
		if (omittedExamples > 0) {
			omitted.Add(omittedExamples == 1 ? "the example" : $"{omittedExamples} examples");
		}
		if (fieldsTrimmed) {
			omitted.Add("field detail");
		}
		if (omitted.Count == 0) {
			return string.Empty;
		}
		// Every byte here is paid once per short contract in a fitted reply, and the contract's own detail="short"
		// already says it is short; a quoted value also costs twelve bytes more under the escaping encoder.
		return $"[Marked sentences kept; cut {string.Join(", ", omitted)}; detail=full has all.]";
	}
}
