using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>One row of <c>Fixtures/site-text-redaction-cases.json</c>: an invented site text and what must go and stay.</summary>
public sealed record SiteTextRedactionCase {

	/// <summary>The row id, <c>case-NNN</c> after the first host case it comes from.</summary>
	[JsonPropertyName("id")] public string Id { get; init; }

	/// <summary>The host lane's case numbers with this input.</summary>
	[JsonPropertyName("cases")] public int[] Cases { get; init; }

	/// <summary>A, B, C, D or E, as the fixture header defines them.</summary>
	[JsonPropertyName("class")] public string Class { get; init; }

	/// <summary>The kind of a B row (scheme, cred-key, sign-in, …), or a C/D row's note kind.</summary>
	[JsonPropertyName("kind")] public string Kind { get; init; }

	/// <summary><c>redact</c> or <c>site</c>: the weakest entry point that must satisfy the row.</summary>
	[JsonPropertyName("entry")] public string Entry { get; init; }

	/// <summary>The site text.</summary>
	[JsonPropertyName("input")] public string Input { get; init; }

	/// <summary>The secret cores that must not appear in the output.</summary>
	[JsonPropertyName("mustGo")] public string[] MustGo { get; init; }

	/// <summary>The phrases carrying the site's reason that must appear in the output verbatim.</summary>
	[JsonPropertyName("mustStay")] public string[] MustStay { get; init; }

	/// <summary>Why the row's mustGo is not asserted yet, or <see langword="null"/>.</summary>
	[JsonPropertyName("pending")] public string Pending { get; init; }

	/// <summary>
	/// Option B: the mustStay fragments master's frozen chain removes (or one of clio's extra rules, named so in the
	/// reason). They are asserted ABSENT, so a change to what the chain removes shows up as a failing row.
	/// </summary>
	[JsonPropertyName("overMatch")] public string[] OverMatch { get; init; }

	/// <summary>Which rule removes the row's <see cref="OverMatch"/> fragments.</summary>
	[JsonPropertyName("overMatchReason")] public string OverMatchReason { get; init; }

	/// <summary>The mustStay fragments that must stay: all of them but the declared over-matches.</summary>
	internal IEnumerable<string> KeptMustStay => MustStay.Except(OverMatch ?? []);

	/// <inheritdoc />
	public override string ToString() => Id;
}

/// <summary>
/// Runs every row of the site-text redaction fixture - the Accelerator host lane's 350 invented site texts, each
/// classified A/B/C/D - through <see cref="SensitiveErrorTextRedactor.Redact"/> and
/// <see cref="SensitiveErrorTextRedactor.RedactSiteText"/>.
/// </summary>
[TestFixture]
[Property("Module", "Common")]
public sealed class SiteTextRedactionFixtureTests {

