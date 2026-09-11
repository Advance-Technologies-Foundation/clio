using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Machine-readable error identity on <see cref="SchemaValidationResult"/> (issue #1464). The
/// persisted-resource-key rescue used to be gated by substring-matching a user-facing diagnostic
/// sentence. A sentence is a wording decision — it gets reworded, gains an appended hint, is localized —
/// and every such edit silently disarmed the gate with nothing to notice it.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class SchemaValidationErrorKindTests {

	private const string UnresolvedLabelSentence =
		"is neither auto-provided by a DS-bound attribute nor registered in the 'resources' parameter.";

	private static string BuildDiffBackedPageBody(string viewConfigDiff, string viewModelConfigDiff) =>
		$$"""
		define(
			"UsrTodo_FormPage",
			/**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/,
			function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/{
				return {
					viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/{{viewConfigDiff}}/**SCHEMA_VIEW_CONFIG_DIFF*/,
					viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/{{viewModelConfigDiff}}/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
					modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
					handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
					converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
					validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
				};
			}
		);
		""";

	/// <summary>The unresolved-label shape: the label key cannot be auto-provided from the bound attribute.</summary>
	private static string BuildUnresolvedLabelBody(string resourceKey) =>
		BuildDiffBackedPageBody(
			"""
			[{"operation":"insert","name":"CaseSLA","values":{"type":"crt.Input","label":"$Resources.Strings.
			""".TrimEnd() + resourceKey + """
			","control":"$PDS_CaseSLA"}}]
			""",
			"""
			[{"operation":"merge","path":[],"values":{"attributes":{"PDS_CaseSLA":{"modelConfig":{"path":"PDS.UsrSLA"}}}}}]
			""");

	[Test]
	[Description("The unresolved-label rejection carries its stable identity, and the user-facing sentence is unchanged - the two halves of the same rule, one for a machine and one for a person (issue #1464).")]
	public void ValidateInsertedFieldSelfConsistency_ShouldTagTheUnresolvedLabelRejection_WithItsErrorKind() {
		// Arrange
		string body = BuildUnresolvedLabelBody("NeverRegistered_label");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body, null);

		// Assert
		result.IsValid.Should().BeFalse(because: "the label key resolves nowhere, so the label renders blank");
		result.ErrorKinds.Should().Contain(SchemaValidationErrorKind.UnresolvedLabelResource,
			because: "the rescue branches on this identity instead of on the sentence");
		result.Errors.Should().Contain(error => error.Contains(UnresolvedLabelSentence),
			because: "the wording a human reads must be unchanged by the machine-readable identity");
	}

	[Test]
	[Description("A clean body carries no error identity - an empty ErrorKinds set must mean 'nothing branchable was reported', never 'valid' by omission (issue #1464).")]
	public void ValidateInsertedFieldSelfConsistency_ShouldReportNoErrorKind_WhenTheBodyIsClean() {
		// Arrange
		string body = BuildDiffBackedPageBody("[]", "[]");

		// Act
		SchemaValidationResult result = SchemaValidationService.ValidateInsertedFieldSelfConsistency(body, null);

		// Assert
		result.IsValid.Should().BeTrue(
			because: "an empty viewConfigDiff declares no inserted field for the validator to reject");
		result.ErrorKinds.Should().BeEmpty(because: "no rejection was reported, so there is nothing to identify");
	}

	[Test]
	[Description("Mutation guard, half one: the rescue opens on the KIND. A body whose only rejection is the unresolved label consults the provider exactly once (issue #1464).")]
	public void ValidateFieldLabelResources_ShouldConsultTheProvider_WhenTheRejectionCarriesTheKind() {
		// Arrange
		string body = BuildUnresolvedLabelBody("CaseSLA_label");
		int providerCalls = 0;

		// Act
		(SchemaValidationResult _, SchemaValidationResult insertedFields) =
			SchemaValidationService.ValidateFieldLabelResources(body, null, () => {
				providerCalls++;
				return new HashSet<string>(StringComparer.Ordinal) { "CaseSLA_label" };
			});

		// Assert
		providerCalls.Should().Be(1,
			because: "the kind is what opens the gate, and one lookup per rescued body is its whole budget");
		insertedFields.IsValid.Should().BeTrue(
			because: "a key already persisted on the schema clears exactly this rejection");
	}

	[Test]
	[Description("Mutation guard, half two: the identity is carried, never inferred. A rejection recorded through the plain Errors list does NOT gain the kind even when its text contains the invariant clause verbatim - which is what proves the gate can no longer be opened or closed by a wording edit (issue #1464).")]
	public void SchemaValidationResult_ShouldNotInferTheErrorKind_FromTheDiagnosticText() {
		// Arrange
		SchemaValidationResult result = new() { IsValid = false };

		// Act
		result.Errors.Add("Inserted field 'X' has label '$Resources.Strings.X_label' but resource 'X_label' "
			+ UnresolvedLabelSentence);

		// Assert
		result.ErrorKinds.Should().BeEmpty(
			because: "only AddError carries an identity - if the text implied it, rewording the sentence would move the gate again");
	}

	[Test]
	[Description("Merging one result into another carries the error IDENTITIES along with the messages: a merge that copies only Errors keeps the unresolved-label sentence while the rescue - which reads the kind, not the text - stops firing for it (issue #1464 review).")]
	public void MergeResult_ShouldCarryTheErrorKinds_NotOnlyTheMessages() {
		// Arrange
		SchemaValidationResult target = new() { IsValid = true };
		SchemaValidationResult source = new() { IsValid = false };
		source.AddError(SchemaValidationErrorKind.UnresolvedLabelResource, "the rejection");
		source.Warnings.Add("a warning");

		// Act
		SchemaValidationService.MergeResult(target, source);

		// Assert
		target.IsValid.Should().BeFalse(
			because: "a merged-in rejection must make the combined verdict a rejection");
		target.Errors.Should().Contain("the rejection",
			because: "the message half of the merge is what a human reads");
		target.ErrorKinds.Should().Contain(SchemaValidationErrorKind.UnresolvedLabelResource,
			because: "the identity half is what a caller branches on, and dropping it disarms the rescue silently");
		target.Warnings.Should().Contain("a warning",
			because: "warnings merge whether or not the source rejected");
	}

	[Test]
	[Description("A clean merge source contributes no identity, so ErrorKinds stays an honest 'nothing branchable was reported' rather than accumulating noise (issue #1464 review).")]
	public void MergeResult_ShouldContributeNoErrorKind_WhenTheSourceIsValid() {
		// Arrange
		SchemaValidationResult target = new() { IsValid = true };
		SchemaValidationResult source = new() { IsValid = true };
		source.Warnings.Add("only a warning");

		// Act
		SchemaValidationService.MergeResult(target, source);

		// Assert
		target.IsValid.Should().BeTrue(because: "a valid source cannot turn the target into a rejection");
		target.ErrorKinds.Should().BeEmpty(because: "no rejection was merged, so there is no identity to carry");
	}

	[Test]
	[Description("The identity survives a reworded diagnostic: AddError records the kind independently of the message, so the rescue keeps firing after a wording change that a substring-keyed gate would have missed (issue #1464).")]
	public void SchemaValidationResult_ShouldKeepTheErrorKind_WhenTheDiagnosticIsReworded() {
		// Arrange
		SchemaValidationResult result = new() { IsValid = false };

		// Act
		result.AddError(SchemaValidationErrorKind.UnresolvedLabelResource, "a completely different sentence");

		// Assert
		result.ErrorKinds.Should().Contain(SchemaValidationErrorKind.UnresolvedLabelResource,
			because: "the identity is what a caller branches on, and it is independent of the wording");
		result.Errors.Should().ContainSingle().Which.Should().Be("a completely different sentence",
			because: "AddError records the message verbatim - the identity is carried beside it, not encoded in it");
	}
}
