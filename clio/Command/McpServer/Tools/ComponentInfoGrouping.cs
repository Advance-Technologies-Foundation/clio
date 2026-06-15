using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Shared search and projection helpers for the curated Freedom UI component catalog.
/// Used by both the MCP <c>get-component-info</c> tool and the <c>clio get-component-info</c>
/// CLI verb so both surfaces produce identical list ordering and identical search semantics.
/// </summary>
/// <remarks>
/// <para>
/// Solution B (ENG-91572) replaced the original binary, case-insensitive substring filter with a
/// <b>scored, deterministic ranking</b>. A search query is tokenised and each entry is scored by the
/// weighted sum of the tiers a token hits, with the highest weight on Solution A's curated selection
/// metadata (<c>synonyms</c>/<c>useCases</c>), then human-facing capability text
/// (<c>description</c>/<c>whenToUse</c>), then identity fields (type/parents/children/category), then
/// the structural binding surface (<c>inputs</c>/<c>outputs</c>/<c>properties</c>). Results are ordered
/// by score descending, then by <c>ComponentType</c> <see cref="StringComparer.OrdinalIgnoreCase"/>
/// ascending so the ordering is stable across macOS, Linux and Windows runners.
/// </para>
/// <para>
/// Parity between the MCP tool and the CLI verb is scoped to this <b>deterministic tool output</b> — the
/// ordered list clio returns. Any LLM reranking happens in the MCP client and is explicitly out of
/// clio's scope (see <c>spec/adr/adr-component-discovery-selection.md</c>, Decision 2). The not-found
/// suggestion path is unified across both surfaces through <see cref="SuggestForUnknown"/>.
/// </para>
/// </remarks>
public static class ComponentInfoGrouping {
	/// <summary>
	/// Upper bound on the "did you mean" entries returned for an unknown component type, shared by the
	/// MCP tool and the CLI verb so both surfaces emit the same bounded shortlist instead of echoing the
	/// full ~199-item catalog as "suggestions".
	/// </summary>
	internal const int MaxNotFoundSuggestions = 8;

	// Tier weights for the scored ranking, in strict descending priority. The gaps are wide enough that
	// a single higher-tier token hit outranks several lower-tier hits for any realistic short query —
	// e.g. a synonym hit (100) beats a description hit (40) which beats a type hit (15) which beats a
	// binding hit (5). Per tier, the contribution is the weight times the number of DISTINCT query terms
	// that hit any field in the tier, so multi-term coverage ranks above a single-term hit at the same tier.
	private const int SelectionMetadataWeight = 100; // synonyms, useCases (Solution A curated match surface)
	private const int CapabilityTextWeight = 40;     // description, whenToUse (human-facing capability text)
	private const int IdentityWeight = 15;           // componentType, category, whenNotToUse, parentTypes, typicalChildren
	private const int BindingWeight = 5;             // inputs, outputs, legacy properties (structural surface)

