using System;
using Clio.Command.BusinessRules;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.BusinessRules;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class RequiredFieldAggregationTests {

	[Test]
	[Description("Every missing identity field is reported in one message instead of one failed call per field (issue #1305, point 3).")]
	public void RequireBatchFields_WhenPackageAndSchemaMissing_ReportsBothInOneMessage() {
		// Arrange
		BusinessRule rule = new() { Name = "Rule_1" };

		// Act
		Action act = () => BusinessRuleBatchValidation.RequireBatchFields(
			null, null, "page-schema-name", [rule]);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("*package-name, page-schema-name are required.*",
				because: "reporting one missing field at a time costs the caller a failed round trip per field");
	}

	[Test]
	[Description("A single missing identity field keeps the singular wording so existing callers and docs stay accurate.")]
	public void RequireBatchFields_WhenOnlySchemaMissing_KeepsSingularWording() {
		// Arrange
		BusinessRule rule = new() { Name = "Rule_1" };

		// Act
		Action act = () => BusinessRuleBatchValidation.RequireBatchFields(
			"UsrPkg", null, "page-schema-name", [rule]);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("*page-schema-name is required.*",
				because: "a single missing field must keep the established singular message");
	}

	[Test]
	[Description("The pre-environment aggregate names environment-name, the identity fields AND the empty collection in one message — PR #1352 review: three of the four page tools pre-checked only the collection field, so a caller who omitted the identity fields learned about them one failed call at a time.")]
	public void MissingRequestFieldsError_WhenEverythingMissing_NamesEveryFieldInOneMessage() {
		// Act
		string? error = BusinessRuleBatchValidation.MissingRequestFieldsError(
			null, null, null, "page-schema-name", "rules", 0);

		// Assert
		error.Should().Be(
			"environment-name, package-name, page-schema-name are required. "
			+ "rules is required and must contain at least one rule.",
			because: "one message per bad request is the whole point of the aggregate; the environment is in it "
				+ "because the executor resolves the environment first and an unknown name would answer ahead "
				+ "of the identity fields");
	}

	[Test]
	[Description("The collection field keeps its own established wording per tool, so delete's rule-names message is not silently reworded.")]
	public void MissingRequestFieldsError_WhenOnlyRuleNamesEmpty_KeepsTheDeleteWording() {
		// Act
		string? error = BusinessRuleBatchValidation.MissingRequestFieldsError(
			"dev", "UsrPkg", "UsrOrder_FormPage", "page-schema-name", "rule-names", 0);

		// Assert
		error.Should().Be("rule-names is required and must contain at least one rule name.",
			because: "delete's message predates this aggregate and callers/docs quote it verbatim");
	}

	[Test]
	[Description("A complete request shape produces no error — otherwise every call would be rejected and the negatives above would pass vacuously.")]
	public void MissingRequestFieldsError_WhenRequestIsComplete_ReturnsNull() {
		// Act & Assert
		BusinessRuleBatchValidation.MissingRequestFieldsError(
			"dev", "UsrPkg", "UsrOrder_FormPage", "page-schema-name", "rules", 1)
			.Should().BeNull(because: "a well-formed request must reach the service");
	}

	[Test]
	[Description("read has no collection field, so passing none is a supported shape rather than an empty-collection error.")]
	public void MissingRequestFieldsError_WhenToolHasNoCollectionField_IgnoresTheCount() {
		// Act & Assert
		BusinessRuleBatchValidation.MissingRequestFieldsError(
			"dev", "UsrPkg", "UsrOrder_FormPage", "page-schema-name", null, 0)
			.Should().BeNull(because: "read-page-business-rules carries no rules array to be empty");
	}
}
