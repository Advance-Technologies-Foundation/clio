using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command.McpServer.Tools;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Loads the shared component-discovery labelled set (umbrella ADR Decision 6) from the test
/// fixtures. A single versioned <c>query → expected-component(s)</c> set consumed by Solution B
/// (ranked-search target, ENG-91572), Solution C (deterministic recall@k go/no-go, ENG-91573) and
/// Solution E (version-unknown communication case, ENG-91583) so the three tasks share one source
/// with no per-task duplication or drift. The <see cref="LabelledSet.Components"/> array is a
/// self-contained mini-catalog with populated selection metadata, so the deterministic ranker can be
/// measured before the producer backfills the live CDN payload (Solution A2) — it benchmarks the
/// ranking algorithm, not the production data.
/// </summary>
internal static class ComponentDiscoveryLabelledSet {
	/// <summary>Kind marker for rows that are component-ranking queries (vs the version-unknown case).</summary>
	public const string RankingKind = "ranking";

	/// <summary>Path of the fixture relative to the test assembly output directory.</summary>
	private const string FixtureRelativePath =
		"Command/McpServer/Fixtures/component-discovery-labelled-set.json";

	private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

	/// <summary>A single labelled query row from the shared set.</summary>
	public sealed record LabelledQuery(
		[property: JsonPropertyName("query")] string Query,
		[property: JsonPropertyName("kind")] string Kind,
		[property: JsonPropertyName("expected")] IReadOnlyList<string> Expected,
		[property: JsonPropertyName("note")] string? Note);

	/// <summary>The deserialised labelled set: a curated mini-catalog plus the labelled queries.</summary>
	public sealed record LabelledSet(
		[property: JsonPropertyName("components")] IReadOnlyList<ComponentRegistryEntry> Components,
		[property: JsonPropertyName("queries")] IReadOnlyList<LabelledQuery> Queries) {
		/// <summary>The component-ranking queries (<see cref="RankingKind"/>) — the subset Solution B measures.</summary>
		public IReadOnlyList<LabelledQuery> RankingQueries =>
			Queries.Where(query => string.Equals(query.Kind, RankingKind, StringComparison.OrdinalIgnoreCase)).ToArray();
	}

	/// <summary>Reads and deserialises the labelled set copied next to the test assembly.</summary>
	public static LabelledSet Load() {
		string path = Path.Combine(TestContext.CurrentContext.TestDirectory, FixtureRelativePath);
		string json = File.ReadAllText(path);
		return JsonSerializer.Deserialize<LabelledSet>(json, Options)
			?? throw new InvalidOperationException($"Failed to deserialise the labelled set from '{path}'.");
	}
}
