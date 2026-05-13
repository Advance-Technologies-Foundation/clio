using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.BusinessRules.Filters;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.BusinessRules.Filters;

[TestFixture]
[Property("Module", "Command.BusinessRules.Filters")]
public sealed class LlmFiltersValidatorTests {

	[Test]
	[Category("Unit")]
	[Description("Throws filter.required when the supplied filter group is null.")]
	public void Validate_Should_Throw_FilterRequired_When_Filter_Is_Null() {
		// Act
		System.Action act = () => LlmFiltersValidator.Validate(null!);

		// Assert
		act.Should().Throw<BusinessRuleFilterException>()
			.Which.ErrorCode.Should().Be(BusinessRuleFilterErrorCodes.FilterRequired);
	}

	[Test]
	[Category("Unit")]
	[Description("Throws filter.logical-operation-unknown for tokens other than AND/OR.")]
	public void Validate_Should_Throw_LogicalOperationUnknown_For_Unknown_Token() {
		// Arrange
		FriendlyFilterGroup group = new("XOR", [], []);

		// Act
		System.Action act = () => LlmFiltersValidator.Validate(group);

		// Assert
		act.Should().Throw<BusinessRuleFilterException>()
			.Which.ErrorCode.Should().Be(BusinessRuleFilterErrorCodes.LogicalOperationUnknown);
	}

	[Test]
	[Category("Unit")]
	[Description("Throws filter.path-unknown when a leaf has no columnPath.")]
	public void Validate_Should_Throw_PathUnknown_When_Leaf_ColumnPath_Is_Empty() {
		// Arrange
		FriendlyFilterGroup group = new("AND",
			[new FriendlyFilterLeaf(string.Empty, "EQUAL", JsonDocument.Parse("\"x\"").RootElement)],
			[]);

		// Act
		System.Action act = () => LlmFiltersValidator.Validate(group);

		// Assert
		act.Should().Throw<BusinessRuleFilterException>()
			.Which.ErrorCode.Should().Be(BusinessRuleFilterErrorCodes.PathUnknown);
	}

	[Test]
	[Category("Unit")]
	[Description("Throws filter.comparison-unknown for tokens not in the supported set.")]
	public void Validate_Should_Throw_ComparisonUnknown_For_Unknown_Token() {
		// Arrange
		FriendlyFilterGroup group = new("AND",
			[new FriendlyFilterLeaf("Name", "MATCHES", JsonDocument.Parse("\"x\"").RootElement)],
			[]);

		// Act
		System.Action act = () => LlmFiltersValidator.Validate(group);

		// Assert
		act.Should().Throw<BusinessRuleFilterException>()
			.Which.ErrorCode.Should().Be(BusinessRuleFilterErrorCodes.ComparisonUnknown);
	}

	[Test]
	[Category("Unit")]
	[Description("Throws filter.value-required when a binary comparison leaf has no value.")]
	public void Validate_Should_Throw_ValueRequired_For_Binary_Without_Value() {
		// Arrange
		FriendlyFilterGroup group = new("AND",
			[new FriendlyFilterLeaf("Name", "EQUAL", null)],
			[]);

		// Act
		System.Action act = () => LlmFiltersValidator.Validate(group);

		// Assert
		act.Should().Throw<BusinessRuleFilterException>()
			.Which.ErrorCode.Should().Be(BusinessRuleFilterErrorCodes.ValueRequired);
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts unary IS_NULL leaves without requiring a value.")]
	public void Validate_Should_Accept_Unary_Without_Value() {
		// Arrange
		FriendlyFilterGroup group = new("AND",
			[new FriendlyFilterLeaf("Name", "IS_NULL", null)],
			[]);

		// Act
		System.Action act = () => LlmFiltersValidator.Validate(group);

		// Assert
		act.Should().NotThrow();
	}

	[Test]
	[Category("Unit")]
	[Description("Throws filter.backward-reference-not-1n when referenceColumnPath is missing.")]
	public void Validate_Should_Throw_BackwardReference_When_Path_Missing() {
		// Arrange
		FriendlyFilterGroup nested = new("AND", [], []);
		FriendlyFilterGroup group = new("AND",
			[],
			[new BackwardReferenceFilter(string.Empty, nested)]);

		// Act
		System.Action act = () => LlmFiltersValidator.Validate(group);

		// Assert
		act.Should().Throw<BusinessRuleFilterException>()
			.Which.ErrorCode.Should().Be(BusinessRuleFilterErrorCodes.BackwardReferenceNot1N);
	}

	[Test]
	[Category("Unit")]
	[Description("Validates a binary EQUAL leaf with a value as the happy-path baseline.")]
	public void Validate_Should_Pass_Happy_Path() {
		// Arrange
		FriendlyFilterGroup group = new("AND",
			[new FriendlyFilterLeaf(
				"Country",
				"EQUAL",
				JsonDocument.Parse("\"a470b005-e8bb-df11-b00f-001d60e938c6\"").RootElement)],
			new List<BackwardReferenceFilter>());

		// Act
		System.Action act = () => LlmFiltersValidator.Validate(group);

		// Assert
		act.Should().NotThrow();
	}
}
