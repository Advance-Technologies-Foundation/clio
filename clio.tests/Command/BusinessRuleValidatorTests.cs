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
public sealed class BusinessRuleValidatorTests {
	private static readonly object[] UnarySupportedTypeCases = [
		new object[] { "is-filled-in", 1, "TextColumn", null },
		new object[] { "is-filled-in", 10, "Owner", "Contact" },
		new object[] { "is-filled-in", 12, "Completed", null },
		new object[] { "is-filled-in", 6, "Amount", null },
		new object[] { "is-filled-in", 0, "RecordId", null },
		new object[] { "is-filled-in", 8, "StartDate", null },
		new object[] { "is-filled-in", 7, "CreatedOn", null },
		new object[] { "is-filled-in", 9, "ReminderTime", null },
		new object[] { "is-not-filled-in", 1, "TextColumn", null },
		new object[] { "is-not-filled-in", 10, "Owner", "Contact" },
		new object[] { "is-not-filled-in", 12, "Completed", null },
		new object[] { "is-not-filled-in", 6, "Amount", null },
		new object[] { "is-not-filled-in", 0, "RecordId", null },
		new object[] { "is-not-filled-in", 8, "StartDate", null },
		new object[] { "is-not-filled-in", 7, "CreatedOn", null },
		new object[] { "is-not-filled-in", 9, "ReminderTime", null }
	];

	private static readonly object[] TextTypeCases = [
		new object[] { 1, "Text" },
		new object[] { 24, "SecureText" },
		new object[] { 27, "ShortText" },
		new object[] { 28, "MediumText" },
		new object[] { 29, "MaxSizeText" },
		new object[] { 30, "LongText" },
		new object[] { 42, "PhoneText" },
		new object[] { 44, "WebText" },
		new object[] { 45, "EmailText" }
	];

	private static readonly object[] EqualityUnsupportedTypeCases = [
		new object[] { "equal", 43, "RichText" },
		new object[] { "not-equal", 43, "RichText" },
		new object[] { "equal", 14, "Image" },
		new object[] { "not-equal", 14, "Image" }
	];

	private static readonly object[] NumberTypeCases = [
		new object[] { 4, "Integer" },
		new object[] { 5, "Float" },
		new object[] { 6, "Money" },
		new object[] { 47, "Float0" }
	];

	private static readonly object[] DateTimeTypeCases = [
		new object[] { 8, "StartDate", "2025-01-15" },
		new object[] { 7, "CreatedOn", "2025-01-15T13:45:00+02:00" },
		new object[] { 9, "ReminderTime", "13:45:00+02:00" }
	];

	private static readonly object[] SetValuesConstantCases = [
		new object[] { 1, "TextResult", "\"Ready\"" },
		new object[] { 4, "Score", "42" },
		new object[] { 12, "Completed", "true" },
		new object[] { 8, "StartDate", "\"2025-01-15\"" },
		new object[] { 7, "PlannedOn", "\"2025-01-15T13:45:00+02:00\"" },
		new object[] { 9, "ReminderTime", "\"13:45:00+02:00\"" }
	];