	/// <summary>
	/// Tokens that carry no discriminative signal for component search and are dropped before scoring:
	/// common English stop words plus the universal <c>crt</c> Freedom UI type prefix (present on every
	/// component, so matching it would score the whole catalog equally and add only noise).
	/// </summary>
	private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal) {
		"the", "a", "an", "of", "to", "for", "and", "or", "in", "on", "is", "with",
		"by", "as", "at", "be", "from", "into", "that", "this", "it", "crt"
	};

	/// <summary>Characters that separate query tokens (whitespace plus common punctuation).</summary>
	private static readonly char[] TokenSeparators =
		" \t\r\n.,;:!?/\\|()[]{}<>\"'`~@#$%^&*-+=".ToCharArray();

	/// <summary>
	/// Ranks the catalog for a list-mode search. When <paramref name="search"/> is empty the whole
	/// catalog is returned in alphabetical <c>ComponentType</c> order (faceted-discovery list — no
	/// ranking signal to apply). Otherwise the query is tokenised and only entries with a positive score
	/// are returned, ordered by score descending then <c>ComponentType</c>
	/// <see cref="StringComparer.OrdinalIgnoreCase"/> ascending. This single ordering is the deterministic
	/// tool output both the MCP tool and the CLI verb surface (CLI/MCP parity).
	/// </summary>
	public static IReadOnlyList<ComponentRegistryEntry> RankEntries(
		IReadOnlyList<ComponentRegistryEntry> entries, string? search) {
		IReadOnlyList<string> terms = Tokenize(search);
		if (terms.Count == 0) {
			return entries
				.OrderBy(entry => entry.ComponentType, StringComparer.OrdinalIgnoreCase)
				.ToArray();
		}
		return entries
			.Select(entry => (Entry: entry, Score: Score(entry, terms)))
			.Where(scored => scored.Score > 0)
			.OrderByDescending(scored => scored.Score)
			.ThenBy(scored => scored.Entry.ComponentType, StringComparer.OrdinalIgnoreCase)
			.Select(scored => scored.Entry)
			.ToArray();
	}

	/// <summary>
	/// Returns the set of entries that match the search query (positive score), preserving input order.
	/// Membership is identical to <see cref="RankEntries"/>; this overload exists for callers that only
	/// need the matched set and not the ranked ordering — the <see cref="SuggestForUnknown"/> candidate
	/// pool and the mobile catalog <c>SearchAsync</c> shim. With an empty query every entry matches.
	/// </summary>
	public static IReadOnlyList<ComponentRegistryEntry> FilterEntries(
		IReadOnlyList<ComponentRegistryEntry> entries, string? search) {
		IReadOnlyList<string> terms = Tokenize(search);
		if (terms.Count == 0) {
			return entries;
		}
		return entries.Where(entry => Score(entry, terms) > 0).ToArray();
	}

	/// <summary>
	/// Returns a bounded "did you mean" shortlist for an unknown component type, ordered by
	/// closeness to the requested type (case-insensitive Levenshtein distance, ties broken
	/// alphabetically). When <paramref name="search"/> is supplied the candidate pool is first
	/// narrowed by the same keyword match as list mode; otherwise every known entry is a
	/// candidate. Either way the result is capped at <paramref name="max"/> so a not-found
	/// response never echoes the full catalog as "suggestions". Used by both the MCP tool and the
	/// CLI verb so the not-found suggestion path is identical on both surfaces.
	/// </summary>
	public static IReadOnlyList<ComponentRegistryEntry> SuggestForUnknown(
		IReadOnlyList<ComponentRegistryEntry> entries, string? componentType, string? search, int max) {
		IReadOnlyList<ComponentRegistryEntry> pool = string.IsNullOrWhiteSpace(search)
			? entries
			: FilterEntries(entries, search);
		string target = (componentType ?? string.Empty).Trim();
		return pool
			.OrderBy(entry => McpToolArgumentSupport.LevenshteinDistance(entry.ComponentType, target))
			.ThenBy(entry => entry.ComponentType, StringComparer.OrdinalIgnoreCase)
			.Take(Math.Max(0, max))
			.ToArray();
	}

	/// <summary>
	/// Projects entries to compact list items, <b>preserving the caller's order</b>. Callers own the
	/// ordering: list mode passes <see cref="RankEntries"/> output (ranked by score, or alphabetical for
	/// an empty query) and the not-found path passes <see cref="SuggestForUnknown"/> output (ordered by
	/// closeness). Description is null-coalesced so the response omits empty strings from the wrapped
	/// payload shape (which does not carry per-component descriptions until the producer backfills them).
	/// </summary>
	public static IReadOnlyList<ComponentInfoListItem> CreateItems(IReadOnlyList<ComponentRegistryEntry> entries) {
		return entries
			.Select(entry => new ComponentInfoListItem {
				ComponentType = entry.ComponentType,
				Description = string.IsNullOrWhiteSpace(entry.Description) ? null : entry.Description
			})
			.ToArray();
	}

	/// <summary>
	/// Computes the weighted relevance score of an entry against the pre-tokenised query. Each tier
	/// contributes <c>tierWeight × (number of distinct terms that hit any field in the tier)</c>. The
	/// score is used only for ordering and the positive-score membership test; it is not surfaced on the
	/// wire (the deterministic contract is the resulting order). Exposed to unit tests via
	/// <see cref="ScoreEntry"/>.
	/// </summary>
	private static int Score(ComponentRegistryEntry entry, IReadOnlyList<string> terms) {
		int score = 0;
		score += SelectionMetadataWeight * CountMatchingTerms(terms, entry.Synonyms.Concat(entry.UseCases));
		score += CapabilityTextWeight * CountMatchingTerms(terms, new[] { entry.Description, entry.WhenToUse });
		score += IdentityWeight * CountMatchingTerms(terms, IdentityTexts(entry));
		score += BindingWeight * CountMatchingBindingTerms(terms, entry);
		return score;
	}

	/// <summary>
	/// Test-facing entry point for the deterministic scorer. Tokenises <paramref name="search"/> and
	/// returns the entry's weighted score, so unit tests can assert the tier weighting
	/// (synonyms/useCases &gt; description &gt; type/parents/children &gt; inputs/outputs) directly
	/// without round-tripping through the response shape. Returns 0 for an empty query.
	/// </summary>
	internal static int ScoreEntry(ComponentRegistryEntry entry, string? search) {
		IReadOnlyList<string> terms = Tokenize(search);
		return terms.Count == 0 ? 0 : Score(entry, terms);
	}

	/// <summary>Flattens the identity tier's text fields (type, category, whenNotToUse, parents, children).</summary>
	private static IEnumerable<string?> IdentityTexts(ComponentRegistryEntry entry) {
		yield return entry.ComponentType;
		yield return entry.Category;
		yield return entry.WhenNotToUse;
		foreach (string parentType in entry.ParentTypes) {
			yield return parentType;
		}
		foreach (string childType in entry.TypicalChildren) {
			yield return childType;
		}
	}

	/// <summary>
	/// Counts how many distinct query terms appear (case-insensitive substring) in at least one of the
	/// supplied texts. A term hitting several texts in the same tier counts once, so a tier's
	/// contribution reflects query coverage rather than how many fields happen to repeat the term.
	/// </summary>
	private static int CountMatchingTerms(IReadOnlyList<string> terms, IEnumerable<string?> texts) {
		List<string?> materialised = texts.ToList();
		int count = 0;
		foreach (string term in terms) {
			if (materialised.Any(text => ContainsCi(text, term))) {
				count++;
			}
		}
		return count;
	}

	/// <summary>
	/// Counts how many distinct query terms hit the entry's structural binding surface — the legacy
	/// <c>properties</c> dictionary and the wrapped-shape <c>inputs</c>/<c>outputs</c> dictionaries.
	/// </summary>
	private static int CountMatchingBindingTerms(IReadOnlyList<string> terms, ComponentRegistryEntry entry) {
		int count = 0;
		foreach (string term in terms) {
			if (PropertiesMatch(entry.Properties, term)
				|| BindingsMatch(entry.Inputs, term)
				|| BindingsMatch(entry.Outputs, term)) {
				count++;
			}
		}
		return count;
	}

	/// <summary>
	/// Returns <c>true</c> when a single term matches the legacy curated <c>properties</c> dictionary —
	/// the property key, its declared type/description, or one of its enum values.
	/// </summary>
	private static bool PropertiesMatch(
		IReadOnlyDictionary<string, ComponentPropertyDefinition>? properties, string term) {
		return properties is { Count: > 0 }
			&& properties.Any(property =>
				ContainsCi(property.Key, term)
				|| ContainsCi(property.Value.Type, term)
				|| ContainsCi(property.Value.Description, term)
				|| property.Value.Values?.Any(value => ContainsCi(value, term)) == true);
	}

	/// <summary>
	/// Searches the wrapped-shape <c>inputs</c> / <c>outputs</c> dictionaries for a single-term
	/// match. The values are <see cref="JsonElement"/> blobs whose schema is owned by the producer
	/// (see <c>static-files-mcp</c>), so the matcher only looks at well-known string fields
	/// (<c>type</c>, <c>description</c>, <c>values</c>) — that keeps the search predictable while
	/// still letting the producer add unknown keys without breaking matching.
	/// </summary>
	private static bool BindingsMatch(IReadOnlyDictionary<string, JsonElement>? bindings, string term) {
		return bindings is { Count: > 0 }
			&& bindings.Any(binding => BindingMatches(binding.Key, binding.Value, term));
	}

	/// <summary>
	/// Returns <c>true</c> when a single binding entry matches the term — either directly (key name)
	/// or through one of its well-known string fields (<c>type</c>, <c>description</c>) or its enum
	/// <c>values</c> array.
	/// </summary>
	private static bool BindingMatches(string key, JsonElement value, string term) {
		if (ContainsCi(key, term)) {
			return true;
		}
		if (value.ValueKind != JsonValueKind.Object) {
			return false;
		}
		if (TryGetStringProperty(value, "type", out string? type) && ContainsCi(type, term)) {
			return true;
		}
		if (TryGetStringProperty(value, "description", out string? description) && ContainsCi(description, term)) {
			return true;
		}
		return EnumValuesMatch(value, term);
	}

	/// <summary>
	/// Checks the binding's optional <c>values</c> enum array for a term match.
	/// Non-string entries are skipped (the producer-side schema is occasionally
	/// numeric — e.g. dataValueType enums on DataGrid columns — and those are not
	/// searchable as text). Returns <c>false</c> when the binding has no
	/// <c>values</c> array at all.
	/// </summary>
	private static bool EnumValuesMatch(JsonElement value, string term) {
		if (!value.TryGetProperty("values", out JsonElement values) || values.ValueKind != JsonValueKind.Array) {
			return false;
		}
		foreach (JsonElement enumValue in values.EnumerateArray()) {
			if (enumValue.ValueKind == JsonValueKind.String && ContainsCi(enumValue.GetString(), term)) {
				return true;
			}
		}
		return false;
	}

	private static bool TryGetStringProperty(JsonElement element, string propertyName, out string? value) {
		if (element.TryGetProperty(propertyName, out JsonElement property)
			&& property.ValueKind == JsonValueKind.String) {
			value = property.GetString();
			return true;
		}
		value = null;
		return false;
	}

	/// <summary>
	/// Splits a raw search string into lower-cased, deduplicated tokens: separated on whitespace and
	/// common punctuation, with stop words and single-character tokens removed. Tokenising (rather than
	/// matching the whole query as one substring) is what lets a natural-language need such as
	/// "photo gallery for property cards" surface <c>crt.Gallery</c> by hitting its <c>photo grid</c>
	/// synonym. Returns an empty list when the query is blank or all-stop-words.
	/// </summary>
	private static IReadOnlyList<string> Tokenize(string? search) {
		if (string.IsNullOrWhiteSpace(search)) {
			return Array.Empty<string>();
		}
		List<string> tokens = [];
		HashSet<string> seen = new(StringComparer.Ordinal);
		foreach (string raw in search.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries)) {
			string token = raw.ToLowerInvariant();
			if (token.Length >= 2 && !StopWords.Contains(token) && seen.Add(token)) {
				tokens.Add(token);
			}
		}
		return tokens;
	}

	private static bool ContainsCi(string? value, string term) {
		return !string.IsNullOrWhiteSpace(value)
			&& value.Contains(term, StringComparison.OrdinalIgnoreCase);
	}
}
