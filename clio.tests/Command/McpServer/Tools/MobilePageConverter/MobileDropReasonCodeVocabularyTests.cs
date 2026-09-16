namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Clio.Command.McpServer.Tools.MobilePageConverter;
using FluentAssertions;
using NUnit.Framework;

/// <summary>
/// Pins the CLOSED VOCABULARY of <see cref="ReasonCodes"/> — the codes the converter can put in ANY
/// <c>reason</c> it returns (<c>droppedElements</c>, <c>requestConversions.droppedRequests</c> and
/// <c>flaggedRequests</c>, <c>pageBusinessRules.droppedRules</c>, <c>normalizations.*.skipped</c>) — so
/// that adding, renaming or removing one cannot ship without the guidance article that decodes it moving
/// in the same change.
/// <para>
/// This exists because ENG-95827 proved the gap is invisible otherwise. Its last commit folded
/// <c>relocate-children</c> into <c>droppedElements</c> as
/// <see cref="ReasonCodes.DropContainerNoMobileEquivalent"/>, the article did not gain an entry, and the
/// whole suite stayed green: the code fires on ZERO of the 12 drops of the OOTB <c>Leads_FormPage</c> the
/// conversion work was measured on, so no fixture and no manual read of a real response could have shown
/// it missing.
/// </para>
/// <para>
/// The cost of the gap is not cosmetic. A caller that meets an unknown code is told — by the article
/// itself — to report it verbatim and not guess, which turns the most benign outcome the converter has
/// (a FLATTENED branch: the layout wrapper is gone, every child kept and re-parented) into an
/// unexplained loss in front of the user.
/// </para>
/// <para>
/// The other half of the guard lives in the clio-knowledge repository
/// (<c>MobileDropReasonCodeCoverageTests</c>), which pins the same set against the article text. Neither
/// repository references the other, so the contract is held from both ends instead of being compared in
/// one place: a code added HERE fails HERE, and an entry deleted THERE fails THERE.
/// </para>
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class MobileDropReasonCodeVocabularyTests {

	/// <summary>
	/// The published vocabulary. Every entry has a matching block in the clio-knowledge article
	/// <c>freedom-page-mobile-reason-codes</c>
	/// (<c>guidance/mcp/guides/platform/mobile/web-to-mobile-reason-codes.md</c>).
	/// </summary>
	/// <remarks>
	/// One vocabulary spans FOUR record kinds — a dropped element, a dropped or flagged request binding, a
	/// dropped page business rule, a skipped normalization — because a caller reads them all the same way
	/// and a per-collection vocabulary would let the same cause acquire two spellings. That is also why the
	/// <c>drop-</c> prefix is not asserted anywhere: <c>flag-</c> and <c>skip-</c> are first-class here.
	/// </remarks>
	private static readonly string[] PublishedCodes = [
		// an element that did not reach the mobile page
		"drop-empty-container",
		"drop-container-no-mobile-equivalent",
		"drop-excluded-by-rule",
		"drop-parent-excluded",
		"drop-inherited-chrome",
		"drop-target-missing",
		"drop-unsupported-request",
		"drop-type-not-in-mobile-registry",
		"drop-unknown-request",
		"drop-no-rule-in-scope",
		"drop-not-an-action-in-scope",
		"drop-non-converting-scope",
		// a request binding: lost with its element, or lost on its own
		"drop-request-chrome-native",
		"drop-request-unsupported",
		"drop-request-element-empty-container",
		"drop-request-element-excluded",
		"flag-request-unmapped",
		// a page business rule that does not convert
		"drop-rule-condition-mixed-and-or",
		"drop-rule-condition-unsupported-comparison",
		"drop-rule-condition-unconvertible",
		"drop-rule-no-action-converts",
		// a normalization the stamp refused
		"skip-normalization-path-blocked"
	];

	/// <summary>Shape an agent's <c>switch</c> and the article's lookup table both depend on.</summary>
	private static readonly Regex KebabCase = new("^[a-z][a-z0-9]*(-[a-z0-9]+)*$", RegexOptions.Compiled);

	[Test]
	[Description("The reason-code vocabulary — across every collection that carries one — is exactly the set published in the guidance article, so a code cannot be added, renamed or removed in clio alone.")]
	public void Vocabulary_ShouldBeExactlyTheSetTheGuidanceArticleDocuments() {
		// Arrange
		string[] expected = [.. PublishedCodes.OrderBy(code => code, StringComparer.Ordinal)];

		// Act
		string[] declared = [.. DeclaredCodes().OrderBy(code => code, StringComparer.Ordinal)];

		// Assert
		declared.Should().Equal(expected,
			because: "a change to this vocabulary is a TWO-REPOSITORY change: the constant here, this pin, and a "
				+ "block in the clio-knowledge article freedom-page-mobile-reason-codes "
				+ "(guidance/mcp/guides/platform/mobile/web-to-mobile-reason-codes.md) plus a libraryVersion bump. "
				+ "An undocumented code reaches the caller as an unexplained drop, which the article's own fallback "
				+ "then tells it to report as loss");
	}

	[Test]
	[Description("Every reason code is a lowercase kebab-case token, the form the article's lookup table and a caller's branch are both written against.")]
	public void Vocabulary_ShouldUseKebabCaseTokensOnly() {
		// Arrange
		IReadOnlyCollection<string> declared = DeclaredCodes();

		// Act
		string[] malformed = [.. declared.Where(code => !KebabCase.IsMatch(code))];

		// Assert
		malformed.Should().BeEmpty(
			because: "the code is the only part of a drop a caller may branch on, so it must stay a stable token "
				+ "rather than anything a reader could reasonably reformat");
	}

	[Test]
	[Description("Every reason code the converter MINTS comes from the vocabulary constants. Pinning the ReasonCodes set says what the vocabulary is, not that the emitting code uses it: a call site that passes a bare string literal ships a code no article decodes and no caller can branch on, and both guards on the set stay green because neither of them ever looks at an emission. The closed vocabulary is a claim about the wire, so it has to be checked where the wire is written.")]
	public void Emissions_ShouldMintEveryReasonCodeFromTheVocabularyConstants() {
		// Arrange — Reason(code, params...) is the only factory (pinned by the test below), so its call sites
		// are every code that can reach any of the five collections. Reading the SOURCE rather than the
		// assembly is deliberate: a const string is inlined at compile time, so in IL a literal and a constant
		// reference are the same bytes — which is precisely the distinction under test.
		string[] sources = ConverterSources();

		// Act
		List<string> literalEmissions = [];
		int callSites = 0;
		foreach (string file in sources) {
			foreach (string code in FirstArgumentsOfReasonCalls(File.ReadAllText(file))) {
				callSites++;
				if (StringLiteral.IsMatch(code)) {
					literalEmissions.Add($"{Path.GetFileName(file)}: Reason({Condense(code)}, ...)");
				}
			}
		}

		// Assert
		callSites.Should().BeGreaterThan(DeclaredCodes().Count,
			because: "the scan has to actually find the emissions — a pattern that matched nothing would make the "
				+ "assertion below pass on a converter that emitted raw strings everywhere, which is the one "
				+ "failure this test exists to catch");
		literalEmissions.Should().BeEmpty(
			because: "a code built from a string literal is outside the vocabulary by construction: the set guard "
				+ "cannot see it, the clio-knowledge article has no block decoding it, and a caller meeting it is "
				+ "told by that article to report an unexplained loss. Offenders: "
				+ string.Join("; ", literalEmissions));
	}

	[Test]
	[Description("Exactly one place builds a ReasonCode — the Reason(code, params) factory. The guard above scans that factory's call sites, so a ReasonCode constructed directly anywhere else would be invisible to it and would bypass the single point at which the code is required to be a vocabulary constant.")]
	public void Emissions_ShouldBuildEveryReasonCodeThroughTheSharedFactory() {
		// Arrange
		string[] sources = ConverterSources();

		// Act
		List<string> constructions = [.. sources.SelectMany(file =>
			ReasonCodeConstruction.Matches(File.ReadAllText(file))
				.Select(_ => Path.GetFileName(file)))];

		// Assert
		constructions.Should().ContainSingle(
			because: "being the SINGLE construction is what lets one guard cover every emission; a second "
				+ "`new ReasonCode { Code = ... }` anywhere reopens the gap silently, and zero would mean the "
				+ "pattern stopped matching the factory and this guard had quietly become vacuous. Found: "
				+ string.Join("; ", constructions));
	}

	private static string[] ConverterSources() => [.. Directory.EnumerateFiles(
		Path.Combine(RepositoryRoot, "clio", "Command", "McpServer", "Tools", "MobilePageConverter"),
		"*.cs", SearchOption.AllDirectories)];

	/// <summary>
	/// The first argument of every <c>Reason(</c> call in <paramref name="source"/>, extracted with balanced
	/// delimiters so a <c>switch</c> expression picking among constants comes back whole instead of being cut
	/// at its first comma. The factory's own DECLARATION is skipped — its first "argument" is the parameter
	/// list's <c>string code</c>, which is not an emission.
	/// </summary>
	private static IEnumerable<string> FirstArgumentsOfReasonCalls(string source) {
		foreach (Match call in ReasonCallStart.Matches(source)) {
			int i = call.Index + call.Length;
			int depth = 0;
			int from = i;
			for (; i < source.Length; i++) {
				char c = source[i];
				if (c is '(' or '[' or '{') {
					depth++;
				} else if (c is ')' or ']' or '}') {
					if (depth == 0) {
						break;
					}
					depth--;
				} else if (c == ',' && depth == 0) {
					break;
				}
			}
			string argument = source[from..i];
			if (!argument.Contains("string code", StringComparison.Ordinal)) {
				yield return argument;
			}
		}
	}

	private static string Condense(string code) =>
		Whitespace.Replace(code, " ").Trim();

	/// <summary>A <c>Reason(</c> call — not <c>.Reason(</c>, and not a longer identifier ending in it.</summary>
	private static readonly Regex ReasonCallStart =
		new(@"(?<![\w.])Reason\(", RegexOptions.Compiled);

	/// <summary>A double-quoted literal, including a verbatim or raw one.</summary>
	private static readonly Regex StringLiteral =
		new(@"@?""", RegexOptions.Compiled);

	/// <summary>A <c>ReasonCode</c> built directly.</summary>
	private static readonly Regex ReasonCodeConstruction =
		new(@"new\s+ReasonCode\s*[({]", RegexOptions.Compiled);

	private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

	private static readonly string RepositoryRoot = Path.GetFullPath(
		Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

	/// <summary>Reads the vocabulary off the production constants, so the pin cannot drift from the source.</summary>
	private static IReadOnlyCollection<string> DeclaredCodes() =>
		[.. typeof(ReasonCodes)
			.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
			.Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
			.Select(field => (string)field.GetRawConstantValue())];
}
