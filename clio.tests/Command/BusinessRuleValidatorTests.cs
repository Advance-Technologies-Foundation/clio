using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.BusinessRules;
using Clio.Command.EntitySchemaDesigner;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
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

	private static BusinessRule CreateRule(
		string logicalOperation = "AND",
		string comparisonType = "equal",
		BusinessRuleExpression? leftExpression = null,
		BusinessRuleExpression? rightExpression = null,
		List<BusinessRuleAction>? actions = null) =>
		new(
			"Rule caption",
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
		Dictionary<string, EntitySchemaColumnDto> result = new(StringComparer.OrdinalIgnoreCase);
		foreach (EntitySchemaColumnDto column in columns) {
			result[column.Name] = column;
		}

		return result;
	}

	private static EntitySchemaColumnDto CreateColumn(string name, int dataValueType, string? referenceSchemaName = null) =>
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
