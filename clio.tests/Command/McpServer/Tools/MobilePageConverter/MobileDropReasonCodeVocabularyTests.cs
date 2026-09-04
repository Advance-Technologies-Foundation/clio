namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter;

using System;
using System.Collections.Generic;
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

	/// <summary>Reads the vocabulary off the production constants, so the pin cannot drift from the source.</summary>
	private static IReadOnlyCollection<string> DeclaredCodes() =>
		[.. typeof(ReasonCodes)
			.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
			.Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
			.Select(field => (string)field.GetRawConstantValue())];
}
