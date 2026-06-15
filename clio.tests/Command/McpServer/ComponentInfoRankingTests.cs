using System.Collections.Generic;
using System.Linq;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Deterministic ranking assertions for Solution B (ENG-91572) over
/// <see cref="ComponentInfoGrouping"/>. These live in <c>[Category("Unit")]</c> because the
/// <c>clio.mcp.e2e</c> project is not in CI (umbrella ADR Decision 2 / Decision 3), so the ranking
/// contract — tier weighting, deterministic cross-OS tie-break, and recall@k against the shared
/// labelled set — is pinned here.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ComponentInfoRankingTests {
	private const string Term = "zarp"; // a distinctive token that only appears where a test puts it

	[Test]
	[Description("A query term hit in synonyms/useCases outranks the same term hit only in the description, so the curated Solution A selection metadata is the strongest ranking signal (ADR Decision 2 tier order).")]
	public void RankEntries_ShouldRankSynonymHitAboveDescriptionHit() {
		// Arrange — two entries that differ only in WHICH tier carries the query term.
		ComponentRegistryEntry bySynonym = new() { ComponentType = "crt.Synonym", Synonyms = new[] { Term } };
		ComponentRegistryEntry byDescription = new() { ComponentType = "crt.Description", Description = $"{Term} widget" };
		ComponentRegistryEntry[] entries = { byDescription, bySynonym }; // input order is the opposite of expected

		// Act
		IReadOnlyList<ComponentRegistryEntry> ranked = ComponentInfoGrouping.RankEntries(entries, Term);

		// Assert
		ranked.Select(entry => entry.ComponentType).Should().Equal(new[] { "crt.Synonym", "crt.Description" },
			because: "a synonym hit must rank above a description hit regardless of input order");
		ComponentInfoGrouping.ScoreEntry(bySynonym, Term).Should().BeGreaterThan(
			ComponentInfoGrouping.ScoreEntry(byDescription, Term),
			because: "synonyms/useCases carry the highest tier weight, above description");
	}

	[Test]
	[Description("A query term hit in the description outranks the same term hit only in an identity field (type/category/parents/children), placing human-facing capability text above structural identity (ADR Decision 2 tier order).")]
	public void RankEntries_ShouldRankDescriptionHitAboveIdentityHit() {
		// Arrange
		ComponentRegistryEntry byDescription = new() { ComponentType = "crt.Description", Description = $"{Term} tool" };
		ComponentRegistryEntry byCategory = new() { ComponentType = "crt.Category", Category = Term };
		ComponentRegistryEntry[] entries = { byCategory, byDescription };

		// Act
		IReadOnlyList<ComponentRegistryEntry> ranked = ComponentInfoGrouping.RankEntries(entries, Term);

		// Assert
		ranked.Select(entry => entry.ComponentType).Should().Equal(new[] { "crt.Description", "crt.Category" },
			because: "a description hit must rank above an identity-field (category) hit");
		ComponentInfoGrouping.ScoreEntry(byDescription, Term).Should().BeGreaterThan(
			ComponentInfoGrouping.ScoreEntry(byCategory, Term),
			because: "description carries a higher tier weight than identity fields");
	}

	[Test]
	[Description("A query term hit in an identity field outranks the same term hit only in the structural binding surface (inputs/outputs/properties), the lowest ranking tier (ADR Decision 2 tier order).")]
	public void RankEntries_ShouldRankIdentityHitAboveBindingHit() {
		// Arrange
		ComponentRegistryEntry byCategory = new() { ComponentType = "crt.Category", Category = Term };
		ComponentRegistryEntry byBinding = new() {
			ComponentType = "crt.Binding",
			Properties = new Dictionary<string, ComponentPropertyDefinition> {
				[Term] = new() { Type = "string", Description = "irrelevant" }
			}
		};
		ComponentRegistryEntry[] entries = { byBinding, byCategory };

		// Act
		IReadOnlyList<ComponentRegistryEntry> ranked = ComponentInfoGrouping.RankEntries(entries, Term);

		// Assert
		ranked.Select(entry => entry.ComponentType).Should().Equal(new[] { "crt.Category", "crt.Binding" },
			because: "an identity-field hit must rank above a binding-surface hit");
		ComponentInfoGrouping.ScoreEntry(byCategory, Term).Should().BeGreaterThan(
			ComponentInfoGrouping.ScoreEntry(byBinding, Term),
			because: "identity fields carry a higher tier weight than the inputs/outputs/properties surface");
	}

	[Test]
	[Description("Entries with an equal score are ordered by ComponentType using OrdinalIgnoreCase ascending, so the ranking is stable and identical across macOS, Linux and Windows runners (ADR Decision 2 deterministic tie-break).")]
	public void RankEntries_ShouldBreakScoreTiesByComponentTypeOrdinalIgnoreCase() {
		// Arrange — equal score (both match the term once in synonyms); names chosen so a case-SENSITIVE
		// ordinal sort ("crt.Banana" before "crt.apple") would disagree with the OrdinalIgnoreCase order.
		ComponentRegistryEntry lowerCase = new() { ComponentType = "crt.apple", Synonyms = new[] { Term } };
		ComponentRegistryEntry upperCase = new() { ComponentType = "crt.Banana", Synonyms = new[] { Term } };
		ComponentRegistryEntry[] entries = { upperCase, lowerCase };

		// Act
		IReadOnlyList<ComponentRegistryEntry> ranked = ComponentInfoGrouping.RankEntries(entries, Term);

		// Assert
		ranked.Select(entry => entry.ComponentType).Should().Equal(new[] { "crt.apple", "crt.Banana" },
			because: "tied scores break by ComponentType OrdinalIgnoreCase ascending — 'apple' before 'Banana' — not a case-sensitive ordinal sort");
	}

	[Test]
	[Description("A blank/whitespace search returns the full catalog ordered alphabetically by ComponentType (faceted-discovery list mode), since there is no ranking signal to apply.")]
	public void RankEntries_ShouldReturnFullCatalogAlphabetically_WhenSearchIsBlank() {
		// Arrange
		ComponentRegistryEntry[] entries = {
			new() { ComponentType = "crt.Zed" },
			new() { ComponentType = "crt.Abc" },
			new() { ComponentType = "crt.Mid" }
		};

		// Act
		IReadOnlyList<ComponentRegistryEntry> ranked = ComponentInfoGrouping.RankEntries(entries, "   ");

		// Assert
		ranked.Select(entry => entry.ComponentType).Should().Equal(new[] { "crt.Abc", "crt.Mid", "crt.Zed" },
			because: "an empty query lists the whole catalog alphabetically, preserving list-mode discovery behaviour");
	}

	[Test]
	[Description("Entries that match no query term are excluded from the ranked list, preserving the filter semantics of the original binary matcher (only relevant components surface).")]
	public void RankEntries_ShouldExcludeEntriesWithNoMatch() {
		// Arrange
		ComponentRegistryEntry match = new() { ComponentType = "crt.Gallery", Synonyms = new[] { "photo grid" } };
		ComponentRegistryEntry noMatch = new() { ComponentType = "crt.Button", Description = "Action button." };
		ComponentRegistryEntry[] entries = { match, noMatch };

		// Act
		IReadOnlyList<ComponentRegistryEntry> ranked = ComponentInfoGrouping.RankEntries(entries, "photo");

		// Assert
		ranked.Should().ContainSingle(entry => entry.ComponentType == "crt.Gallery",
			because: "only entries with a positive score are returned; a non-matching component is dropped");
	}

	[Test]
	[Description("Ranking output is identical regardless of input order — the score-then-name ordering is a total, stable order. This is what gives the MCP tool and the CLI verb identical deterministic output (CLI/MCP parity, ADR Decision 2).")]
	public void RankEntries_ShouldBeDeterministic_RegardlessOfInputOrder() {
		// Arrange
		IReadOnlyList<ComponentRegistryEntry> catalog = ComponentDiscoveryLabelledSet.Load().Components;
		const string query = "editable data table with columns";

		// Act
		IReadOnlyList<string> forward = ComponentInfoGrouping.RankEntries(catalog.ToArray(), query)
			.Select(entry => entry.ComponentType).ToArray();
		IReadOnlyList<string> reversed = ComponentInfoGrouping.RankEntries(catalog.Reverse().ToArray(), query)
			.Select(entry => entry.ComponentType).ToArray();

		// Assert
		reversed.Should().Equal(forward,
			because: "the ranked order must not depend on the catalog's input order — both surfaces must agree");
	}

	[Test]
	[Description("The ENG-91134 motivating case: the natural-language need 'photo gallery for property cards' surfaces crt.Gallery as the top-ranked component, above the grid/file-list types the agent previously settled on (AC-01 evidence).")]
	public void RankEntries_ShouldSurfaceGalleryForNaturalLanguageCardQuery() {
		// Arrange
		IReadOnlyList<ComponentRegistryEntry> catalog = ComponentDiscoveryLabelledSet.Load().Components;

		// Act
		IReadOnlyList<ComponentRegistryEntry> ranked =
			ComponentInfoGrouping.RankEntries(catalog.ToArray(), "photo gallery for property cards");

		// Assert
		ranked.Should().NotBeEmpty(because: "a natural-language card-gallery need must match at least one component");
		ranked[0].ComponentType.Should().Be("crt.Gallery",
			because: "the reopened ENG-91134 bug was the agent picking a grid/file-list — ranked search must put crt.Gallery first for this need");
		int galleryRank = IndexOf(ranked, "crt.Gallery");
		int dataGridRank = IndexOf(ranked, "crt.DataGrid");
		if (dataGridRank >= 0) {
			galleryRank.Should().BeLessThan(dataGridRank,
				because: "crt.Gallery must outrank crt.DataGrid for an image-card need");
		}
	}

	[Test]
	[Description("Deterministic (no-LLM) recall@5 over the shared labelled set meets the ADR threshold (>= 0.8), the go/no-go bar that lets the epic stop at A+B+D without vector search (ADR Decision 3). Uses the shared set, not a single anecdote (ADR Decision 6).")]
	public void RankEntries_ShouldMeetDeterministicRecallThresholdOnLabelledSet() {
		// Arrange
		ComponentDiscoveryLabelledSet.LabelledSet set = ComponentDiscoveryLabelledSet.Load();
		IReadOnlyList<ComponentDiscoveryLabelledSet.LabelledQuery> queries = set.RankingQueries;
		ComponentRegistryEntry[] catalog = set.Components.ToArray();
		const int k = 5;
		const double threshold = 0.8; // ADR Decision 3

		// Act — a query is a hit when any expected component appears in the top-k ranked results.
		int hits = queries.Count(query => {
			List<string> topK = ComponentInfoGrouping.RankEntries(catalog, query.Query)
				.Take(k).Select(entry => entry.ComponentType).ToList();
			return query.Expected.Any(topK.Contains);
		});
		double recall = (double)hits / queries.Count;

		// Assert
		queries.Should().NotBeEmpty(because: "the labelled set must contribute ranking queries to measure");
		recall.Should().BeGreaterThanOrEqualTo(threshold,
			because: $"deterministic recall@{k} must clear the ADR go/no-go bar ({threshold:P0}); measured {recall:P0} on {queries.Count} queries");
	}

	[Test]
	[Description("Sanity-checks the shared labelled set so Solutions C and E inherit a non-trivial source: it carries the confusable component family (Gallery/DataGrid/List/FileList/ImageInput) and at least one non-ranking version-unknown row.")]
	public void LabelledSet_ShouldCoverConfusableComponentsAndVersionUnknownCase() {
		// Arrange
		ComponentDiscoveryLabelledSet.LabelledSet set = ComponentDiscoveryLabelledSet.Load();

		// Act
		IReadOnlyList<string> types = set.Components.Select(entry => entry.ComponentType).ToList();
		bool hasVersionUnknownRow = set.Queries.Any(query =>
			!string.Equals(query.Kind, ComponentDiscoveryLabelledSet.RankingKind, System.StringComparison.OrdinalIgnoreCase));

		// Assert
		string[] confusableFamily = { "crt.Gallery", "crt.DataGrid", "crt.List", "crt.FileList", "crt.ImageInput" };
		confusableFamily.Should().OnlyContain(componentType => types.Contains(componentType),
			because: "the confusable component family is the heart of the ENG-91134 ranking problem and must be present for B/C");
		set.RankingQueries.Count.Should().BeGreaterThanOrEqualTo(10,
			because: "a single anecdote is explicitly rejected by the ADR — the set must hold a meaningful number of ranking queries");
		hasVersionUnknownRow.Should().BeTrue(
			because: "Solution E reuses the same shared set for the version-unknown communication case");
	}

	private static int IndexOf(IReadOnlyList<ComponentRegistryEntry> entries, string componentType) {
		for (int index = 0; index < entries.Count; index++) {
			if (entries[index].ComponentType == componentType) {
				return index;
			}
		}
		return -1;
	}
}
