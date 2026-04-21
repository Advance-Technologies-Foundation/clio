using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command.BusinessRules;
using Clio.Command.EntitySchemaDesigner;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class BusinessRuleMetadataConverterTests {
	[Test]
	[Category("Unit")]
	[Description("Maps OR conditions, attribute and constant expressions, raw action items, and trigger metadata into business-rule metadata DTOs.")]
	public void ToMetadata_Should_Map_Or_NotEqual_Expressions_Actions_And_Triggers() {
		// Arrange
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Owner", 10, "Contact"),
			CreateColumn("Approver", 10, "Contact"),
			CreateColumn("Amount", 6),
			CreateColumn("Status", 1));
		BusinessRule rule = new(
			"  Lock fields when owner changes  ",
			new BusinessRuleConditionGroup(
				"OR",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Owner", null),
						"equal",
						new BusinessRuleExpression("AttributeValue", "Approver", null)),
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Amount", null),
						"not-equal",
						new BusinessRuleExpression("Const", null, Json(5)))
				]),
			[
				new BusinessRuleAction("make-required", [" Owner ", "Amount", "Owner"]),
				new BusinessRuleAction("make-read-only", ["Status"])
			]);

		// Act
		BusinessRuleMetadataDto metadata = BusinessRuleMetadataConverter.ToMetadata(columnMap, rule);

		// Assert
		metadata.Name.Should().StartWith("BusinessRule_",
			because: "converted metadata should generate an internal business-rule name");
		metadata.Caption.Should().Be("Lock fields when owner changes",
			because: "captions should be trimmed before persistence");
		metadata.Cases.Should().ContainSingle(
			because: "the current business-rule converter should emit a single metadata case");
		BusinessRuleCaseMetadataDto @case = metadata.Cases.Single();
		@case.Condition.Should().NotBeNull(
			because: "the metadata case should contain the converted condition group");
		@case.Condition!.LogicalOperation.Should().Be(BusinessRuleConstants.LogicalOr,
			because: "OR groups should map to the business-rule logical OR constant");
		@case.Condition.Conditions.Should().HaveCount(2,
			because: "every source condition should be preserved in metadata");
		@case.Condition.Conditions[0].ComparisonType.Should().Be(BusinessRuleConstants.ComparisonEqual,
			because: "equal comparisons should map to the business-rule equality constant");
		@case.Condition.Conditions[1].ComparisonType.Should().Be(BusinessRuleConstants.ComparisonNotEqual,
			because: "not-equal comparisons should map to the business-rule inequality constant");
		@case.Condition.Conditions[0].LeftExpression.Type.Should().Be("AttributeValue",
			because: "left expressions should remain attribute references in metadata");
		@case.Condition.Conditions[0].LeftExpression.Path.Should().Be("Owner",
			because: "the left attribute path should be preserved");
		@case.Condition.Conditions[0].LeftExpression.DataValueTypeName.Should().Be("Lookup",
			because: "lookup attributes should preserve their runtime data value type");
		@case.Condition.Conditions[0].LeftExpression.ReferenceSchemaName.Should().Be("Contact",
			because: "lookup attributes should preserve the reference schema name");
		@case.Condition.Conditions[0].RightExpression.Type.Should().Be("AttributeValue",
			because: "attribute-to-attribute comparisons should emit an attribute expression on the right side");
		@case.Condition.Conditions[0].RightExpression.Path.Should().Be("Approver",
			because: "the right attribute path should be preserved for attribute comparisons");
		@case.Condition.Conditions[1].RightExpression.Type.Should().Be("Const",
			because: "constant comparisons should emit value expressions on the right side");
		@case.Condition.Conditions[1].RightExpression.DataValueTypeName.Should().Be("Money",
			because: "constant expressions should inherit the left attribute runtime type");
		@case.Condition.Conditions[1].RightExpression.ReferenceSchemaName.Should().BeNullOrEmpty(
			because: "non-lookup constants should not carry a reference schema");
		@case.Condition.Conditions[1].RightExpression.Value.Should().NotBeNull(
			because: "constant comparisons should keep a persisted value payload in metadata");
		@case.Condition.Conditions[1].RightExpression.Value!.ToString().Should().Be("5",
			because: "numeric constants should keep the raw comparison value");
		@case.Actions[0].Items.Should().Be(" Owner ,Amount,Owner",
			because: "action items should be preserved exactly as supplied without trimming or deduplication");
		@case.Actions[1].Items.Should().Be("Status",
			because: "single target actions should keep their single attribute name");
		metadata.Triggers.Should().HaveCount(4,
			because: "the converter should emit change triggers for referenced attributes plus the data-loaded trigger");
		metadata.Triggers.Should().Contain(trigger =>
				trigger.Type == BusinessRuleConstants.ChangeAttributeValueTriggerType && trigger.Name == "Owner",
			because: "attribute comparisons should add a change trigger for the left attribute");
		metadata.Triggers.Should().Contain(trigger =>
				trigger.Type == BusinessRuleConstants.ChangeAttributeValueTriggerType && trigger.Name == "Approver",
			because: "attribute comparisons should add a change trigger for the right attribute when it is also an attribute");
		metadata.Triggers.Should().Contain(trigger =>
				trigger.Type == BusinessRuleConstants.ChangeAttributeValueTriggerType && trigger.Name == "Amount",
			because: "constant comparisons should add a change trigger for the referenced left attribute");
		metadata.Triggers.Should().Contain(trigger =>
				trigger.Type == BusinessRuleConstants.DataLoadedTriggerType && trigger.Name == string.Empty,
			because: "every business rule should also run on DataLoaded");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps lookup constants with the left attribute data value type and reference schema metadata.")]
	public void ToMetadata_Should_Map_Lookup_Constant_Metadata() {
		// Arrange
		const string ownerId = "11111111-1111-1111-1111-111111111111";
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Owner", 10, "Contact"),
			CreateColumn("Status", 1));
		BusinessRule rule = new(
			"Require status when owner is set",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Owner", null),
						"equal",
						new BusinessRuleExpression("Const", null, Json(ownerId)))
				]),
			[
				new BusinessRuleAction("make-required", ["Status"])
			]);

		// Act
		BusinessRuleMetadataDto metadata = BusinessRuleMetadataConverter.ToMetadata(columnMap, rule);

		// Assert
		BusinessRuleExpressionMetadataDto rightExpression = metadata.Cases[0].Condition!.Conditions[0].RightExpression;
		rightExpression.Type.Should().Be("Const",
			because: "lookup comparisons against a constant should emit a constant right expression");
		rightExpression.DataValueTypeName.Should().Be("Lookup",
			because: "lookup constants should inherit the left attribute runtime type");
		rightExpression.ReferenceSchemaName.Should().Be("Contact",
			because: "lookup constants should keep the inferred reference schema name");
		rightExpression.Value.Should().NotBeNull(
			because: "lookup constants should persist their raw comparison value");
		rightExpression.Value!.ToString().Should().Be(ownerId,
			because: "lookup constants should keep the raw GUID string value");
	}

	[Test]
	[Category("Unit")]
	[Description("Serializes constant expressions with dataValueTypeName before value so sequential metadata readers interpret numeric constants correctly.")]
	public void ToMetadata_Should_Serialize_DataValueTypeName_Before_Value_For_And_Equal_Conditions() {
		// Arrange
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Amount", 6));
		BusinessRule rule = new(
			"Readonly amount when threshold is 5",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Amount", null),
						"equal",
						new BusinessRuleExpression("Const", null, Json(5)))
				]),
			[
				new BusinessRuleAction("make-read-only", ["Amount"])
			]);

		// Act
		BusinessRuleMetadataDto metadata = BusinessRuleMetadataConverter.ToMetadata(columnMap, rule);
		string json = JsonSerializer.Serialize(metadata, BusinessRuleConstants.JsonOptions);

		// Assert
		metadata.Cases[0].Condition!.LogicalOperation.Should().Be(BusinessRuleConstants.LogicalAnd,
			because: "AND groups should map to the business-rule logical AND constant");
		metadata.Cases[0].Condition.Conditions[0].ComparisonType.Should().Be(BusinessRuleConstants.ComparisonEqual,
			because: "equal comparisons should map to the business-rule equality constant");
		using JsonDocument document = JsonDocument.Parse(json);
		string rightExpressionJson = document.RootElement
			.GetProperty("cases")[0]
			.GetProperty("condition")
			.GetProperty("conditions")[0]
			.GetProperty("rightExpression")
			.GetRawText();
		rightExpressionJson.IndexOf("\"dataValueTypeName\"", StringComparison.Ordinal).Should().BeGreaterThan(-1,
			because: "constant expressions should serialize their runtime type");
		rightExpressionJson.IndexOf("\"value\"", StringComparison.Ordinal).Should().BeGreaterThan(-1,
			because: "constant expressions should serialize their raw comparison value");
		rightExpressionJson.IndexOf("\"dataValueTypeName\"", StringComparison.Ordinal).Should()
			.BeLessThan(rightExpressionJson.IndexOf("\"value\"", StringComparison.Ordinal),
				because: "sequential business-rule metadata readers need dataValueTypeName before value");
		document.RootElement
			.GetProperty("cases")[0]
			.GetProperty("condition")
			.GetProperty("conditions")[0]
			.GetProperty("rightExpression")
			.GetProperty("dataValueTypeName")
			.GetString()
			.Should()
			.Be("Money", because: "numeric constants should inherit the left attribute data value type");
		document.RootElement
			.GetProperty("cases")[0]
			.GetProperty("condition")
			.GetProperty("conditions")[0]
			.GetProperty("rightExpression")
			.GetProperty("value")
			.GetInt32()
			.Should()
			.Be(5, because: "numeric constants should remain JSON numbers after serialization");
	}

	private static IReadOnlyDictionary<string, EntitySchemaColumnDto> CreateColumnMap(params EntitySchemaColumnDto[] columns) {
		Dictionary<string, EntitySchemaColumnDto> result = new(StringComparer.Ordinal);
		foreach (EntitySchemaColumnDto column in columns) {
			result[column.Name] = column;
		}

		return result;
	}

	private static EntitySchemaColumnDto CreateColumn(string name, int dataValueType, string? referenceSchemaName = null) {
		return new EntitySchemaColumnDto {
			Name = name,
			DataValueType = dataValueType,
			ReferenceSchema = string.IsNullOrWhiteSpace(referenceSchemaName)
				? null!
				: new EntityDesignSchemaDto {
					Name = referenceSchemaName
				}
		};
	}

	private static JsonElement Json(string value) =>
		JsonSerializer.Deserialize<JsonElement>($"\"{value}\"");

	private static JsonElement Json(int value) =>
		JsonSerializer.Deserialize<JsonElement>(value.ToString());
}
