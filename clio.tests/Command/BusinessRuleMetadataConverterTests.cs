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
	[TestCase("is-not-filled-in", BusinessRuleConstants.ComparisonIsNotFilledIn, false)]
	[TestCase("is-filled-in", BusinessRuleConstants.ComparisonIsFilledIn, false)]
	[TestCase("equal", BusinessRuleConstants.ComparisonEqual, true)]
	[TestCase("not-equal", BusinessRuleConstants.ComparisonNotEqual, true)]
	[TestCase("less-than", BusinessRuleConstants.ComparisonLessThan, true)]
	[TestCase("less-than-or-equal", BusinessRuleConstants.ComparisonLessThanOrEqual, true)]
	[TestCase("greater-than", BusinessRuleConstants.ComparisonGreaterThan, true)]
	[TestCase("greater-than-or-equal", BusinessRuleConstants.ComparisonGreaterThanOrEqual, true)]
	[Category("Unit")]
	[Description("Maps every supported wire comparison type to the expected Creatio metadata value and omits rightExpression for unary comparisons.")]
	public void ToMetadata_Should_Map_Supported_Comparison_Types(
		string comparisonType,
		int expectedMetadataValue,
		bool shouldIncludeRightExpression) {
		// Arrange
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("CreatedOn", 7),
			CreateColumn("Status", 1));
		BusinessRule rule = new(
			"Caption",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "CreatedOn", null),
						comparisonType,
						shouldIncludeRightExpression
							? new BusinessRuleExpression("Const", null, Json("2025-01-15T13:45:30Z"))
							: null)
				]),
			[
				new MakeReadOnlyBusinessRuleAction(["Status"])
			]);

		// Act
		BusinessRuleMetadataDto metadata = BusinessRuleMetadataConverter.ToMetadata(columnMap, rule, "UsrTask");

		// Assert
		BusinessRuleConditionMetadataDto condition = metadata.Cases[0].Condition!.Conditions[0];
		condition.ComparisonType.Should().Be(expectedMetadataValue,
			because: "each supported wire comparison should map to the corresponding Creatio business-rule enum value");
		if (shouldIncludeRightExpression) {
			condition.RightExpression.Should().NotBeNull(
				because: "binary comparisons should persist a right expression in metadata");
		} else {
			condition.RightExpression.Should().BeNull(
				because: "unary comparisons should omit the right expression from serialized metadata");
		}
	}

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
				new MakeRequiredBusinessRuleAction([" Owner ", "Amount", "Owner"]),
				new MakeReadOnlyBusinessRuleAction(["Status"])
			]);

		// Act
		BusinessRuleMetadataDto metadata = BusinessRuleMetadataConverter.ToMetadata(columnMap, rule, "UsrTask");

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
				new MakeRequiredBusinessRuleAction(["Status"])
			]);

		// Act
		BusinessRuleMetadataDto metadata = BusinessRuleMetadataConverter.ToMetadata(columnMap, rule, "UsrTask");

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
	[Description("Maps page lookup attribute expressions without reference schema metadata while preserving lookup constants.")]
	public void ToPageMetadata_Should_Omit_Reference_Schema_For_Page_Attribute_Expressions() {
		// Arrange
		const string countryId = "11111111-1111-1111-1111-111111111111";
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["PDS_UsrLookupCountry_qs19ss4"] = new("PDS_UsrLookupCountry_qs19ss4", "Lookup", "Country")
			};
		BusinessRule rule = new(
			"Show country input",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "PDS_UsrLookupCountry_qs19ss4", null),
						"equal",
						new BusinessRuleExpression("Const", null, Json(countryId)))
				]),
			[
				new ShowElementBusinessRuleAction(["Input_0dqt4ly"])
			]);

		// Act
		BusinessRuleMetadataDto metadata = BusinessRuleMetadataConverter.ToPageMetadata(attributeMap, rule);

		// Assert
		BusinessRuleConditionMetadataDto condition = metadata.Cases[0].Condition!.Conditions[0];
		condition.LeftExpression.ReferenceSchemaName.Should().BeNull(
			because: "page AttributeValue metadata should match Creatio page add-on shape and omit referenceSchemaName");
		condition.RightExpression!.ReferenceSchemaName.Should().Be("Country",
			because: "lookup constant metadata still needs the referenced schema for value interpretation");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps set-values constant assignments into BusinessRuleActionSetValues metadata with typed item expressions.")]
	public void ToMetadata_Should_Map_SetValues_Constant_Action_Metadata() {
		// Arrange
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("TextResult", 1),
			CreateColumn("Score", 4),
			CreateColumn("Completed", 12),
			CreateColumn("StartDate", 8),
			CreateColumn("ReminderTime", 9),
			CreateColumn("PlannedOn", 7));
		BusinessRule rule = new(
			"Populate defaults",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Status", null),
						"equal",
						new BusinessRuleExpression("Const", null, Json("Draft")))
				]),
			[
				new SetValuesBusinessRuleAction(
					new List<BusinessRuleSetValueItem> {
						new(
							new BusinessRuleExpression("AttributeValue", "TextResult", null),
							new BusinessRuleExpression("Const", null, Json("Ready"))),
						new(
							new BusinessRuleExpression("AttributeValue", "Score", null),
							new BusinessRuleExpression("Const", null, JsonRaw("42"))),
						new(
							new BusinessRuleExpression("AttributeValue", "Completed", null),
							new BusinessRuleExpression("Const", null, JsonRaw("true"))),
						new(
							new BusinessRuleExpression("AttributeValue", "StartDate", null),
							new BusinessRuleExpression("Const", null, Json("2025-01-15"))),
						new(
							new BusinessRuleExpression("AttributeValue", "ReminderTime", null),
							new BusinessRuleExpression("Const", null, Json("13:45:00+02:00"))),
						new(
							new BusinessRuleExpression("AttributeValue", "PlannedOn", null),
							new BusinessRuleExpression("Const", null, Json("2025-01-15T13:45:00+02:00")))
					})
			]);

		// Act
		BusinessRuleMetadataDto metadata = BusinessRuleMetadataConverter.ToMetadata(columnMap, rule, "UsrTask");

		// Assert
		FieldSelectionBusinessRuleActionMetadataDto action = metadata.Cases[0].Actions.Single();
		action.TypeName.Should().Be(BusinessRuleConstants.BusinessRuleSetValuesElementTypeName,
			because: "set-values actions should persist as the core BusinessRuleActionSetValues type");
		action.Items.Should().BeOfType<List<BusinessRuleSetValueItemMetadataDto>>(
			because: "set-values actions should persist assignment items instead of a comma-separated target list");
		List<BusinessRuleSetValueItemMetadataDto> items = (List<BusinessRuleSetValueItemMetadataDto>)action.Items!;
		JsonElement serializedItems = JsonSerializer.SerializeToElement(items, BusinessRuleConstants.JsonOptions);
		items.Should().HaveCount(6,
			because: "every set-values assignment should become a separate persisted item");
		items[0].TypeName.Should().Be(BusinessRuleConstants.BusinessRuleSetValueItemTypeName,
			because: "each item should use the core BusinessRuleSetValueItem metadata type");
		serializedItems[0].EnumerateObject().Select(property => property.Name).Should()
			.BeEquivalentTo(["typeName", "uId", "enabled", "expression", "value"],
				because: "object-level set-values item metadata should only include the fields consumed by core");
		items[0].Expression.Path.Should().Be("TextResult",
			because: "the target expression should preserve the assigned column path");
		serializedItems[0].GetProperty("expression").EnumerateObject().Select(property => property.Name).Should()
			.BeEquivalentTo(["typeName", "uId", "type", "dataValueTypeName", "path"],
				because: "object-level set-values expressions should rely on the core default root scope");
		items[0].Value.Type.Should().Be("Const",
			because: "constant set-values sources should emit value expressions");
		items[0].Value.DataValueTypeName.Should().Be("Text",
			because: "constant values should inherit the target column type");
		serializedItems[0].GetProperty("value").GetProperty("value").GetString().Should().Be("Ready",
			because: "text constants should persist their string value");
		items[1].Value.DataValueTypeName.Should().Be("Integer",
			because: "numeric constants should inherit the numeric target column type");
		serializedItems[1].GetProperty("value").GetProperty("value").GetInt64().Should().Be(42L,
			because: "integer constants should persist as JSON numbers");
		items[2].Value.DataValueTypeName.Should().Be("Boolean",
			because: "boolean constants should inherit the Boolean target column type");
		serializedItems[2].GetProperty("value").GetProperty("value").GetBoolean().Should().BeTrue(
			because: "boolean constants should persist as JSON booleans");
		items[3].Value.DataValueTypeName.Should().Be("Date",
			because: "Date constants should inherit the Date target column type");
		serializedItems[3].GetProperty("value").GetProperty("value").GetString().Should().Be("2025-01-15T00:00:00Z",
			because: "Date constants should persist as midnight UTC date values");
		items[4].Value.DataValueTypeName.Should().Be("Time",
			because: "Time constants should inherit the Time target column type");
		serializedItems[4].GetProperty("value").GetProperty("value").GetString().Should().Be("0001-01-01T11:45:00Z",
			because: "Time constants should be normalized to UTC on the metadata time anchor date");
		items[5].Value.DataValueTypeName.Should().Be("DateTime",
			because: "DateTime constants should inherit the DateTime target column type");
		serializedItems[5].GetProperty("value").GetProperty("value").GetString().Should().Be("2025-01-15T11:45:00Z",
			because: "DateTime constants should be normalized to UTC before serialization");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps set-values formula assignments into BusinessRuleFormulaExpression metadata and adds triggers for formula source fields.")]
	public void ToMetadata_Should_Map_SetValues_Formula_Action_Metadata() {
		// Arrange
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("EstimatedMinutes", 4),
			CreateColumn("OvertimeMinutes", 4),
			CreateColumn("SpentMinutes", 4));
		BusinessRule rule = new(
			"Calculate spent minutes",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Status", null),
						"equal",
						new BusinessRuleExpression("Const", null, Json("Draft")))
				]),
			[
				new SetValuesBusinessRuleAction(
					new List<BusinessRuleSetValueItem> {
						new(
							new BusinessRuleExpression("AttributeValue", "SpentMinutes", null),
							new BusinessRuleExpression("Formula", expression: "EstimatedMinutes + OvertimeMinutes"))
					})
			]);

		// Act
		BusinessRuleMetadataDto metadata = BusinessRuleMetadataConverter.ToMetadata(columnMap, rule, "UsrTask");

		// Assert
		FieldSelectionBusinessRuleActionMetadataDto action = metadata.Cases[0].Actions.Single();
		List<BusinessRuleSetValueItemMetadataDto> items = (List<BusinessRuleSetValueItemMetadataDto>)action.Items!;
		BusinessRuleExpressionMetadataDto value = items.Single().Value;
		value.TypeName.Should().Be(BusinessRuleConstants.BusinessRuleFormulaExpressionTypeName,
			because: "formula set-values sources should use the core formula expression contract");
		value.Type.Should().Be("Formula",
			because: "the persisted business-rule expression should keep its Formula discriminator");
		value.ParameterMappings.Should().NotBeNull().And.HaveCount(2,
			because: "formula expressions need mappings for record id and field-values context parameters");
		value.ParameterMappings![0].ParameterName.Should().Be("UsrTaskIdParameter",
			because: "the record primary value should be passed through a deterministic id parameter");
		value.ParameterMappings[1].ParameterName.Should().Be("UsrTaskfieldValuesParameter",
			because: "the current field-values context should be passed through the field-values parameter");
		value.ExpressionSchema.Should().NotBeNull(
			because: "formula expressions persist a nested expression schema in BRF2");
		value.ExpressionSchema!.Expression.Should().Be("#UsrTaskRecord.EstimatedMinutes# + #UsrTaskRecord.OvertimeMinutes#",
			because: "agent-friendly direct field references should be translated to PowerFx record references");
		value.ExpressionSchema.ResultDataValueType.Should().Be("Integer",
			because: "the formula result type should inherit the target column data value type");
		value.ExpressionSchema.ExpressionVariables.Should().ContainSingle(
			because: "a direct entity formula should use one record variable");
		value.ExpressionSchema.ExpressionVariables[0].Name.Should().Be("UsrTaskRecord",
			because: "the record variable should be deterministic for the target entity schema");
		value.ExpressionSchema.ExpressionVariables[0].Config!.Value.Should().Be("UsrTask",
			because: "the variable config should reference the target entity schema");
		value.ExpressionSchema.ExpressionVariables[0].Config!.PrimaryValue!.Value.Should().Be("UsrTaskIdParameter",
			because: "the variable primary value should bind to the id parameter");
		value.ExpressionSchema.ExpressionVariables[0].Config!.FieldValues!.Value.Should().Be("UsrTaskfieldValuesParameter",
			because: "runtime formula execution should receive the edited field-values context");
		metadata.Triggers.Should().Contain(trigger =>
				trigger.Type == BusinessRuleConstants.ChangeAttributeValueTriggerType && trigger.Name == "EstimatedMinutes",
			because: "formula source fields should trigger business-rule recalculation");
		metadata.Triggers.Should().Contain(trigger =>
				trigger.Type == BusinessRuleConstants.ChangeAttributeValueTriggerType && trigger.Name == "OvertimeMinutes",
			because: "formula source fields should trigger business-rule recalculation");
		metadata.Triggers.Should().Contain(trigger =>
				trigger.Type == BusinessRuleConstants.DataLoadedTriggerType && trigger.Name == string.Empty,
			because: "formula rules should still run on DataLoaded");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects formula assignments when expression translation cannot resolve a source attribute.")]
	public void ToMetadata_Should_Reject_SetValues_Formula_With_Unknown_Source_Attribute() {
		// Arrange
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("BaseScore", 4),
			CreateColumn("Score", 4));
		BusinessRule rule = CreateFormulaRule("Score", "BaseScore + MissingScore");

		// Act
		Action act = () => BusinessRuleMetadataConverter.ToMetadata(columnMap, rule, "UsrTask");

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Unknown attribute 'MissingScore' in rule.actions[*].items[*].value.expression formula.",
				because: "formula translation should resolve source fields before metadata is persisted");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects formula assignments that do not contain source fields for trigger generation.")]
	public void ToMetadata_Should_Reject_SetValues_Formula_Without_Source_Attributes() {
		// Arrange
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Score", 4));
		BusinessRule rule = CreateFormulaRule("Score", "1 + 2");

		// Act
		Action act = () => BusinessRuleMetadataConverter.ToMetadata(columnMap, rule, "UsrTask");

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Formula for 'Score' must reference at least one entity attribute.",
				because: "formula source fields are needed to build recalculation triggers");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects formula functions because current formula support is limited to simple direct-field expressions.")]
	public void ToMetadata_Should_Reject_SetValues_Formula_Function_Call() {
		// Arrange
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("BaseScore", 4),
			CreateColumn("Score", 4));
		BusinessRule rule = CreateFormulaRule("Score", "If(BaseScore > 0, BaseScore, 0)");

		// Act
		Action act = () => BusinessRuleMetadataConverter.ToMetadata(columnMap, rule, "UsrTask");

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Formula functions are not supported in rule.actions[*].items[*].value.expression. Use a simple direct-field expression instead of 'If(...)'.",
				because: "PowerFx functions are outside the current simple formula scope");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects formula assignments with operators outside the local arithmetic expression whitelist.")]
	public void ToMetadata_Should_Reject_SetValues_Formula_With_Unsupported_Operator() {
		// Arrange
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("BaseScore", 4),
			CreateColumn("Score", 4));
		BusinessRule rule = CreateFormulaRule("Score", "BaseScore > 0");

		// Act
		Action act = () => BusinessRuleMetadataConverter.ToMetadata(columnMap, rule, "UsrTask");

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Formula expression supports only direct entity fields, numbers, arithmetic operators (+, -, *, /), dots, parentheses, and whitespace.",
				because: "local validation should keep formula scope aligned with the expression designer arithmetic whitelist");
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
				new MakeReadOnlyBusinessRuleAction(["Amount"])
			]);

		// Act
		BusinessRuleMetadataDto metadata = BusinessRuleMetadataConverter.ToMetadata(columnMap, rule, "UsrTask");
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

	[Test]
	[Category("Unit")]
	[Description("Normalizes date/time constants into typed metadata values before JSON serialization.")]
	public void ToMetadata_Should_Normalize_Date_Time_Constant_Metadata() {
		// Arrange
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("CreatedOn", 7),
			CreateColumn("Status", 1));
		BusinessRule rule = new(
			"Readonly status before cutoff",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "CreatedOn", null),
						"less-than",
						new BusinessRuleExpression("Const", null, Json("2025-01-15T13:45:30+02:00")))
				]),
			[
				new MakeReadOnlyBusinessRuleAction(["Status"])
			]);

		// Act
		BusinessRuleMetadataDto metadata = BusinessRuleMetadataConverter.ToMetadata(columnMap, rule, "UsrTask");
		BusinessRuleExpressionMetadataDto rightExpression = metadata.Cases[0].Condition!.Conditions[0].RightExpression!;
		string json = JsonSerializer.Serialize(metadata, BusinessRuleConstants.JsonOptions);

		// Assert
		rightExpression.Value.Should().BeOfType<DateTime>(
			because: "date/time constants should be normalized to typed DateTime values before metadata serialization");
		((DateTime)rightExpression.Value!).Should().Be(new DateTime(2025, 1, 15, 11, 45, 30, DateTimeKind.Utc),
			because: "date-time constants with offsets should be normalized to UTC");
		using JsonDocument document = JsonDocument.Parse(json);
		document.RootElement
			.GetProperty("cases")[0]
			.GetProperty("condition")
			.GetProperty("conditions")[0]
			.GetProperty("rightExpression")
			.GetProperty("value")
			.GetString()
			.Should()
			.Be("2025-01-15T11:45:30Z",
				because: "typed date/time constants should serialize back to stable UTC JSON strings");
	}

	[Test]
	[Category("Unit")]
	[Description("Normalizes timezone-aware time constants into UTC time-of-day metadata values before JSON serialization.")]
	public void ToMetadata_Should_Normalize_Time_Constant_Metadata() {
		// Arrange
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("ReminderTime", 9),
			CreateColumn("Status", 1));
		BusinessRule rule = new(
			"Readonly status after local noon",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "ReminderTime", null),
						"greater-than",
						new BusinessRuleExpression("Const", null, Json("12:00:00+02:00")))
				]),
			[
				new MakeReadOnlyBusinessRuleAction(["Status"])
			]);

		// Act
		BusinessRuleMetadataDto metadata = BusinessRuleMetadataConverter.ToMetadata(columnMap, rule, "UsrTask");
		BusinessRuleExpressionMetadataDto rightExpression = metadata.Cases[0].Condition!.Conditions[0].RightExpression!;
		string json = JsonSerializer.Serialize(metadata, BusinessRuleConstants.JsonOptions);

		// Assert
		rightExpression.Value.Should().BeOfType<DateTime>(
			because: "timezone-aware time constants should be normalized before metadata serialization");
		((DateTime)rightExpression.Value!).Should().Be(new DateTime(1, 1, 1, 10, 0, 0, DateTimeKind.Utc),
			because: "a +02:00 time constant should be converted to the equivalent UTC time-of-day");
		using JsonDocument document = JsonDocument.Parse(json);
		document.RootElement
			.GetProperty("cases")[0]
			.GetProperty("condition")
			.GetProperty("conditions")[0]
			.GetProperty("rightExpression")
			.GetProperty("value")
			.GetString()
			.Should()
			.Be("0001-01-01T10:00:00Z",
				because: "normalized time constants should serialize as stable UTC strings in add-on metadata");
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

	private static BusinessRule CreateFormulaRule(string targetPath, string formula) =>
		new(
			"Formula rule",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Status", null),
						"equal",
						new BusinessRuleExpression("Const", null, Json("Draft")))
				]),
			[
				new SetValuesBusinessRuleAction(
					new List<BusinessRuleSetValueItem> {
						new(
							new BusinessRuleExpression("AttributeValue", targetPath, null),
							new BusinessRuleExpression("Formula", expression: formula))
					})
			]);

	private static JsonElement Json(string value) =>
		JsonSerializer.Deserialize<JsonElement>($"\"{value}\"");

	private static JsonElement Json(int value) =>
		JsonSerializer.Deserialize<JsonElement>(value.ToString());

	private static JsonElement JsonRaw(string value) =>
		JsonSerializer.Deserialize<JsonElement>(value);
}