	/// <summary>The fixture, read from the source tree: the test output directory is four levels below it.</summary>
	internal static readonly string FixturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
		"..", "..", "..", "..", "clio.tests", "Common", "Fixtures", "site-text-redaction-cases.json"));

	private static IReadOnlyList<SiteTextRedactionCase> cachedRows;

	internal static IReadOnlyList<SiteTextRedactionCase> LoadRows() {
		if (cachedRows is not null) {
			return cachedRows;
		}
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(FixturePath));
		cachedRows = document.RootElement.GetProperty("rows").Deserialize<List<SiteTextRedactionCase>>();
		return cachedRows;
	}

	private static IEnumerable<TestCaseData> RunnableRows() =>
		LoadRows().Where(row => row.Pending is null)
			.Select(row => new TestCaseData(row).SetArgDisplayNames(row.Id, row.Class, row.Entry));

	private static IEnumerable<TestCaseData> PendingRows() =>
		LoadRows().Where(row => row.Pending is not null)
			.Select(row => new TestCaseData(row).SetArgDisplayNames(row.Id, row.Class, row.Kind));

	[TestCaseSource(nameof(RunnableRows))]
	[Category("Unit")]
	[Description("RedactSiteText removes every mustGo fragment of a row and keeps every mustStay fragment: the "
		+ "host lane's B rows close, its C rows (the site's reason) and D rows (what the host removes - its "
		+ "documented trades, or a shape clio declines by choice) stay, and the A rows clio already covered stay "
		+ "covered.")]
	public void RedactSiteText_ShouldRemoveMustGoAndKeepMustStay_ForEveryFixtureRow(SiteTextRedactionCase row) {
		// Arrange
		string input = row.Input;

		// Act
		string output = SensitiveErrorTextRedactor.RedactSiteText(input);

		// Assert
		Failures(output, row.MustGo, row.KeptMustStay).Concat(OverMatchFailures(output, row)).Should().BeEmpty(
			because: $"row {row.Id} ({row.Class}{(row.Kind is null ? "" : " " + row.Kind)}) gave \"{output}\" for \"{input}\"");
	}

	[TestCaseSource(nameof(RunnableRows))]
	[Category("Unit")]
	[Description("Redact satisfies every row whose entry is 'redact' in full, and keeps every mustStay fragment of "
		+ "the 'site' rows too: the rules Redact gained are the ones safe on any text, and none of them takes the "
		+ "site's reason.")]
	public void Redact_ShouldSatisfyRedactRows_AndKeepMustStayOfSiteRows(SiteTextRedactionCase row) {
		// Arrange
		string input = row.Input;
		IReadOnlyList<string> mustGo = row.Entry == "redact" ? row.MustGo : [];

		// Act
		string output = SensitiveErrorTextRedactor.Redact(input);

		// Assert
		Failures(output, mustGo, row.KeptMustStay).Concat(OverMatchFailures(output, row)).Should().BeEmpty(
			because: $"row {row.Id} ({row.Class}, entry {row.Entry}) gave \"{output}\" for \"{input}\"");
	}

	[TestCaseSource(nameof(PendingRows))]
	[Category("Unit")]
	[Description("The SQL rows that wait for rule 9 (PR B) still keep their mustStay fragments under both entry "
		+ "points: PR A's rules must not take the words around a statement either.")]
	public void BothEntryPoints_ShouldKeepMustStay_ForRowsPendingRuleNine(SiteTextRedactionCase row) {
		// Arrange
		string input = row.Input;

		// Act
		string site = SensitiveErrorTextRedactor.RedactSiteText(input);
		string redact = SensitiveErrorTextRedactor.Redact(input);

		// Assert
		Failures(site, [], row.MustStay).Should().BeEmpty(because: $"RedactSiteText gave \"{site}\" for row {row.Id}");
		Failures(redact, [], row.MustStay).Should().BeEmpty(because: $"Redact gave \"{redact}\" for row {row.Id}");
	}

	[Test]
	[Category("Unit")]
	[Description("The fixture holds the host lane's 350 distinct inputs in their classes - the gap analysis's counts, "
		+ "with the 12 rows clio has nothing to remove from moved from A and B to D and case-320 moved to A - and "
		+ "every rule-9 SQL row, and only those, pending for PR B.")]
	public void Fixture_ShouldHoldTheAnalysedRows_WithOnlySqlRowsPending() {
		// Arrange
		IReadOnlyList<SiteTextRedactionCase> rows = LoadRows();

		// Act
		Dictionary<string, int> byClass = rows.GroupBy(row => row.Class).ToDictionary(group => group.Key, group => group.Count());
		string[] pendingKinds = rows.Where(row => row.Pending is not null).Select(row => row.Kind).Distinct().ToArray();

		// Assert
		rows.Should().HaveCount(350, because: "the 42 host tests expand to 350 distinct inputs");
		rows.Select(row => row.Input).Should().OnlyHaveUniqueItems(because: "a repeated host case is one row with several case numbers");
		byClass.Should().BeEquivalentTo(new Dictionary<string, int> { ["A"] = 46, ["B"] = 167, ["C"] = 111, ["D"] = 25, ["E"] = 1 },
			because: "the gap analysis counted A 46, B 179, C 120 assertions (112 distinct inputs) and D 13; the 11 B rows "
				+ "and 1 A row clio keeps by choice are D (the host removes something there and clio does not), case-320, "
				+ "whose password master's JSON rule already removes, is A, and case-357, which the host keeps and the Bearer "
				+ "floor removes, is E");
		pendingKinds.Should().Equal(["sql"], because: "only rule 9 (SQL) is left to PR B");
		rows.Count(row => row.Pending is not null).Should().Be(71, because: "the analysis counted 71 SQL rows");
		rows.Should().OnlyContain(row => row.Entry == "redact" || row.Entry == "site",
			because: "each row names the weakest entry point that must satisfy it");
	}

	// Words that carry no reason on their own; a mustStay fragment must hold more than these.
	private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase) {
		"a", "an", "the", "is", "are", "was", "were", "be", "been", "not", "no", "or", "and", "to", "of", "in", "on", "at",
		"by", "for", "as", "it", "its", "with", "from", "too", "now", "here", "this", "that"
	};

	[Test]
	[Category("Unit")]
	[Description("Every row asserts something real: an A, B or E row names the secret core that must go, a C or D row "
		+ "names nothing that must go, every fragment is taken from the input, and no mustStay fragment of a runnable "
		+ "row is a stop-word alone - so a row cannot pass on an assertion that holds for any output.")]
	public void Fixture_ShouldAssertTheSecretCoreAndTheReasonPhrase_InEveryRow() {
		// Arrange
		IReadOnlyList<SiteTextRedactionCase> rows = LoadRows();

		// Act
		List<string> wrong = [];
		foreach (SiteTextRedactionCase row in rows) {
			bool removes = row.Class is "A" or "B" or "E";
			if (removes && row.Pending is null && row.MustGo.Length == 0) {
				wrong.Add($"{row.Id} ({row.Class}) names nothing that must go");
			}
			if (!removes && row.MustGo.Length > 0) {
				wrong.Add($"{row.Id} ({row.Class}) is a keep row with a mustGo");
			}
			wrong.AddRange(row.MustGo.Concat(row.MustStay).Where(fragment => !row.Input.Contains(fragment, StringComparison.Ordinal))
				.Select(fragment => $"{row.Id}: \"{fragment}\" is not in the input"));
			if (row.Pending is null) {
				wrong.AddRange(row.MustStay.Where(fragment => Regex.Matches(fragment, @"[A-Za-z0-9_]+")
						.All(word => StopWords.Contains(word.Value)))
					.Select(fragment => $"{row.Id}: mustStay \"{fragment}\" is only stop-words"));
			}
		}

		// Assert
		wrong.Should().BeEmpty(because: "each row must fail when its secret survives or its reason is lost");
	}

	// A declared over-match that no longer happens: master's chain or the named rule changed, so the row must change too.
	private static IEnumerable<string> OverMatchFailures(string output, SiteTextRedactionCase row) =>
		(row.OverMatch ?? []).Where(fragment => output.Contains(fragment, StringComparison.Ordinal))
			.Select(fragment => $"declared over-match \"{fragment}\" ({row.OverMatchReason}) no longer happens");

	[Test]
	[Category("Unit")]
	[Description("Every declared over-match names its reason and is one of the row's mustStay fragments, so a row can "
		+ "only give up a reason phrase that it states and explains.")]
	public void Fixture_ShouldExplainEveryDeclaredOverMatch() {
		// Arrange
		IReadOnlyList<SiteTextRedactionCase> rows = LoadRows();

		// Act
		string[] wrong = rows.Where(row => row.OverMatch is { Length: > 0 }
				&& (string.IsNullOrWhiteSpace(row.OverMatchReason) || row.OverMatch.Except(row.MustStay).Any()))
			.Select(row => row.Id).ToArray();

		// Assert
		wrong.Should().BeEmpty(because: "an over-match is declared with its reason, fragment by mustStay fragment");
		rows.Count(row => row.OverMatch is { Length: > 0 }).Should().Be(32,
			because: "option B declares 32 rows: master's chain removes their reason phrase, or Extra does (case-055)");
	}

	private static List<string> Failures(string output, IEnumerable<string> mustGo, IEnumerable<string> mustStay) {
		List<string> failures = [];
		failures.AddRange(mustGo.Where(fragment => output.Contains(fragment, StringComparison.Ordinal))
			.Select(fragment => $"still holds \"{fragment}\""));
		failures.AddRange(mustStay.Where(fragment => !output.Contains(fragment, StringComparison.Ordinal))
			.Select(fragment => $"lost \"{fragment}\""));
		return failures;
	}
}