	[Test]
	[Category("Unit")]
	[Description("Deserializes shared page show and hide action discriminators into business-rule action models.")]
	[TestCase("hide-element", nameof(HideElementBusinessRuleAction))]
	[TestCase("show-element", nameof(ShowElementBusinessRuleAction))]
	public void BusinessRule_Should_Deserialize_Page_Action_Discriminators(
		string actionType,
		string expectedActionTypeName) {
		// Arrange
		string payload = $$"""
		{
		  "caption": "Toggle page element",
		  "condition": {
		    "logicalOperation": "AND",
		    "conditions": [
		      {
		        "leftExpression": {
		          "type": "AttributeValue",
		          "path": "PDS_Name"
		        },
		        "comparisonType": "is-filled-in"
		      }
		    ]
		  },
		  "actions": [
		    {
		      "type": "{{actionType}}",
		      "items": [ "NameInput" ]
		    }
		  ]
		}
		""";

		// Act
		BusinessRule? result = JsonSerializer.Deserialize<BusinessRule>(payload);

		// Assert
		result.Should().NotBeNull(
			because: "page business-rule actions should be supported by the shared model discriminator map");
		BusinessRuleAction action = result!.Actions.Single();
		action.GetType().Name.Should().Be(expectedActionTypeName,
			because: "the shared model should materialize the page action type selected by the discriminator");
		action.FieldSelectionItems.Should().Equal(["NameInput"],
			because: "page action item names should survive shared BusinessRule deserialization");
	}

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
			comparisonType: "contains");
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Unsupported rule.condition.conditions[*].comparisonType 'contains'. Supported values: equal, not-equal, is-filled-in, is-not-filled-in, greater-than, greater-than-or-equal, less-than, less-than-or-equal.",
				because: "the validator should reject comparisons outside the supported object business-rule subset");
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

	[TestCaseSource(nameof(UnarySupportedTypeCases))]
	[Category("Unit")]
	[Description("Accepts unary filled-state comparisons for representative text lookup boolean numeric guid and date/time left attributes.")]
	public void Validate_Should_Accept_Unary_Comparison_For_Representative_Left_Types(
		string comparisonType,
		int dataValueType,
		string leftPath,
		string? referenceSchemaName) {
		// Arrange
		BusinessRule rule = CreateRule(
			comparisonType: comparisonType,
			leftExpression: new BusinessRuleExpression("AttributeValue", leftPath, null),
			omitRightExpression: true);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn(leftPath, dataValueType, referenceSchemaName),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().NotThrow(
			because: "filled-in and not-filled-in comparisons should be valid for every supported left-side data family");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects unary filled-state comparisons when a right expression is supplied.")]
	public void Validate_Should_Reject_Right_Expression_For_Unary_Comparison() {
		// Arrange
		BusinessRule rule = CreateRule(
			comparisonType: "is-filled-in",
			leftExpression: new BusinessRuleExpression("AttributeValue", "Status", null),
			rightExpression: new BusinessRuleExpression("Const", null, Json("\"Draft\"")));
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.condition.conditions[*].rightExpression must be omitted when comparisonType is 'is-filled-in'.",
				because: "unary comparisons do not accept a right-hand operand");
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
			CreateColumn("TextColumn", 28),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().NotThrow(
			because: "supported text attributes should accept JSON string constants");
	}

	[TestCaseSource(nameof(EqualityUnsupportedTypeCases))]
	[Category("Unit")]
	[Description("Rejects equal and not-equal comparisons for RichText and Image left attributes.")]
	public void Validate_Should_Reject_Equality_Comparison_For_Large_Attribute_Types(
		string comparisonType,
		int dataValueType,
		string expectedTypeName) {
		// Arrange
		BusinessRule rule = CreateRule(
			comparisonType: comparisonType,
			leftExpression: new BusinessRuleExpression("AttributeValue", "UnsupportedCol", null),
			rightExpression: new BusinessRuleExpression("AttributeValue", "OtherUnsupportedCol", null));
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("UnsupportedCol", dataValueType),
			CreateColumn("OtherUnsupportedCol", dataValueType),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage($"rule.condition.conditions[*].comparisonType '{comparisonType}' is not supported for left attribute 'UnsupportedCol' with type {expectedTypeName}. RichText and Image attributes do not support equal or not-equal business-rule conditions.",
				because: "the Creatio business-rule designer blocks equality comparisons for large attribute value types");
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
	[Description("Accepts relational comparisons with numeric constants when the left attribute is numeric.")]
	public void Validate_Should_Accept_Relational_Number_Constant() {
		// Arrange
		BusinessRule rule = CreateRule(
			comparisonType: "greater-than",
			leftExpression: new BusinessRuleExpression("AttributeValue", "Amount", null),
			rightExpression: new BusinessRuleExpression("Const", null, Json("5")));
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Amount", 6),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().NotThrow(
			because: "numeric relational comparisons should allow numeric constants");
	}

	[TestCase("1e100")]
	[Category("Unit")]
	[Description("Rejects numeric constants that cannot be represented as Int64 or Decimal.")]
	public void Validate_Should_Reject_Numeric_Constant_Outside_Supported_Range(string numericJson) {
		// Arrange
		BusinessRule rule = CreateRule(
			leftExpression: new BusinessRuleExpression("AttributeValue", "Amount", null),
			rightExpression: new BusinessRuleExpression("Const", null, Json(numericJson)));
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Amount", 6),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.condition.conditions[*].rightExpression.value must be a JSON number representable as Int64 or Decimal when the left attribute is a numeric type.",
				because: "numeric constants outside the supported CLR range should be rejected before metadata serialization");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts relational comparisons between numeric attributes of the same data value type.")]
	public void Validate_Should_Accept_Relational_Number_Attribute_Comparison() {
		// Arrange
		BusinessRule rule = CreateRule(
			comparisonType: "less-than-or-equal",
			leftExpression: new BusinessRuleExpression("AttributeValue", "Amount", null),
			rightExpression: new BusinessRuleExpression("AttributeValue", "Budget", null));
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Amount", 6),
			CreateColumn("Budget", 6),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().NotThrow(
			because: "relational attribute comparisons should be allowed when both numeric operands have the same type");
	}

	[TestCaseSource(nameof(DateTimeTypeCases))]
	[Category("Unit")]
	[Description("Accepts relational comparisons with date/time constants for Date DateTime and Time attributes.")]
	public void Validate_Should_Accept_Relational_Date_Time_Constant(
		int dataValueType,
		string leftPath,
		string constantValue) {
		// Arrange
		BusinessRule rule = CreateRule(
			comparisonType: "less-than",
			leftExpression: new BusinessRuleExpression("AttributeValue", leftPath, null),
			rightExpression: new BusinessRuleExpression("Const", null, Json($"\"{constantValue}\"")));
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn(leftPath, dataValueType),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().NotThrow(
			because: "date/time relational comparisons should allow valid string constants for Date DateTime and Time");
	}

	[TestCase(8, "StartDate", "DueDate")]
	[TestCase(7, "CreatedOn", "ModifiedOn")]
	[TestCase(9, "ReminderTime", "EndTime")]
	[Category("Unit")]
	[Description("Accepts relational comparisons between date/time attributes when both operands use the same date/time data value type.")]
	public void Validate_Should_Accept_Relational_Date_Time_Attribute_Comparison(
		int dataValueType,
		string leftPath,
		string rightPath) {
		// Arrange
		BusinessRule rule = CreateRule(
			comparisonType: "greater-than-or-equal",
			leftExpression: new BusinessRuleExpression("AttributeValue", leftPath, null),
			rightExpression: new BusinessRuleExpression("AttributeValue", rightPath, null));
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn(leftPath, dataValueType),
			CreateColumn(rightPath, dataValueType),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().NotThrow(
			because: "date/time relational comparisons should allow matching Date DateTime and Time attribute pairs");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects relational comparisons when the left attribute is not numeric or date/time.")]
	public void Validate_Should_Reject_Relational_Comparison_For_Unsupported_Left_Type() {
		// Arrange
		BusinessRule rule = CreateRule(
			comparisonType: "greater-than",
			leftExpression: new BusinessRuleExpression("AttributeValue", "Status", null),
			rightExpression: new BusinessRuleExpression("Const", null, Json("\"Draft\"")));
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.condition.conditions[*].comparisonType 'greater-than' is only supported for numeric and date/time left attributes. Left attribute 'Status' has type Text.",
				because: "relational operators should be limited to numeric and date/time columns");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects non-string date/time constants for DateTime attributes.")]
	public void Validate_Should_Reject_Invalid_Date_Time_Constant() {
		// Arrange
		BusinessRule rule = CreateRule(
			comparisonType: "greater-than",
			leftExpression: new BusinessRuleExpression("AttributeValue", "CreatedOn", null),
			rightExpression: new BusinessRuleExpression("Const", null, Json("5")));
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("CreatedOn", 7),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.condition.conditions[*].rightExpression.value must be a JSON string in ISO 8601 date-time format with a timezone suffix ('Z' or '+/-HH:mm') when the left attribute is DateTime.",
				because: "date/time comparisons should reject non-string constants before metadata conversion");
	}

	[TestCase("CreatedOn", 7, "2025-01-15T13:45:00", "rule.condition.conditions[*].rightExpression.value must be a JSON string in ISO 8601 date-time format with a timezone suffix ('Z' or '+/-HH:mm') when the left attribute is DateTime.")]
	[TestCase("ReminderTime", 9, "13:45:00", "rule.condition.conditions[*].rightExpression.value must be a JSON string in ISO 8601 time format with a timezone suffix ('Z' or '+/-HH:mm') when the left attribute is Time.")]
	[Category("Unit")]
	[Description("Rejects timezone-less date/time constants when DateTime or Time comparisons require explicit timezone semantics.")]
	public void Validate_Should_Reject_Date_Time_Constant_Without_Explicit_Timezone(
		string leftPath,
		int dataValueType,
		string constantValue,
		string expectedMessage) {
		// Arrange
		BusinessRule rule = CreateRule(
			comparisonType: "greater-than",
			leftExpression: new BusinessRuleExpression("AttributeValue", leftPath, null),
			rightExpression: new BusinessRuleExpression("Const", null, Json($"\"{constantValue}\"")));
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn(leftPath, dataValueType),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage(expectedMessage,
				because: "DateTime and Time comparisons should require an explicit timezone suffix");
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
				new MakeRequiredBusinessRuleAction(["Owner"])
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
				new MakeRequiredBusinessRuleAction(["Owner"])
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
	[Description("Rejects null action entries instead of allowing malformed payloads to be applied partially.")]
	public void Validate_Should_Reject_Null_Action_Entry() {
		// Arrange
		BusinessRule rule = CreateRule(
			actions: [null!]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.actions[*].type is required.",
				because: "null action entries should fail validation before any partial mutation can be saved");
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
				new MakeRequiredBusinessRuleAction(["Owner"])
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
	[Description("Accepts apply-filter rules with an empty condition group when lookup paths resolve to matching types.")]
	public void Validate_Should_Accept_ApplyFilter_With_Empty_Condition_Group() {
		// Arrange
		BusinessRule rule = new(
			"Filter city by country",
			new BusinessRuleConditionGroup("AND", []),
			[
				new ApplyFilterBusinessRuleAction(
					"City",
					"Country.TimeZone",
					"Country",
					"TimeZone",
					clearValue: true,
					populateValue: false)
			]);
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["City"] = new("City", "Lookup", "City"),
				["City.Country.TimeZone"] = new("City.Country.TimeZone", "Lookup", "TimeZone"),
				["Country"] = new("Country", "Lookup", "Country"),
				["Country.TimeZone"] = new("Country.TimeZone", "Lookup", "TimeZone")
			};

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, attributeMap);

		// Assert
		act.Should().NotThrow(
			because: "apply-filter is the one entity-rule shape that keeps its logic inside the action payload and may omit outer conditions");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects apply-filter rules when the condition group omits the conditions list instead of supplying an empty array.")]
	public void Validate_Should_Reject_ApplyFilter_With_Null_Condition_List() {
		// Arrange
		BusinessRule rule = new(
			"Filter city by country",
			new BusinessRuleConditionGroup("AND", null!),
			[
				new ApplyFilterBusinessRuleAction(
					"City",
					"Country.TimeZone",
					"Country",
					"TimeZone",
					clearValue: true,
					populateValue: false)
			]);
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["City"] = new("City", "Lookup", "City"),
				["City.Country.TimeZone"] = new("City.Country.TimeZone", "Lookup", "TimeZone"),
				["Country"] = new("Country", "Lookup", "Country"),
				["Country.TimeZone"] = new("Country.TimeZone", "Lookup", "TimeZone")
			};

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, attributeMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.condition.conditions is required.",
				because: "apply-filter may use an empty condition list, but the conditions collection itself must still be present");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects apply-filter rules that combine the lookup-filter action with any other entity action.")]
	public void Validate_Should_Reject_ApplyFilter_Combined_With_Other_Action() {
		// Arrange
		BusinessRule rule = new(
			"Invalid mixed rule",
			new BusinessRuleConditionGroup("AND", []),
			[
				new ApplyFilterBusinessRuleAction("City", "Country", "Country", null),
				new MakeRequiredBusinessRuleAction(["Owner"])
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("City", 10, "City"),
			CreateColumn("Country", 10, "Country"),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("apply-filter rules support exactly one action and cannot be combined with other entity business-rule actions.",
				because: "apply-filter persistence expands into a dedicated parent/child rule family and cannot share a single input rule with other action kinds");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects apply-filter populateValue when sourceFilterPath is provided.")]
	public void Validate_Should_Reject_ApplyFilter_PopulateValue_With_SourceFilterPath() {
		// Arrange
		BusinessRule rule = new(
			"Invalid populate filter",
			new BusinessRuleConditionGroup("AND", []),
			[
				new ApplyFilterBusinessRuleAction(
					"City",
					"Country.TimeZone",
					"Country",
					"TimeZone",
					clearValue: false,
					populateValue: true)
			]);
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["City"] = new("City", "Lookup", "City"),
				["City.Country.TimeZone"] = new("City.Country.TimeZone", "Lookup", "TimeZone"),
				["Country"] = new("Country", "Lookup", "Country"),
				["Country.TimeZone"] = new("Country.TimeZone", "Lookup", "TimeZone")
			};

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, attributeMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.actions[*].populateValue is not supported when rule.actions[*].sourceFilterPath is set for apply-filter.",
				because: "current Freedom UI behavior does not allow back-population when the source side is a nested lookup path");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects apply-filter when targetFilterPath resolves to Guid instead of Lookup.")]
	public void Validate_Should_Reject_ApplyFilter_When_TargetFilterPath_Is_Guid() {
		// Arrange
		BusinessRule rule = new(
			"Invalid target filter path",
			new BusinessRuleConditionGroup("AND", []),
			[
				new ApplyFilterBusinessRuleAction(
					"City",
					"CountryId",
					"Country",
					"Id",
					clearValue: true,
					populateValue: false)
			]);
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["City"] = new("City", "Lookup", "City"),
				["City.CountryId"] = new("City.CountryId", "Guid", null),
				["Country"] = new("Country", "Lookup", "Country"),
				["Country.Id"] = new("Country.Id", "Guid", null)
			};

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, attributeMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Attribute 'City.CountryId' in rule.actions[*].targetFilterPath must be a Lookup.",
				because: "apply-filter targetFilterPath must resolve to a lookup attribute and should reject Guid paths");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects apply-filter when sourceFilterPath resolves to Guid instead of Lookup.")]
	public void Validate_Should_Reject_ApplyFilter_When_SourceFilterPath_Is_Guid() {
		// Arrange
		BusinessRule rule = new(
			"Invalid source filter path",
			new BusinessRuleConditionGroup("AND", []),
			[
				new ApplyFilterBusinessRuleAction(
					"City",
					"Country",
					"Country",
					"Id",
					clearValue: true,
					populateValue: false)
			]);
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["City"] = new("City", "Lookup", "City"),
				["City.Country"] = new("City.Country", "Lookup", "Country"),
				["Country"] = new("Country", "Lookup", "Country"),
				["Country.Id"] = new("Country.Id", "Guid", null)
			};

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, attributeMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Attribute 'Country.Id' in rule.actions[*].sourceFilterPath must be a Lookup.",
				because: "apply-filter sourceFilterPath must resolve to a lookup attribute and should reject Guid paths");
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
				new MakeRequiredBusinessRuleAction(["Owner"])
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.condition.conditions[*].rightExpression is required when comparisonType is 'equal'.",
				because: "binary comparisons should require an explicit right-hand operand");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects unsupported business-rule action types before action metadata is generated.")]
	public void Validate_Should_Reject_Unsupported_Action_Type() {
		// Arrange
		BusinessRule rule = CreateRule(
			actions: [
				new CustomBusinessRuleAction("set-visible", ["Owner"])
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Unsupported rule.actions[*].type 'set-visible'. Supported values: make-editable, make-read-only, make-required, make-optional, set-values, apply-filter, apply-static-filter.",
				because: "the current business-rule action subset is intentionally limited to supported field-state, set-values, and lookup-filter actions");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects action entries that do not declare any target attributes.")]
	public void Validate_Should_Reject_Missing_Action_Items() {
		// Arrange
		BusinessRule rule = CreateRule(
			actions: [
				new MakeRequiredBusinessRuleAction( new List<string>())
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
				new MakeRequiredBusinessRuleAction(["MissingOwner"])
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

	[TestCaseSource(nameof(SetValuesConstantCases))]
	[Category("Unit")]
	[Description("Accepts set-values actions with constant values for text number boolean and date/time targets.")]
	public void Validate_Should_Accept_SetValues_Action_With_Supported_Constant(
		int targetDataValueType,
		string targetPath,
		string constantJson) {
		// Arrange
		BusinessRule rule = CreateRule(
			actions: [
				CreateSetValuesAction(targetPath, new BusinessRuleExpression("Const", null, Json(constantJson)))
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn(targetPath, targetDataValueType));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().NotThrow(
			because: "set-values constants should support text number boolean Date DateTime and Time columns");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts set-values actions with Formula sources when the formula payload is a non-empty string.")]
	public void Validate_Should_Accept_SetValues_Action_With_Formula_Source() {
		// Arrange
		BusinessRule rule = CreateRule(
			actions: [
				CreateSetValuesAction("Score", new BusinessRuleExpression("Formula", expression: "BaseScore + BonusScore"))
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Score", 4),
			CreateColumn("BaseScore", 4),
			CreateColumn("BonusScore", 4));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().NotThrow(
			because: "formula set-values structure should be validated before field resolution and expression-schema translation");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects set-values Formula sources that use DateTime attributes in arithmetic expressions.")]
	public void Validate_Should_Reject_SetValues_Formula_With_DateTime_Source() {
		// Arrange
		BusinessRule rule = CreateRule(
			actions: [
				CreateSetValuesAction(
					"NumberOfDays",
					new BusinessRuleExpression("Formula", expression: "EndDate - StartDate + 1"))
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("NumberOfDays", 4),
			CreateColumn("StartDate", 7),
			CreateColumn("EndDate", 7));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Formula source attribute 'EndDate' has type DateTime. Formula set-values supports only numeric source attributes.",
				because: "current formula support is limited to numeric arithmetic and must not accept DateTime subtraction");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects set-values Formula assignments to non-numeric target attributes.")]
	public void Validate_Should_Reject_SetValues_Formula_With_Non_Numeric_Target() {
		// Arrange
		BusinessRule rule = CreateRule(
			actions: [
				CreateSetValuesAction("TextResult", new BusinessRuleExpression("Formula", expression: "BaseScore + BonusScore"))
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("TextResult", 1),
			CreateColumn("BaseScore", 4),
			CreateColumn("BonusScore", 4));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Formula target attribute 'TextResult' has type Text. Formula set-values supports only numeric target attributes.",
				because: "arithmetic formula results should only be assigned to numeric targets");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts set-values actions that assign from another same-typed attribute.")]
	public void Validate_Should_Accept_SetValues_Action_With_Attribute_Value_Source() {
		// Arrange
		BusinessRule rule = CreateRule(
			actions: [
				CreateSetValuesAction("TextResult", new BusinessRuleExpression("AttributeValue", "Status", null))
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("TextResult", 1));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().NotThrow(
			because: "Set values should support copying values from same-typed source attributes");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects set-values AttributeValue sources that omit the source path.")]
	public void Validate_Should_Reject_SetValues_Attribute_Source_With_Missing_Path() {
		// Arrange
		BusinessRule rule = CreateRule(
			actions: [
				CreateSetValuesAction("TextResult", new BusinessRuleExpression("AttributeValue"))
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("TextResult", 1));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.actions[*].items[*].value.path is required when value.type is 'AttributeValue'.",
				because: "attribute-source assignments need an explicit source column path");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects set-values AttributeValue sources when source and target data value types differ.")]
	public void Validate_Should_Reject_SetValues_Attribute_Source_With_Different_Type() {
		// Arrange
		BusinessRule rule = CreateRule(
			actions: [
				CreateSetValuesAction("Score", new BusinessRuleExpression("AttributeValue", "Status", null))
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Score", 4));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.actions[*].items[*] assigns source attribute 'Status' (Text) to target attribute 'Score' (Integer). Both attributes must have the same data value type.",
				because: "copying from another attribute is only safe when the source and target metadata types match");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts set-values AttributeValue sources that use a same-typed forward reference path.")]
	public void Validate_Should_Accept_SetValues_Attribute_Source_With_Forward_Path() {
		// Arrange
		BusinessRule rule = CreateRule(
			actions: [
				CreateSetValuesAction("Score", new BusinessRuleExpression("AttributeValue", "Owner.Age", null))
			]);
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["Status"] = new("Status", "Text", null),
				["Owner"] = new("Owner", "Lookup", "Contact"),
				["Owner.Age"] = new("Owner.Age", "Integer", null),
				["Score"] = new("Score", "Integer", null)
			};

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, attributeMap);

		// Assert
		act.Should().NotThrow(
			because: "forward-reference sources are valid when their final column type matches the target column");
	}

	[Test]
	[Category("Unit")]
	[Description("Allows forward-reference paths only as set-values AttributeValue sources.")]
	public void Validate_Should_Reject_Forward_Reference_Action_Targets() {
		// Arrange
		BusinessRule rule = CreateRule(
			actions: [
				CreateSetValuesAction("Owner.Age", new BusinessRuleExpression("AttributeValue", "Score", null))
			]);
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["Status"] = new("Status", "Text", null),
				["Owner"] = new("Owner", "Lookup", "Contact"),
				["Owner.Age"] = new("Owner.Age", "Integer", null),
				["Score"] = new("Score", "Integer", null)
			};

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, attributeMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.actions[*].items[*].expression.path must reference a direct entity attribute. Forward reference paths are supported only in rule.actions[*].items[*].value.path.",
				because: "Set values can read through a lookup but must still write to a current-object column");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects set-values Formula sources when the formula expression is missing.")]
	public void Validate_Should_Reject_SetValues_Formula_With_Missing_Expression() {
		// Arrange
		BusinessRule rule = CreateRule(
			actions: [
				CreateSetValuesAction("Score", new BusinessRuleExpression("Formula"))
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Score", 4),
			CreateColumn("BaseScore", 4));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.actions[*].items[*].value.expression must be a non-empty string when value.type is 'Formula'.",
				because: "the formula contract should receive agent-friendly expression text before expression-schema translation");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects set-values Formula sources that contain symbols outside the local arithmetic whitelist.")]
	public void Validate_Should_Reject_SetValues_Formula_With_Unsupported_Operator() {
		// Arrange
		BusinessRule rule = CreateRule(
			actions: [
				CreateSetValuesAction("Score", new BusinessRuleExpression("Formula", expression: "BaseScore > 0"))
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Score", 4),
			CreateColumn("BaseScore", 4));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Formula expression supports only direct entity fields, numbers, arithmetic operators (+, -, *, /), dots, parentheses, and whitespace.",
				because: "local validation should reject expressions outside the supported arithmetic scope before server validation");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects set-values constants that are incompatible with the target column data value type.")]
	public void Validate_Should_Reject_SetValues_Action_With_Incompatible_Constant() {
		// Arrange
		BusinessRule rule = CreateRule(
			actions: [
				CreateSetValuesAction("Score", new BusinessRuleExpression("Const", null, Json("\"42\"")))
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Score", 4));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.actions[*].items[*].value.value must be a JSON number when the target attribute is a numeric type.",
				because: "numeric set-values targets should receive JSON numbers, not numeric-looking strings");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects set-values DateTime constants without an explicit timezone suffix.")]
	public void Validate_Should_Reject_SetValues_DateTime_Constant_Without_Timezone() {
		// Arrange
		BusinessRule rule = CreateRule(
			actions: [
				CreateSetValuesAction("PlannedOn", new BusinessRuleExpression("Const", null, Json("\"2025-01-15T13:45:00\"")))
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("PlannedOn", 7));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.actions[*].items[*].value.value must be a JSON string in ISO 8601 date-time format with a timezone suffix ('Z' or '+/-HH:mm') when the target attribute is DateTime.",
				because: "DateTime set-values constants should have deterministic timezone semantics");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects set-values Date constants that do not use yyyy-MM-dd format.")]
	public void Validate_Should_Reject_SetValues_Date_Constant_With_Invalid_Format() {
		// Arrange
		BusinessRule rule = CreateRule(
			actions: [
				CreateSetValuesAction("StartDate", new BusinessRuleExpression("Const", null, Json("\"2025-01-15T00:00:00Z\"")))
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("StartDate", 8));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.actions[*].items[*].value.value must be a JSON string in 'yyyy-MM-dd' format when the target attribute is Date.",
				because: "Date set-values constants should use the same date-only format as condition constants");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects set-values Time constants without an explicit timezone suffix.")]
	public void Validate_Should_Reject_SetValues_Time_Constant_Without_Timezone() {
		// Arrange
		BusinessRule rule = CreateRule(
			actions: [
				CreateSetValuesAction("ReminderTime", new BusinessRuleExpression("Const", null, Json("\"13:45:00\"")))
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("ReminderTime", 9));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.actions[*].items[*].value.value must be a JSON string in ISO 8601 time format with a timezone suffix ('Z' or '+/-HH:mm') when the target attribute is Time.",
				because: "Time set-values constants should have deterministic timezone semantics");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects set-values target attributes outside the supported constant data types.")]
	public void Validate_Should_Reject_SetValues_Action_For_Unsupported_Target_Type() {
		// Arrange
		BusinessRule rule = CreateRule(
			actions: [
				CreateSetValuesAction("Owner", new BusinessRuleExpression("Const", null, Json("\"11111111-1111-1111-1111-111111111111\"")))
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Const set-values is not supported for target attribute type 'Lookup'.",
				because: "lookup set-values constants are outside the current support scope");
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

	[TestCase(11, "Enum")]
	[TestCase(13, "Blob")]
	[TestCase(25, "File")]
	[Category("Unit")]
	[Description("Rejects Const right expressions when the left attribute has a type that does not support constant comparison (Enum, Blob, File).")]
	public void Validate_Should_Reject_Const_For_Unsupported_Left_Type(int dataValueType, string expectedTypeName) {
		// Arrange
		BusinessRule rule = CreateRule(
			leftExpression: new BusinessRuleExpression("AttributeValue", "UnsupportedCol", null),
			rightExpression: new BusinessRuleExpression("Const", null, Json("\"any-value\"")));
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("UnsupportedCol", dataValueType),
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage($"Const rightExpression is not supported for left attribute type '{expectedTypeName}'.",
				because: $"{expectedTypeName} columns do not have a defined constant comparison contract and should be rejected before metadata is serialized");
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

	[Test]
	[Category("Unit")]
	[Description("Rejects null condition entries with a deterministic validation error.")]
	public void Validate_Should_Reject_Null_Condition_Entry() {
		// Arrange
		BusinessRule rule = new(
			"Rule caption",
			new BusinessRuleConditionGroup(
				"AND",
				[
					null!
				]),
			[
				new MakeRequiredBusinessRuleAction(["Owner"])
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Owner", 10, "Contact"));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.condition.conditions[*] is required.",
				because: "malformed MCP condition arrays should return stable validation errors instead of null reference failures");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects null set-values item entries with a deterministic validation error.")]
	public void Validate_Should_Reject_Null_SetValues_Item_Entry() {
		// Arrange
		BusinessRule rule = CreateRule(
			actions: [
				new SetValuesBusinessRuleAction(
					new List<BusinessRuleSetValueItem> {
						null!
					})
			]);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1));

		// Act
		Action act = () => BusinessRuleValidator.Validate(rule, columnMap);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.actions[*].items[*] is required.",
				because: "malformed MCP set-values arrays should return stable validation errors instead of null reference failures");
	}

	private static BusinessRule CreateRule(
		string caption = "Rule caption",
		string logicalOperation = "AND",
		string comparisonType = "equal",
		BusinessRuleExpression? leftExpression = null,
		BusinessRuleExpression? rightExpression = null,
		List<BusinessRuleAction>? actions = null,
		bool omitRightExpression = false) =>
		new(
			caption,
			new BusinessRuleConditionGroup(
				logicalOperation,
				[
					new BusinessRuleCondition(
						leftExpression ?? new BusinessRuleExpression("AttributeValue", "Status", null),
						comparisonType,
						omitRightExpression
							? null
							: rightExpression ?? new BusinessRuleExpression("Const", null, Json("\"Draft\"")))
				]),
			actions ?? [
				new MakeRequiredBusinessRuleAction(["Owner"])
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

	private static BusinessRuleAction CreateSetValuesAction(string targetPath, BusinessRuleExpression value) =>
		new SetValuesBusinessRuleAction(
			new List<BusinessRuleSetValueItem> {
				new(
					new BusinessRuleExpression("AttributeValue", targetPath, null),
					value)
			});

	private static JsonElement Json(string json) =>
		JsonSerializer.Deserialize<JsonElement>(json);
}

