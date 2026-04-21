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
public sealed class BusinessRuleValidatorTests {
	private static readonly object[] TextTypeCases = [
		new object[] { 1, "Text" },
		new object[] { 24, "SecureText" },
		new object[] { 27, "ShortText" },
		new object[] { 28, "MediumText" },
		new object[] { 29, "MaxSizeText" },
		new object[] { 30, "LongText" },
		new object[] { 42, "PhoneText" },
		new object[] { 43, "RichText" },
		new object[] { 44, "WebText" },
		new object[] { 45, "EmailText" }
	];

	private static readonly object[] NumberTypeCases = [
		new object[] { 4, "Integer" },
		new object[] { 5, "Float" },
		new object[] { 6, "Money" },
		new object[] { 47, "Float0" }
	];

	[Test]
	[Category("Unit")]
	[Description("Rejects unsupported logical operations in the top-level business-rule condition group.")]
	public void Validate_Should_Reject_Unsupported_Logical_Operation() {
		// Arrange
		BusinessRule rule = CreateRule(
			logicalOperation: "XOR");
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Unsupported rule.condition.logicalOperation 'XOR'. Use AND or OR.",
				because: "the validator should only accept the logical operations supported by the metadata serializer");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects unsupported comparison types in condition expressions.")]
	public void Validate_Should_Reject_Unsupported_Comparison_Type() {
		// Arrange
		BusinessRule rule = CreateRule(
			comparisonType: "greater-than");
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Unsupported rule.condition.conditions[*].comparisonType 'greater-than'. Supported values: equal, not-equal.",
				because: "the current business-rule flow only supports equality-based comparisons");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts OR groups that use not-equal comparisons when the referenced attributes and constant values are valid.")]
	public void Validate_Should_Accept_Or_Group_With_Not_Equal_Comparison() {
		// Arrange
		BusinessRule rule = CreateRule(
			logicalOperation: "OR",
			comparisonType: "not-equal");
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().NotThrow(
			because: "valid OR groups and not-equal comparisons should pass validator ownership checks");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects attribute-to-attribute comparisons when the left and right columns have different data value types.")]
	public void Validate_Should_Reject_Different_Left_And_Right_Attribute_Types() {
		// Arrange
		BusinessRule rule = CreateRule(
			rightExpression: new BusinessRuleExpression("AttributeValue", "Amount", null));
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Amount", 6),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.condition.conditions[*] compares left attribute 'Status' (Text) to right attribute 'Amount' (Money). Both attributes must have the same data value type.",
				because: "attribute-to-attribute comparisons should only compare like-typed entity columns");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts attribute-to-attribute comparisons when both entity columns have the same data value type.")]
	public void Validate_Should_Accept_Attribute_To_Attribute_Comparison_With_Matching_Types() {
		// Arrange
		BusinessRule rule = CreateRule(
			leftExpression: new BusinessRuleExpression("AttributeValue", "Owner", null),
			rightExpression: new BusinessRuleExpression("AttributeValue", "Approver", null));
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Owner", 10, "Contact"),
			CreateColumn("Approver", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().NotThrow(
			because: "attribute comparisons with matching data value types should pass validation");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects non-boolean constants when the left attribute uses the Boolean data value type.")]
	public void Validate_Should_Reject_Non_Boolean_Constant_For_Boolean_Attribute() {
		// Arrange
		BusinessRule rule = CreateRule(
			leftExpression: new BusinessRuleExpression("AttributeValue", "Completed", null),
			rightExpression: new BusinessRuleExpression("Const", null, Json("\"true\"")));
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Completed", 12),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.condition.conditions[*].rightExpression.value must be a JSON boolean when the left attribute is Boolean.",
				because: "boolean comparisons should reject string payloads that only look like booleans");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts boolean constants when the left attribute uses the Boolean data value type.")]
	public void Validate_Should_Accept_Boolean_Constant_For_Boolean_Attribute() {
		// Arrange
		BusinessRule rule = CreateRule(
			leftExpression: new BusinessRuleExpression("AttributeValue", "Completed", null),
			rightExpression: new BusinessRuleExpression("Const", null, Json("true")));
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Completed", 12),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().NotThrow(
			because: "valid boolean constants should pass validation for boolean attributes");
	}

	[TestCaseSource(nameof(TextTypeCases))]
	[Category("Unit")]
	[Description("Rejects non-string constants for every supported text-like Creatio data value type.")]
	public void Validate_Should_Reject_Non_String_Constant_For_Text_Attribute(int dataValueType, string dataValueTypeName) {
		// Arrange
		BusinessRule rule = CreateRule(
			leftExpression: new BusinessRuleExpression("AttributeValue", "TextColumn", null),
			rightExpression: new BusinessRuleExpression("Const", null, Json("5")));
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("TextColumn", dataValueType),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.condition.conditions[*].rightExpression.value must be a JSON string when the left attribute is a text type.",
				because: $"{dataValueTypeName} constants should be represented as JSON strings in business-rule metadata");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts string constants when the left attribute uses a supported text data value type.")]
	public void Validate_Should_Accept_String_Constant_For_Text_Attribute() {
		// Arrange
		BusinessRule rule = CreateRule(
			leftExpression: new BusinessRuleExpression("AttributeValue", "TextColumn", null),
			rightExpression: new BusinessRuleExpression("Const", null, Json("\"Draft\"")));
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("TextColumn", 43),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().NotThrow(
			because: "supported text attributes should accept JSON string constants");
	}

	[TestCaseSource(nameof(NumberTypeCases))]
	[Category("Unit")]
	[Description("Rejects non-numeric constants for every supported numeric Creatio data value type.")]
	public void Validate_Should_Reject_Non_Number_Constant_For_Number_Attribute(int dataValueType, string dataValueTypeName) {
		// Arrange
		BusinessRule rule = CreateRule(
			leftExpression: new BusinessRuleExpression("AttributeValue", "NumberColumn", null),
			rightExpression: new BusinessRuleExpression("Const", null, Json("\"5\"")));
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("NumberColumn", dataValueType),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.condition.conditions[*].rightExpression.value must be a JSON number when the left attribute is a numeric type.",
				because: $"{dataValueTypeName} constants should stay numeric in the serialized business-rule payload");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts numeric constants when the left attribute uses a supported numeric data value type.")]
	public void Validate_Should_Accept_Number_Constant_For_Number_Attribute() {
		// Arrange
		BusinessRule rule = CreateRule(
			leftExpression: new BusinessRuleExpression("AttributeValue", "NumberColumn", null),
			rightExpression: new BusinessRuleExpression("Const", null, Json("5")));
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("NumberColumn", 6),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().NotThrow(
			because: "supported numeric attributes should accept JSON numeric constants");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects lookup constants that are not GUID strings.")]
	public void Validate_Should_Reject_Non_Guid_Constant_For_Lookup_Attribute() {
		// Arrange
		BusinessRule rule = CreateRule(
			leftExpression: new BusinessRuleExpression("AttributeValue", "Owner", null),
			rightExpression: new BusinessRuleExpression("Const", null, Json("\"not-a-guid\"")));
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.condition.conditions[*].rightExpression.value must be a GUID string when the left attribute is a Lookup.",
				because: "lookup comparisons should persist raw lookup ids rather than captions or arbitrary text");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts GUID string constants when the left attribute uses the Lookup data value type.")]
	public void Validate_Should_Accept_Guid_Constant_For_Lookup_Attribute() {
		// Arrange
		BusinessRule rule = CreateRule(
			leftExpression: new BusinessRuleExpression("AttributeValue", "Owner", null),
			rightExpression: new BusinessRuleExpression("Const", null, Json("\"11111111-1111-1111-1111-111111111111\"")));
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().NotThrow(
			because: "lookup attributes should accept raw GUID string constants");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects Guid constants that are not valid GUID strings.")]
	public void Validate_Should_Reject_Non_Guid_Constant_For_Guid_Attribute() {
		// Arrange
		BusinessRule rule = CreateRule(
			leftExpression: new BusinessRuleExpression("AttributeValue", "RecordId", null),
			rightExpression: new BusinessRuleExpression("Const", null, Json("\"not-a-guid\"")));
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("RecordId", 0),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.condition.conditions[*].rightExpression.value must be a GUID string when the left attribute is Guid.",
				because: "Guid columns should only compare against actual Guid payloads");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects comparisons whose left attribute has an unknown Creatio data value type id.")]
	public void Validate_Should_Reject_Unknown_Left_Data_Value_Type() {
		// Arrange
		BusinessRule rule = CreateRule();
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 999),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("Unsupported entity schema dataValueType '999'.",
				because: "validation should fail fast when the entity schema exposes an unsupported data value type");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects comparisons whose left attribute omits the Creatio data value type id.")]
	public void Validate_Should_Reject_Missing_Left_Data_Value_Type() {
		// Arrange
		BusinessRule rule = CreateRule();
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", null),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("Entity schema column dataValueType is required.",
				because: "validation should fail fast when the entity schema omits a data value type");
	}

	[TestCase("MissingStatus", "Const", null, "Unknown attribute 'MissingStatus' in rule.condition.conditions[*].leftExpression.path.")]
	[TestCase("Status", "AttributeValue", "MissingOwner", "Unknown attribute 'MissingOwner' in rule.condition.conditions[*].rightExpression.path.")]
	[Category("Unit")]
	[Description("Rejects condition attributes that are missing from the target entity schema for either side of the comparison.")]
	public void Validate_Should_Reject_Unknown_Condition_Attributes(
		string leftPath,
		string rightType,
		string? rightPath,
		string expectedMessage) {
		// Arrange
		BusinessRule rule = CreateRule(
			leftExpression: new BusinessRuleExpression("AttributeValue", leftPath, null),
			rightExpression: string.Equals(rightType, "AttributeValue", StringComparison.Ordinal)
				? new BusinessRuleExpression("AttributeValue", rightPath, null)
				: new BusinessRuleExpression("Const", null, Json("\"Draft\"")));
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage(expectedMessage,
				because: "both sides of the condition should resolve to real entity schema columns before metadata is generated");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects missing business-rule captions before evaluating condition or action contents.")]
	public void Validate_Should_Reject_Missing_Caption() {
		// Arrange
		BusinessRule rule = new(
			"",
			new BusinessRuleConditionGroup("AND", [
				new BusinessRuleCondition(
					new BusinessRuleExpression("AttributeValue", "Status", null),
					"equal",
					new BusinessRuleExpression("Const", null, Json("\"Draft\"")))
			]),
			[
				new BusinessRuleAction("make-required", ["Owner"])
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.caption is required.",
				because: "the validator should reject empty captions before doing deeper validation work");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects missing condition groups before evaluating rule expressions.")]
	public void Validate_Should_Reject_Missing_Condition() {
		// Arrange
		BusinessRule rule = new(
			"Rule caption",
			null!,
			[
				new BusinessRuleAction("make-required", ["Owner"])
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.condition is required.",
				because: "a business rule cannot be validated without a top-level condition group");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects rules that do not declare any actions.")]
	public void Validate_Should_Reject_Empty_Actions() {
		// Arrange
		BusinessRule rule = new(
			"Rule caption",
			new BusinessRuleConditionGroup("AND", [
				new BusinessRuleCondition(
					new BusinessRuleExpression("AttributeValue", "Status", null),
					"equal",
					new BusinessRuleExpression("Const", null, Json("\"Draft\"")))
			]),
			[]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.actions must contain at least one action.",
				because: "a business rule without actions does not have any effect to validate");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects condition groups that do not contain any child conditions.")]
	public void Validate_Should_Reject_Empty_Condition_List() {
		// Arrange
		BusinessRule rule = new(
			"Rule caption",
			new BusinessRuleConditionGroup("AND", []),
			[
				new BusinessRuleAction("make-required", ["Owner"])
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.condition.conditions must contain at least one condition.",
				because: "a business rule needs at least one leaf condition before it can be evaluated");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects conditions that omit the right expression payload.")]
	public void Validate_Should_Reject_Missing_Right_Expression() {
		// Arrange
		BusinessRule rule = new(
			"Rule caption",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Status", null),
						"equal",
						null!)
				]),
			[
				new BusinessRuleAction("make-required", ["Owner"])
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.condition.conditions[*].rightExpression is required.",
				because: "the validator should require an explicit right-hand operand");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects unsupported business-rule action types before action metadata is generated.")]
	public void Validate_Should_Reject_Unsupported_Action_Type() {
		// Arrange
		BusinessRule rule = CreateRule(
			actions: [
				new BusinessRuleAction("set-visible", ["Owner"])
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Unsupported rule.actions[*].type 'set-visible'. Supported values: make-editable, make-read-only, make-required, make-optional.",
				because: "the current business-rule action subset is intentionally limited to field state changes");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects action entries that do not declare any target attributes.")]
	public void Validate_Should_Reject_Missing_Action_Items() {
		// Arrange
		BusinessRule rule = CreateRule(
			actions: [
				new BusinessRuleAction("make-required", [])
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.actions[*].items must contain at least one attribute.",
				because: "actions without target attributes cannot be applied to the entity schema");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects action items that reference unknown entity schema attributes.")]
	public void Validate_Should_Reject_Unknown_Action_Target() {
		// Arrange
		BusinessRule rule = CreateRule(
			actions: [
				new BusinessRuleAction("make-required", ["MissingOwner"])
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Unknown attribute 'MissingOwner' in rule.actions[*].items.",
				because: "action targets should resolve to real entity schema columns");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects left expressions that are not attribute references.")]
	public void Validate_Should_Reject_Unsupported_Left_Expression_Type() {
		// Arrange
		BusinessRule rule = CreateRule(
			leftExpression: new BusinessRuleExpression("Const", null, Json("\"Draft\"")));
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.condition.conditions[*].leftExpression.type must be 'AttributeValue'.",
				because: "the left side of a business-rule condition must always reference an entity attribute");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects right expressions that are neither attribute references nor constants.")]
	public void Validate_Should_Reject_Unsupported_Right_Expression_Type() {
		// Arrange
		BusinessRule rule = CreateRule(
			rightExpression: new BusinessRuleExpression("Unsupported", null, Json("\"Draft\"")));
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.condition.conditions[*].rightExpression.type must be 'AttributeValue' or 'Const'.",
				because: "the validator should only allow the supported right-hand operand kinds");
	}

	private static BusinessRule CreateRule(
		string caption = "Rule caption",
		string logicalOperation = "AND",
		string comparisonType = "equal",
		BusinessRuleExpression? leftExpression = null,
		BusinessRuleExpression? rightExpression = null,
		List<BusinessRuleAction>? actions = null) =>
		new(
			caption,
			new BusinessRuleConditionGroup(
				logicalOperation,
				[
					new BusinessRuleCondition(
						leftExpression ?? new BusinessRuleExpression("AttributeValue", "Status", null),
						comparisonType,
						rightExpression ?? new BusinessRuleExpression("Const", null, Json("\"Draft\"")))
				]),
			actions ?? [
				new BusinessRuleAction("make-required", ["Owner"])
			]);

	private static IReadOnlyDictionary<string, EntitySchemaColumnDto> CreateColumnMap(params EntitySchemaColumnDto[] columns) {
		Dictionary<string, EntitySchemaColumnDto> result = new(StringComparer.Ordinal);
		foreach (EntitySchemaColumnDto column in columns) {
			result[column.Name] = column;
		}

		return result;
	}

	private static EntitySchemaColumnDto CreateColumn(string name, int? dataValueType, string? referenceSchemaName = null) =>
		new() {
			Name = name,
			DataValueType = dataValueType,
			ReferenceSchema = string.IsNullOrWhiteSpace(referenceSchemaName)
				? null!
				: new EntityDesignSchemaDto {
					Name = referenceSchemaName
				}
		};

	private static JsonElement Json(string json) =>
		JsonSerializer.Deserialize<JsonElement>(json);
}
