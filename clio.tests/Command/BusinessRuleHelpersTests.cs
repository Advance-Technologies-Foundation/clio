using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.BusinessRules;
using Clio.Command.EntitySchemaDesigner;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class BusinessRuleHelpersTests {
	[Test]
	[Category("Unit")]
	[Description("Builds a column map from current and inherited columns for exact schema column names.")]
	public void BuildColumnMap_Should_Include_Current_And_Inherited_Columns() {
		// Arrange
		EntityDesignSchemaDto entitySchema = new() {
			Columns = [
				new EntitySchemaColumnDto {
					UId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
					Name = "Status",
					DataValueType = 1
				},
				new EntitySchemaColumnDto {
					UId = Guid.Parse("30000000-0000-0000-0000-000000000003"),
					Name = "Owner",
					DataValueType = 10
				}
			],
			InheritedColumns = [
				new EntitySchemaColumnDto {
					UId = Guid.Parse("20000000-0000-0000-0000-000000000002"),
					Name = "Approver",
					DataValueType = 27
				}
			]
		};

		// Act
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = BusinessRuleHelpers.BuildColumnMap(entitySchema);

		// Assert
		columnMap.Should().HaveCount(3,
			because: "the helper should include both current and inherited columns in the returned map");
		columnMap["Status"].DataValueType.Should().Be(1,
			because: "current schema columns should be available by their exact column name");
		columnMap["Owner"].DataValueType.Should().Be(10,
			because: "current lookup columns should be preserved in the returned map");
		columnMap["Approver"].DataValueType.Should().Be(27,
			because: "inherited columns should also be available in the returned map");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps known Creatio data value type ids to their canonical business-rule names.")]
	public void MapDataValueTypeName_Should_Return_Known_Name() {
		// Arrange

		// Act
		string dataValueTypeName = BusinessRuleHelpers.MapDataValueTypeName(10);

		// Assert
		dataValueTypeName.Should().Be("Lookup",
			because: "known Creatio data value types should map to canonical business-rule names");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects unknown Creatio data value type ids.")]
	public void MapDataValueTypeName_Should_Throw_For_Unknown_Value() {
		// Arrange

		// Act
		Action act = () => BusinessRuleHelpers.MapDataValueTypeName(999);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("Unsupported entity schema dataValueType '999'.",
				because: "unknown data value types should fail fast");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects missing Creatio data value type ids.")]
	public void MapDataValueTypeName_Should_Throw_For_Null_Value() {
		// Arrange

		// Act
		Action act = () => BusinessRuleHelpers.MapDataValueTypeName(null);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("Entity schema column dataValueType is required.",
				because: "missing data value types should fail fast before metadata generation");
	}

	[Test]
	[Category("Unit")]
	[Description("Converts JSON string elements into CLR strings.")]
	public void ConvertJsonElement_Should_Return_String_Value() {
		// Arrange
		JsonElement element = JsonSerializer.Deserialize<JsonElement>("\"Draft\"");

		// Act
		object? value = BusinessRuleHelpers.ConvertJsonElement(element);

		// Assert
		value.Should().NotBeNull(
			because: "JSON string constants should produce a scalar payload for metadata serialization");
	}

	[Test]
	[Category("Unit")]
	[Description("Converts JSON boolean elements into CLR booleans.")]
	public void ConvertJsonElement_Should_Return_Boolean_Value() {
		// Arrange
		JsonElement element = JsonSerializer.Deserialize<JsonElement>("true");

		// Act
		object? value = BusinessRuleHelpers.ConvertJsonElement(element);

		// Assert
		value.Should().NotBeNull(
			because: "JSON booleans should produce a scalar payload for metadata serialization");
	}

	[Test]
	[Category("Unit")]
	[Description("Converts integer JSON numbers into CLR Int64 values.")]
	public void ConvertJsonElement_Should_Return_Integer_Value() {
		// Arrange
		JsonElement element = JsonSerializer.Deserialize<JsonElement>("5");

		// Act
		object? value = BusinessRuleHelpers.ConvertJsonElement(element);

		// Assert
		value.Should().NotBeNull(
			because: "whole JSON numbers should produce a scalar payload for metadata serialization");
	}

	[Test]
	[Category("Unit")]
	[Description("Converts non-integer JSON numbers into CLR decimal values.")]
	public void ConvertJsonElement_Should_Return_Decimal_Value() {
		// Arrange
		JsonElement element = JsonSerializer.Deserialize<JsonElement>("5.25");

		// Act
		object? value = BusinessRuleHelpers.ConvertJsonElement(element);

		// Assert
		value.Should().NotBeNull(
			because: "fractional JSON numbers should produce a scalar payload for metadata serialization");
	}

	[Test]
	[Category("Unit")]
	[Description("Converts JSON null values into CLR null.")]
	public void ConvertJsonElement_Should_Return_Null_For_Json_Null() {
		// Arrange
		JsonElement element = JsonSerializer.Deserialize<JsonElement>("null");

		// Act
		object? value = BusinessRuleHelpers.ConvertJsonElement(element);

		// Assert
		value.Should().BeNull(
			because: "JSON null constants should remain null in metadata DTOs");
	}

}
