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
}
