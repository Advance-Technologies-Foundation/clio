using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command.BusinessRules;
using Clio.Command.EntitySchemaDesigner;
using FluentAssertions;
using NUnit.Framework;
using Clio.Command.BusinessRules.Converters;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class SimpleToFullBusinessRuleConverterTests {
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
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToMetadata(columnMap, rule, "UsrTask");

		// Assert
		BusinessRuleGroupConditionMetadataDto conditionGroup = metadata.Cases[0].Condition!
			.Should().BeOfType<BusinessRuleGroupConditionMetadataDto>(
				because: "standard business rules should still persist grouped case conditions").Subject;
		BusinessRuleConditionMetadataDto condition = conditionGroup.Conditions[0];
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
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToMetadata(columnMap, rule, "UsrTask");

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
		BusinessRuleGroupConditionMetadataDto conditionGroup = @case.Condition!
			.Should().BeOfType<BusinessRuleGroupConditionMetadataDto>(
				because: "standard multi-condition rules should still persist a condition group").Subject;
		conditionGroup.LogicalOperation.Should().Be(BusinessRuleConstants.LogicalOr,
			because: "OR groups should map to the business-rule logical OR constant");
		conditionGroup.Conditions.Should().HaveCount(2,
			because: "every source condition should be preserved in metadata");
		conditionGroup.Conditions[0].ComparisonType.Should().Be(BusinessRuleConstants.ComparisonEqual,
			because: "equal comparisons should map to the business-rule equality constant");
		conditionGroup.Conditions[1].ComparisonType.Should().Be(BusinessRuleConstants.ComparisonNotEqual,
			because: "not-equal comparisons should map to the business-rule inequality constant");
		conditionGroup.Conditions[0].LeftExpression.Type.Should().Be("AttributeValue",
			because: "left expressions should remain attribute references in metadata");
		conditionGroup.Conditions[0].LeftExpression.Path.Should().Be("Owner",
			because: "the left attribute path should be preserved");
		conditionGroup.Conditions[0].LeftExpression.DataValueTypeName.Should().Be("Lookup",
			because: "lookup attributes should preserve their runtime data value type");
		conditionGroup.Conditions[0].LeftExpression.ReferenceSchemaName.Should().Be("Contact",
			because: "lookup attributes should preserve the reference schema name");
		conditionGroup.Conditions[0].RightExpression.Type.Should().Be("AttributeValue",
			because: "attribute-to-attribute comparisons should emit an attribute expression on the right side");
		conditionGroup.Conditions[0].RightExpression.Path.Should().Be("Approver",
			because: "the right attribute path should be preserved for attribute comparisons");
		conditionGroup.Conditions[1].RightExpression.Type.Should().Be("Const",
			because: "constant comparisons should emit value expressions on the right side");
		conditionGroup.Conditions[1].RightExpression.DataValueTypeName.Should().Be("Money",
			because: "constant expressions should inherit the left attribute runtime type");
		conditionGroup.Conditions[1].RightExpression.ReferenceSchemaName.Should().BeNullOrEmpty(
			because: "non-lookup constants should not carry a reference schema");
		conditionGroup.Conditions[1].RightExpression.Value.Should().NotBeNull(
			because: "constant comparisons should keep a persisted value payload in metadata");
		conditionGroup.Conditions[1].RightExpression.Value!.ToString().Should().Be("5",
			because: "numeric constants should keep the raw comparison value");
		@case.Actions[0].Should().BeOfType<FieldSelectionBusinessRuleActionMetadataDto>(
			because: "field-state entity actions should still persist as field-selection metadata").Subject.Items
			.Should().Be(" Owner ,Amount,Owner",
			because: "action items should be preserved exactly as supplied without trimming or deduplication");
		@case.Actions[1].Should().BeOfType<FieldSelectionBusinessRuleActionMetadataDto>(
			because: "field-state entity actions should still persist as field-selection metadata").Subject.Items
			.Should().Be("Status",
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
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToMetadata(columnMap, rule, "UsrTask");

		// Assert
		BusinessRuleGroupConditionMetadataDto conditionGroup = metadata.Cases[0].Condition!
			.Should().BeOfType<BusinessRuleGroupConditionMetadataDto>(
				because: "standard business rules should still persist grouped case conditions").Subject;
		BusinessRuleExpressionMetadataDto rightExpression = conditionGroup.Conditions[0].RightExpression;
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
	[Description("Maps a lookup system variable into a BusinessRuleSysValueExpression that inherits the left lookup type and reference schema and adds no extra trigger.")]
	public void ToMetadata_Should_Map_Lookup_SysValue_Right_Expression() {
		// Arrange
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Owner", 10, "Contact"),
			CreateColumn("Status", 1));
		BusinessRule rule = new(
			"Require status when owner is the current user",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Owner", null),
						"equal",
						new BusinessRuleExpression("SysValue", sysValueName: "CurrentUserContact"))
				]),
			[
				new MakeRequiredBusinessRuleAction(["Status"])
			]);

		// Act
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToMetadata(columnMap, rule, "UsrTask");

		// Assert
		BusinessRuleGroupConditionMetadataDto conditionGroup = metadata.Cases[0].Condition!
			.Should().BeOfType<BusinessRuleGroupConditionMetadataDto>(
				because: "system-variable conditions should still persist a grouped case condition").Subject;
		BusinessRuleExpressionMetadataDto rightExpression = conditionGroup.Conditions[0].RightExpression!;
		rightExpression.TypeName.Should().Be(BusinessRuleConstants.BusinessRuleSysValueExpressionTypeName,
			because: "system variables should persist as the core BusinessRuleSysValueExpression type");
		rightExpression.Type.Should().Be("SysValue",
			because: "the persisted expression should keep its SysValue discriminator");
		rightExpression.SysValueName.Should().Be("CurrentUserContact",
			because: "the selected system variable name should be persisted verbatim");
		rightExpression.DataValueTypeName.Should().Be("Lookup",
			because: "the system variable expression should inherit the left attribute runtime type");
		rightExpression.ReferenceSchemaName.Should().Be("Contact",
			because: "the system variable expression should inherit the left lookup reference schema");
		rightExpression.Value.Should().BeNull(
			because: "system variables resolve at runtime and carry no static value payload");
		metadata.Triggers.Should().ContainSingle(trigger =>
				trigger.Type == BusinessRuleConstants.ChangeAttributeValueTriggerType,
			because: "only the left attribute should add a change trigger; a SysValue right side has no attribute path");
		metadata.Triggers.Should().Contain(trigger =>
				trigger.Type == BusinessRuleConstants.ChangeAttributeValueTriggerType && trigger.Name == "Owner",
			because: "the left attribute should drive the change trigger");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps a CurrentUserRoles CONTAIN role condition (SysValue on the left, Const role on the right) with no change triggers.")]
	public void ToPageMetadata_Should_Map_CurrentUserRoles_Contain_Role_Condition() {
		// Arrange
		const string roleId = "83a43ebc-f36b-1410-298d-001e8c82bcad";
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal);
		BusinessRule rule = new(
			"Show Resolved for administrators",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("SysValue", sysValueName: "CurrentUserRoles"),
						"contain",
						new BusinessRuleExpression("Const", null, Json(roleId)))
				]),
			[
				new ShowElementBusinessRuleAction(["Checkbox_7c8snfo"])
			]);

		// Act
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToPageMetadata(attributeMap, rule);

		// Assert
		BusinessRuleConditionMetadataDto condition = metadata.Cases[0].Condition!
			.Should().BeOfType<BusinessRuleGroupConditionMetadataDto>(
				because: "role-gate conditions still persist a grouped case condition").Subject
			.Conditions[0];
		condition.ComparisonType.Should().Be(BusinessRuleConstants.ComparisonContain,
			because: "the 'contain' comparison should map to the collection-membership comparison value");
		condition.LeftExpression.TypeName.Should().Be(BusinessRuleConstants.BusinessRuleSysValueExpressionTypeName,
			because: "a system variable on the left should persist as the core BusinessRuleSysValueExpression type");
		condition.LeftExpression.Type.Should().Be("SysValue",
			because: "the left expression discriminator should be preserved");
		condition.LeftExpression.SysValueName.Should().Be("CurrentUserRoles",
			because: "the role-collection system variable name should be persisted");
		condition.LeftExpression.DataValueTypeName.Should().Be("ObjectList",
			because: "CurrentUserRoles is a collection of roles");
		condition.RightExpression!.Type.Should().Be("Const",
			because: "the compared role should persist as a constant");
		condition.RightExpression.DataValueTypeName.Should().Be("Lookup",
			because: "a role compared against the CurrentUserRoles collection is a single SysAdminUnit lookup");
		condition.RightExpression.ReferenceSchemaName.Should().Be("SysAdminUnit",
			because: "the constant role inherits the collection's SysAdminUnit reference schema");
		condition.RightExpression.Value!.ToString().Should().Be(roleId,
			because: "the role record id should be persisted verbatim");
		metadata.Triggers.Should().OnlyContain(trigger => trigger.Type == BusinessRuleConstants.DataLoadedTriggerType,
			because: "a condition with no attribute operand contributes only the DataLoaded trigger");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps a CurrentUserContact EQUAL contact condition (SysValue on the left, Const contact on the right).")]
	public void ToPageMetadata_Should_Map_CurrentUserContact_Left_Equal_Const_Condition() {
		// Arrange
		const string contactId = "410006e1-ca4e-4502-a9ec-e54d922d2c00";
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal);
		BusinessRule rule = new(
			"Show Assignee group for the supervisor",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("SysValue", sysValueName: "CurrentUserContact"),
						"equal",
						new BusinessRuleExpression("Const", null, Json(contactId)))
				]),
			[
				new ShowElementBusinessRuleAction(["Input_wiom81t"])
			]);

		// Act
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToPageMetadata(attributeMap, rule);

		// Assert
		BusinessRuleConditionMetadataDto condition = metadata.Cases[0].Condition!
			.Should().BeOfType<BusinessRuleGroupConditionMetadataDto>().Subject.Conditions[0];
		condition.ComparisonType.Should().Be(BusinessRuleConstants.ComparisonEqual,
			because: "the 'equal' comparison should map to the equality comparison value");
		condition.LeftExpression.SysValueName.Should().Be("CurrentUserContact",
			because: "the current-user-contact system variable should be on the left");
		condition.LeftExpression.DataValueTypeName.Should().Be("Lookup",
			because: "CurrentUserContact resolves to a Contact lookup");
		condition.RightExpression!.DataValueTypeName.Should().Be("Lookup",
			because: "the constant contact inherits the lookup type");
		condition.RightExpression.ReferenceSchemaName.Should().Be("Contact",
			because: "the constant contact inherits the Contact reference schema from the system variable");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps a date system variable into a page BusinessRuleSysValueExpression with the left date type for relational comparisons.")]
	public void ToPageMetadata_Should_Map_Date_SysValue_Right_Expression() {
		// Arrange
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["PDS_UsrDueDate"] = new("PDS_UsrDueDate", "Date", null)
			};
		BusinessRule rule = new(
			"Hide reminder when due on or before today",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "PDS_UsrDueDate", null),
						"less-than-or-equal",
						new BusinessRuleExpression("SysValue", sysValueName: "CurrentDate"))
				]),
			[
				new HideElementBusinessRuleAction(["ReminderLabel"])
			]);

		// Act
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToPageMetadata(attributeMap, rule);

		// Assert
		BusinessRuleGroupConditionMetadataDto conditionGroup = metadata.Cases[0].Condition!
			.Should().BeOfType<BusinessRuleGroupConditionMetadataDto>(
				because: "page system-variable conditions should still persist a grouped case condition").Subject;
		BusinessRuleExpressionMetadataDto rightExpression = conditionGroup.Conditions[0].RightExpression!;
		rightExpression.TypeName.Should().Be(BusinessRuleConstants.BusinessRuleSysValueExpressionTypeName,
			because: "page system variables should also persist as the core BusinessRuleSysValueExpression type");
		rightExpression.SysValueName.Should().Be("CurrentDate",
			because: "the selected date system variable name should be persisted verbatim");
		rightExpression.DataValueTypeName.Should().Be("Date",
			because: "the date system variable expression should inherit the left date attribute type");
		rightExpression.Value.Should().BeNull(
			because: "system variables resolve at runtime and carry no static value payload on the page path either");
		metadata.Triggers.Should().ContainSingle(trigger =>
				trigger.Type == BusinessRuleConstants.ChangeAttributeValueTriggerType
				&& trigger.Name == "PDS_UsrDueDate",
			because: "only the left attribute should add a change trigger; a SysValue right side has no attribute path");
	}

	[Test]
	[Category("Unit")]
	[Description("Normalizes a non-canonical system variable name casing to the canonical catalog name before persisting the SysValue expression.")]
	public void ToMetadata_Should_Persist_Canonical_SysValue_Name_When_Input_Casing_Differs() {
		// Arrange
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("DueDate", 8),
			CreateColumn("Owner", 10, "Contact"));
		BusinessRule rule = new(
			"Flag overdue records",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "DueDate", null),
						"less-than-or-equal",
						new BusinessRuleExpression("SysValue", sysValueName: "currentdate"))
				]),
			[
				new MakeRequiredBusinessRuleAction(["Owner"])
			]);

		// Act
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToMetadata(columnMap, rule, "UsrTask");

		// Assert
		BusinessRuleGroupConditionMetadataDto conditionGroup = metadata.Cases[0].Condition!
			.Should().BeOfType<BusinessRuleGroupConditionMetadataDto>(
				because: "system-variable conditions should still persist a grouped case condition").Subject;
		BusinessRuleExpressionMetadataDto rightExpression = conditionGroup.Conditions[0].RightExpression!;
		rightExpression.SysValueName.Should().Be("CurrentDate",
			because: "the validator accepts the name case-insensitively but the platform resolves it by exact name, so the canonical catalog casing must be persisted");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps a page lookup system variable into a SysValue expression that inherits the left lookup type and reference schema.")]
	public void ToPageMetadata_Should_Map_Lookup_SysValue_Right_Expression() {
		// Arrange
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["PDS_UsrOwner_a1b2c3d"] = new("PDS_UsrOwner_a1b2c3d", "Lookup", "Contact")
			};
		BusinessRule rule = new(
			"Show panel when owner is the current user",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "PDS_UsrOwner_a1b2c3d", null),
						"equal",
						new BusinessRuleExpression("SysValue", sysValueName: "CurrentUserContact"))
				]),
			[
				new ShowElementBusinessRuleAction(["Panel_0dqt4ly"])
			]);

		// Act
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToPageMetadata(attributeMap, rule);

		// Assert
		BusinessRuleGroupConditionMetadataDto conditionGroup = metadata.Cases[0].Condition!
			.Should().BeOfType<BusinessRuleGroupConditionMetadataDto>(
				because: "page system-variable conditions should still persist a grouped case condition").Subject;
		BusinessRuleExpressionMetadataDto rightExpression = conditionGroup.Conditions[0].RightExpression!;
		rightExpression.TypeName.Should().Be(BusinessRuleConstants.BusinessRuleSysValueExpressionTypeName,
			because: "page lookup system variables should persist as the core BusinessRuleSysValueExpression type");
		rightExpression.SysValueName.Should().Be("CurrentUserContact",
			because: "the selected lookup system variable name should be persisted verbatim on the page path");
		rightExpression.DataValueTypeName.Should().Be("Lookup",
			because: "the page lookup system variable expression should inherit the left lookup attribute type");
		rightExpression.ReferenceSchemaName.Should().Be("Contact",
			because: "the page SysValue right side inherits the left lookup reference schema, unlike page AttributeValue expressions which omit it");
		rightExpression.Value.Should().BeNull(
			because: "system variables resolve at runtime and carry no static value payload");
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
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToPageMetadata(attributeMap, rule);

		// Assert
		BusinessRuleGroupConditionMetadataDto conditionGroup = metadata.Cases[0].Condition!
			.Should().BeOfType<BusinessRuleGroupConditionMetadataDto>(
				because: "page business rules should still persist grouped case conditions").Subject;
		BusinessRuleConditionMetadataDto condition = conditionGroup.Conditions[0];
		condition.LeftExpression.ReferenceSchemaName.Should().BeNull(
			because: "page AttributeValue metadata should match Creatio page add-on shape and omit referenceSchemaName");
		condition.RightExpression!.ReferenceSchemaName.Should().Be("Country",
			because: "lookup constant metadata still needs the referenced schema for value interpretation");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps data-source-scoped page condition attributes to scoped metadata and triggers.")]
	public void ToPageMetadata_Should_Emit_Scope_For_Datasource_Scoped_Condition_Attributes() {
		// Arrange
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["PDS.ModifiedOn"] = new("ModifiedOn", "DateTime", null, "PDS"),
				["PDS.CreatedOn"] = new("CreatedOn", "DateTime", null, "PDS")
			};
		BusinessRule rule = new(
			"Show name when modified after created",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "PDS.ModifiedOn", null),
						"greater-than",
						new BusinessRuleExpression("AttributeValue", "PDS.CreatedOn", null))
				]),
			[
				new ShowElementBusinessRuleAction(["Name"])
			]);

		// Act
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToPageMetadata(attributeMap, rule);

		// Assert
		BusinessRuleConditionMetadataDto condition = metadata.Cases[0].Condition!
			.Should().BeOfType<BusinessRuleGroupConditionMetadataDto>(
				because: "page business rules persist grouped case conditions").Subject.Conditions[0];
		condition.LeftExpression.Path.Should().Be("ModifiedOn",
			because: "a data-source-scoped condition operand persists the bare entity column as its path");
		condition.LeftExpression.ScopeId.Should().Be("PDS",
			because: "the data source name is carried on scopeId when the column is not surfaced on the page");
		condition.RightExpression!.ScopeId.Should().Be("PDS",
			because: "the right-side data-source-scoped operand is also scoped to its data source");

		metadata.Triggers
			.Where(trigger => trigger.Type == BusinessRuleConstants.ChangeAttributeValueTriggerType)
			.Should().BeEquivalentTo(
				new[] {
					new { Name = "ModifiedOn", ScopeId = (string?)"PDS" },
					new { Name = "CreatedOn", ScopeId = (string?)"PDS" }
				},
				because: "each data-source-scoped condition column contributes a change trigger scoped to its data source");
		metadata.Triggers
			.Where(trigger => trigger.Type == BusinessRuleConstants.DataLoadedTriggerType)
			.Should().BeEquivalentTo(
				new[] { new { Name = "PDS", ScopeId = (string?)string.Empty } },
				because: "a data-source-scoped rule loads via a DataLoaded trigger named after the data source and no page-level DataLoaded trigger");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps page field-state actions to Creatio page business-rule action metadata type names.")]
	public void ToPageMetadata_Should_Map_Page_Field_State_Action_Metadata() {
		// Arrange
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["PDS_Status"] = new("PDS_Status", "Text", null)
			};
		BusinessRule rule = new(
			"Change page element state",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "PDS_Status", null),
						"is-filled-in")
				]),
			[
				new MakeEditableBusinessRuleAction(["NameInput"]),
				new MakeReadOnlyBusinessRuleAction(["AmountInput"]),
				new MakeRequiredBusinessRuleAction(["CloseDateInput"]),
				new MakeOptionalBusinessRuleAction(["CommentInput"])
			]);

		// Act
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToPageMetadata(attributeMap, rule);

		// Assert
		metadata.Cases.Single().Actions.Select(action => action.TypeName).Should().Equal([
				BusinessRuleConstants.BusinessRuleEditableElementTypeName,
				BusinessRuleConstants.BusinessRuleReadonlyElementTypeName,
				BusinessRuleConstants.BusinessRuleRequiredElementTypeName,
				BusinessRuleConstants.BusinessRuleOptionalElementTypeName
			],
			because: "page editability and required-state actions should persist using Creatio business-rule metadata type names");
		metadata.Cases.Single().Actions
			.Select(action => action.Should().BeOfType<FieldSelectionBusinessRuleActionMetadataDto>(
				because: "page actions should persist as field-selection metadata").Subject.Items)
			.Should().Equal([
				"NameInput",
				"AmountInput",
				"CloseDateInput",
				"CommentInput"
			],
			because: "page action items should stay as page element names");
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
			CreateColumn("PlannedOn", 7),
			CreateColumn("Owner", 10, "Contact"));
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
							new BusinessRuleExpression("Const", null, Json("2025-01-15T13:45:00+02:00"))),
						new(
							new BusinessRuleExpression("AttributeValue", "Owner", null),
							new BusinessRuleExpression("Const", null, Json("11111111-1111-1111-1111-111111111111")))
					})
			]);

		// Act
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToMetadata(columnMap, rule, "UsrTask");

		// Assert
		FieldSelectionBusinessRuleActionMetadataDto action = metadata.Cases[0].Actions.Single()
			.Should().BeOfType<FieldSelectionBusinessRuleActionMetadataDto>(
				because: "set-values should still persist through the field-selection metadata DTO shape").Subject;
		action.TypeName.Should().Be(BusinessRuleConstants.BusinessRuleSetValuesElementTypeName,
			because: "set-values actions should persist as the core BusinessRuleActionSetValues type");
		action.Items.Should().BeOfType<List<BusinessRuleSetValueItemMetadataDto>>(
			because: "set-values actions should persist assignment items instead of a comma-separated target list");
		List<BusinessRuleSetValueItemMetadataDto> items = (List<BusinessRuleSetValueItemMetadataDto>)action.Items!;
		JsonElement serializedItems = JsonSerializer.SerializeToElement(items, BusinessRuleConstants.JsonOptions);
		items.Should().HaveCount(7,
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
		items[6].Value.DataValueTypeName.Should().Be("Lookup",
			because: "lookup constants should inherit the Lookup target column type");
		items[6].Value.ReferenceSchemaName.Should().Be("Contact",
			because: "lookup set-values constants should keep the target reference schema name");
		serializedItems[6].GetProperty("value").GetProperty("value").GetString().Should().Be("11111111-1111-1111-1111-111111111111",
			because: "lookup set-values constants should persist the raw lookup record id");
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
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToMetadata(columnMap, rule, "UsrTask");

		// Assert
		FieldSelectionBusinessRuleActionMetadataDto action = metadata.Cases[0].Actions.Single()
			.Should().BeOfType<FieldSelectionBusinessRuleActionMetadataDto>(
				because: "formula set-values should still persist through the field-selection metadata DTO shape").Subject;
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
	[Description("Maps set-values AttributeValue assignments into attribute expression metadata and trigger roots.")]
	public void ToMetadata_Should_Map_SetValues_Attribute_Value_Action_Metadata() {
		// Arrange
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["Status"] = new("Status", "Text", null),
				["UsrNameCopy"] = new("UsrNameCopy", "Text", null),
				["Name"] = new("Name", "Text", null),
				["UsrCreatorAge"] = new("UsrCreatorAge", "Integer", null),
				["CreatedBy"] = new("CreatedBy", "Lookup", "Contact"),
				["CreatedBy.Age"] = new("CreatedBy.Age", "Integer", null)
			};
		BusinessRule rule = new(
			"Copy values",
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
							new BusinessRuleExpression("AttributeValue", "UsrNameCopy", null),
							new BusinessRuleExpression("AttributeValue", "Name", null)),
						new(
							new BusinessRuleExpression("AttributeValue", "UsrCreatorAge", null),
							new BusinessRuleExpression("AttributeValue", "CreatedBy.Age", null))
					})
			]);

		// Act
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToMetadata(attributeMap, rule, "UsrTask");

		// Assert
		FieldSelectionBusinessRuleActionMetadataDto action = metadata.Cases[0].Actions.Single()
			.Should().BeOfType<FieldSelectionBusinessRuleActionMetadataDto>(
				because: "attribute-source set-values should still persist through the field-selection metadata DTO shape").Subject;
		List<BusinessRuleSetValueItemMetadataDto> items = (List<BusinessRuleSetValueItemMetadataDto>)action.Items!;
		items[0].Value.TypeName.Should().Be(BusinessRuleConstants.BusinessRuleAttributeExpressionTypeName,
			because: "direct AttributeValue sources should persist as core attribute expressions");
		items[0].Value.Type.Should().Be("AttributeValue",
			because: "the source discriminator should stay AttributeValue in add-on metadata");
		items[0].Value.Path.Should().Be("Name",
			because: "direct source attribute paths should be preserved");
		items[1].Value.Path.Should().Be("CreatedBy.Age",
			because: "forward reference source paths should be preserved for core evaluation");
		items[1].Value.DataValueTypeName.Should().Be("Integer",
			because: "the source expression should describe the final source column type");
		metadata.Triggers.Should().Contain(trigger =>
				trigger.Type == BusinessRuleConstants.ChangeAttributeValueTriggerType && trigger.Name == "Name",
			because: "direct AttributeValue sources should recalculate when the source column changes");
		metadata.Triggers.Should().Contain(trigger =>
				trigger.Type == BusinessRuleConstants.ChangeAttributeValueTriggerType && trigger.Name == "CreatedBy",
			because: "forward AttributeValue sources should match designer metadata and trigger on the root lookup column");
		metadata.Triggers.Should().NotContain(trigger => trigger.Name == "CreatedBy.Age",
			because: "designer-generated business-rule metadata does not emit full forward-path triggers");
		metadata.Triggers.Should().Contain(trigger =>
				trigger.Type == BusinessRuleConstants.DataLoadedTriggerType && trigger.Name == string.Empty,
			because: "Set values rules should still run on DataLoaded");
	}

	[Test]
	[Category("Unit")]
	[Description("Maps apply-filter rules into a parent filter-lookup rule that preserves user conditions plus autogenerated clear and populate child rules.")]
	public void ToEntityMetadata_Should_Map_ApplyFilter_Into_Parent_And_Child_Rules() {
		// Arrange
		string supervisorId = "4055a3b6-867e-3756-311a-576cbaba3230";
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["Owner"] = new("Owner", "Lookup", "Contact"),
				["Country"] = new("Country", "Lookup", "Country"),
				["Country.TimeZone"] = new("Country.TimeZone", "Lookup", "TimeZone"),
				["City"] = new("City", "Lookup", "City"),
				["City.Country"] = new("City.Country", "Lookup", "Country"),
				["City.Country.TimeZone"] = new("City.Country.TimeZone", "Lookup", "TimeZone")
			};
		BusinessRule rule = new(
			"  Filter city by country timezone  ",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Owner"),
						"equal",
						new BusinessRuleExpression("Const", value: JsonSerializer.SerializeToElement(supervisorId)))
				]),
			[
				new ApplyFilterBusinessRuleAction(
					"City",
					"Country.TimeZone",
					"Country",
					"TimeZone",
					clearValue: true,
					populateValue: false)
			]);

		// Act
		IReadOnlyList<BusinessRuleMetadataDto> metadata = SimpleToFullBusinessRuleConverter.ToEntityMetadata(attributeMap, rule, "UsrOrder");

		// Assert
		metadata.Should().HaveCount(2,
			because: "apply-filter with clear only should emit one parent rule and one autogenerated clear child rule");
		BusinessRuleMetadataDto parentRule = metadata[0];
		BusinessRuleMetadataDto clearRule = metadata[1];
		parentRule.Caption.Should().Be("Filter city by country timezone",
			because: "the parent apply-filter rule should trim and keep the requested caption");
		clearRule.Caption.Should().Be($"ChildRule-{parentRule.UId}-ClearValue",
			because: "autogenerated child rules should carry deterministic non-empty captions like the platform designer");
		parentRule.Cases.Single().Condition.Should().BeOfType<BusinessRuleGroupConditionMetadataDto>(
			because: "the parent apply-filter rule should persist the user-authored parent condition group").Subject.Conditions.Should().ContainSingle(
			because: "apply-filter parent rules should no longer discard the incoming user conditions");
		BusinessRuleConditionMetadataDto parentCondition = parentRule.Cases.Single().Condition!
			.Should().BeOfType<BusinessRuleGroupConditionMetadataDto>(
				because: "the parent rule should still use a group condition metadata wrapper").Subject.Conditions.Single();
		parentCondition.LeftExpression.Path.Should().Be("Owner",
			because: "the preserved parent condition should still target the original left attribute");
		parentCondition.RightExpression.Should().NotBeNull(
			because: "the preserved parent condition should still include the original right expression");
		parentCondition.RightExpression!.Value.Should().NotBeNull(
			because: "the preserved parent condition should still contain the original constant payload");
		parentCondition.RightExpression!.Value!.ToString().Should().Be(supervisorId,
			because: "the preserved parent condition should keep the original constant comparison value");
		parentRule.Cases.Single().Actions.Should().ContainSingle(
			because: "apply-filter parent rules should contain a single filter lookup action");
		BusinessRuleFilterLookupActionMetadataDto action = parentRule.Cases.Single().Actions.Single()
			.Should().BeOfType<BusinessRuleFilterLookupActionMetadataDto>(
				because: "the parent rule should persist the lookup filter action metadata shape").Subject;
		action.TypeName.Should().Be(BusinessRuleConstants.BusinessRuleFilterLookupElementTypeName,
			because: "apply-filter actions should use the Creatio filter lookup metadata type");
		action.LeftExpression.Path.Should().Be("City",
			because: "the left lookup expression should point at the target lookup");
		action.LeftExpression.FilterExpression.Should().Be("Country.TimeZone",
			because: "the target filter path should be stored separately from the target lookup root path");
		action.LeftExpression.DataValueTypeName.Should().Be("Lookup",
			because: "parent apply-filter target expressions should preserve the lookup data value type for UI parity");
		action.LeftExpression.ReferenceSchemaName.Should().Be("City",
			because: "parent apply-filter target expressions should preserve the target lookup reference schema");
		action.RightExpression.Path.Should().Be("Country",
			because: "the right lookup expression should point at the source lookup");
		action.RightExpression.FilterExpression.Should().Be("TimeZone",
			because: "the optional source filter path should be stored on the right expression");
		action.RightExpression.DataValueTypeName.Should().Be("Lookup",
			because: "parent apply-filter source expressions should preserve the lookup data value type for UI parity");
		action.RightExpression.ReferenceSchemaName.Should().Be("Country",
			because: "parent apply-filter source expressions should preserve the source lookup reference schema");
		parentRule.Triggers.Select(trigger => (trigger.Type, trigger.Name)).Should().Contain([
				(BusinessRuleConstants.DataLoadedTriggerType, string.Empty),
				(BusinessRuleConstants.ChangeAttributeValueTriggerType, "Country"),
				(BusinessRuleConstants.ChangeAttributeValueTriggerType, "Owner")
			],
			because: "the parent apply-filter rule should trigger on DataLoaded, source lookup changes, and preserved parent-condition attributes");
		clearRule.ParentUId.Should().Be(parentRule.UId,
			because: "autogenerated clear child rules should reference the parent rule");
		clearRule.ParentActionUId.Should().Be(action.UId,
			because: "autogenerated clear child rules should reference the parent action");
		BusinessRuleConditionMetadataDto clearCondition = clearRule.Cases.Single().Condition!
			.Should().BeOfType<BusinessRuleConditionMetadataDto>(
				because: "autogenerated clear child rules should persist a direct condition like the UI metadata").Subject;
		clearCondition.ComparisonType.Should().Be(BusinessRuleConstants.ComparisonNotEqual,
			because: "clear child rules should compare source and current target-filter values");
		JsonElement clearConditionJson = JsonSerializer.SerializeToElement(clearCondition, BusinessRuleConstants.JsonOptions);
		clearConditionJson.GetProperty("leftExpression").EnumerateObject().Select(property => property.Name).Should()
			.BeEquivalentTo(["typeName", "uId", "type", "path"],
				because: "UI-generated apply-filter clear child conditions omit lookup descriptor metadata on attribute expressions");
		clearConditionJson.GetProperty("rightExpression").EnumerateObject().Select(property => property.Name).Should()
			.BeEquivalentTo(["typeName", "uId", "type", "path"],
				because: "UI-generated apply-filter clear child conditions keep only the minimal attribute expression payload");
		FieldSelectionBusinessRuleActionMetadataDto clearAction = clearRule.Cases.Single().Actions.Single()
			.Should().BeOfType<FieldSelectionBusinessRuleActionMetadataDto>(
				because: "clear child rules should reset the target lookup through a set-values action").Subject;
		List<BusinessRuleSetValueItemMetadataDto> clearItems = clearAction.Items.Should().BeOfType<List<BusinessRuleSetValueItemMetadataDto>>(
			because: "clear child rules should persist one set-values item").Subject;
		clearItems.Should().ContainSingle(
			because: "clear child rules should emit exactly one set-values item for the lookup reset");
		clearItems.Single().Expression.Path.Should().Be("City",
			because: "clear child rules should reset the target lookup root itself");
		clearItems.Single().Expression.DataValueTypeName.Should().Be("Lookup",
			because: "the clear child target expression should preserve the lookup data value type");
		clearItems.Single().Expression.ReferenceSchemaName.Should().Be("City",
			because: "the clear child target expression should preserve the target lookup reference schema");
		clearItems.Single().Value.TypeName.Should().Be(BusinessRuleConstants.BusinessRuleEmptyValueExpressionTypeName,
			because: "clear child rules should assign the empty lookup expression");
		clearItems.Single().Value.DataValueTypeName.Should().Be("Lookup",
			because: "the empty lookup expression should preserve its lookup type metadata");
		clearItems.Single().Value.Value.Should().Be(Guid.Empty.ToString(),
			because: "UI parity requires clear child rules to assign the zero GUID empty lookup value");
		BusinessRuleMetadataDto populateRule = SimpleToFullBusinessRuleConverter.ToEntityMetadata(
			attributeMap,
			new BusinessRule(
				rule.Caption,
				rule.Condition,
				[
					new ApplyFilterBusinessRuleAction(
						"City",
						"Country.TimeZone",
						"Country",
						"TimeZone",
						clearValue: false,
						populateValue: true)
				]),
			"UsrOrder")[1];
		JsonElement populateConditionJson = JsonSerializer.SerializeToElement(
				populateRule.Cases.Single().Condition!,
				BusinessRuleConstants.JsonOptions);
		populateConditionJson.GetProperty("leftExpression").EnumerateObject().Select(property => property.Name).Should()
			.BeEquivalentTo(["typeName", "uId", "type", "path"],
				because: "UI-generated apply-filter populate child conditions also omit lookup descriptor metadata");
		FieldSelectionBusinessRuleActionMetadataDto populateAction = populateRule.Cases.Single().Actions.Single()
			.Should().BeOfType<FieldSelectionBusinessRuleActionMetadataDto>(
				because: "populate child rules should assign a lookup value through a set-values action").Subject;
		List<BusinessRuleSetValueItemMetadataDto> populateItems = populateAction.Items.Should().BeOfType<List<BusinessRuleSetValueItemMetadataDto>>(
			because: "populate child rules should persist one set-values item").Subject;
		populateItems.Should().ContainSingle(
			because: "populate child rules should emit exactly one set-values item for the source lookup");
		populateItems.Single().Expression.Path.Should().Be("Country",
			because: "populate child rules should assign the source lookup root");
		populateItems.Single().Expression.DataValueTypeName.Should().Be("Lookup",
			because: "the populate child target expression should preserve the lookup data value type");
		populateItems.Single().Expression.ReferenceSchemaName.Should().Be("Country",
			because: "the populate child target expression should preserve the source lookup reference schema");
		populateItems.Single().Value.Path.Should().Be("City.Country.TimeZone",
			because: "populate child rules should copy the resolved target-related lookup path");
		populateItems.Single().Value.DataValueTypeName.Should().Be("Lookup",
			because: "the copied target-related lookup path should preserve its lookup data value type");
		populateItems.Single().Value.ReferenceSchemaName.Should().Be("TimeZone",
			because: "the copied target-related lookup path should preserve the final lookup reference schema");
	}

	[Test]
	[Category("Unit")]
	[Description("Uses the resolved source lookup path in the autogenerated clear child rule when sourceFilterPath is provided.")]
	public void ToEntityMetadata_Should_Use_SourceFilterPath_In_ApplyFilter_Clear_Child_Condition() {
		// Arrange
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["Country"] = new("Country", "Lookup", "Country"),
				["Country.TimeZone"] = new("Country.TimeZone", "Lookup", "TimeZone"),
				["City"] = new("City", "Lookup", "City"),
				["City.Country"] = new("City.Country", "Lookup", "Country"),
				["City.Country.TimeZone"] = new("City.Country.TimeZone", "Lookup", "TimeZone")
			};
		BusinessRule rule = new(
			"Filter city by country timezone",
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

		// Act
		IReadOnlyList<BusinessRuleMetadataDto> metadata = SimpleToFullBusinessRuleConverter.ToEntityMetadata(attributeMap, rule, "UsrOrder");

		// Assert
		BusinessRuleConditionMetadataDto clearCondition = metadata[1].Cases.Single().Condition!
			.Should().BeOfType<BusinessRuleConditionMetadataDto>(
				because: "apply-filter clear child rules should persist a direct condition").Subject;
		clearCondition.LeftExpression.Path.Should().Be("Country.TimeZone",
			because: "the clear child rule must compare against the resolved source lookup path that validation approved");
		clearCondition.RightExpression!.Path.Should().Be("City.Country.TimeZone",
			because: "the clear child rule should compare the resolved source lookup path to the resolved target lookup path");
	}

	[Test]
	[Category("Unit")]
	[Description("Normalizes whitespace around apply-filter relative paths before persisting parent and child metadata.")]
	public void ToEntityMetadata_Should_Trim_ApplyFilter_Relative_Paths_Before_Persistence() {
		// Arrange
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["Country"] = new("Country", "Lookup", "Country"),
				["City"] = new("City", "Lookup", "City"),
				["City.Country"] = new("City.Country", "Lookup", "Country")
			};
		BusinessRule rule = new(
			"Filter city by country",
			new BusinessRuleConditionGroup("AND", []),
			[
				new ApplyFilterBusinessRuleAction(
					"City",
					" Country ",
					"Country",
					"  ",
					clearValue: true,
					populateValue: true)
			]);

		// Act
		IReadOnlyList<BusinessRuleMetadataDto> metadata = SimpleToFullBusinessRuleConverter.ToEntityMetadata(attributeMap, rule, "UsrOrder");

		// Assert
		BusinessRuleFilterLookupActionMetadataDto parentAction = metadata[0].Cases.Single().Actions.Single()
			.Should().BeOfType<BusinessRuleFilterLookupActionMetadataDto>(
				because: "the parent apply-filter rule should still persist the filter lookup action").Subject;
		parentAction.LeftExpression.FilterExpression.Should().Be("Country",
			because: "the converter should trim surrounding whitespace from targetFilterPath before saving metadata");
		parentAction.RightExpression.FilterExpression.Should().Be("null",
			because: "blank sourceFilterPath values should normalize to the null sentinel");
		BusinessRuleConditionMetadataDto clearCondition = metadata[1].Cases.Single().Condition!
			.Should().BeOfType<BusinessRuleConditionMetadataDto>(
				because: "the autogenerated clear child should still persist a direct condition").Subject;
		clearCondition.RightExpression!.Path.Should().Be("City.Country",
			because: "the child rule should build the resolved target comparison path from the trimmed targetFilterPath");
	}

	[Test]
	[Category("Unit")]
	[Description("Preserves a user-authored apply-filter parent condition for the Supervisor-created ninja regression scenario.")]
	public void ToEntityMetadata_Should_Preserve_ApplyFilter_Parent_Condition_For_Supervisor_Created_Ninja() {
		// Arrange
		string supervisorId = "4055a3b6-867e-3756-311a-576cbaba3230";
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["CreatedBy"] = new("CreatedBy", "Lookup", "Contact"),
				["Clan"] = new("Clan", "Lookup", "NinjaClan"),
				["Clan.CreatedBy"] = new("Clan.CreatedBy", "Lookup", "Contact")
			};
		BusinessRule rule = new(
			"Filter clan by Supervisor-created ninja",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "CreatedBy"),
						"equal",
						new BusinessRuleExpression("Const", value: JsonSerializer.SerializeToElement(supervisorId)))
				]),
			[
				new ApplyFilterBusinessRuleAction(
					"Clan",
					"CreatedBy",
					"CreatedBy",
					null,
					clearValue: true,
					populateValue: false)
			]);

		// Act
		IReadOnlyList<BusinessRuleMetadataDto> metadata = SimpleToFullBusinessRuleConverter.ToEntityMetadata(attributeMap, rule, "Ninja");

		// Assert
		BusinessRuleMetadataDto parentRule = metadata[0];
		BusinessRuleGroupConditionMetadataDto parentConditionGroup = parentRule.Cases.Single().Condition!
			.Should().BeOfType<BusinessRuleGroupConditionMetadataDto>(
				because: "apply-filter parent rules should preserve the top-level user condition group").Subject;
		parentConditionGroup.Conditions.Should().ContainSingle(
			because: "the reported regression had exactly one Supervisor condition that must survive conversion");
		BusinessRuleConditionMetadataDto condition = parentConditionGroup.Conditions.Single();
		condition.LeftExpression.Path.Should().Be("CreatedBy",
			because: "the preserved condition should still compare the CreatedBy attribute");
		condition.RightExpression.Should().NotBeNull(
			because: "the preserved condition should still compare against the Supervisor constant");
		condition.RightExpression!.Value.Should().NotBeNull(
			because: "the preserved condition should still carry the Supervisor constant payload");
		condition.RightExpression!.Value!.ToString().Should().Be(supervisorId,
			because: "the preserved condition should keep the Supervisor identifier from the original payload");
		parentRule.Triggers.Select(trigger => (trigger.Type, trigger.Name)).Should().Contain(
				(BusinessRuleConstants.ChangeAttributeValueTriggerType, "CreatedBy"),
			because: "the preserved parent condition should keep the parent rule reactive to CreatedBy changes");
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
		Action act = () => SimpleToFullBusinessRuleConverter.ToMetadata(columnMap, rule, "UsrTask");

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Unknown attribute 'MissingScore' in rule.actions[*].items[*].value.expression formula.",
				because: "formula translation should resolve source fields before metadata is persisted");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects formula assignments that use DateTime source attributes before metadata is persisted.")]
	public void ToMetadata_Should_Reject_SetValues_Formula_With_DateTime_Source_Attribute() {
		// Arrange
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("StartDate", 7),
			CreateColumn("EndDate", 7),
			CreateColumn("NumberOfDays", 4));
		BusinessRule rule = CreateFormulaRule("NumberOfDays", "EndDate - StartDate + 1");

		// Act
		Action act = () => SimpleToFullBusinessRuleConverter.ToMetadata(columnMap, rule, "UsrLeaveRequest");

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Formula source attribute 'EndDate' has type DateTime. Formula set-values supports only numeric source attributes.",
				because: "metadata generation should not allow DateTime arithmetic that local formula validation rejects");
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
		Action act = () => SimpleToFullBusinessRuleConverter.ToMetadata(columnMap, rule, "UsrTask");

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
		Action act = () => SimpleToFullBusinessRuleConverter.ToMetadata(columnMap, rule, "UsrTask");

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
		Action act = () => SimpleToFullBusinessRuleConverter.ToMetadata(columnMap, rule, "UsrTask");

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
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToMetadata(columnMap, rule, "UsrTask");
		string json = JsonSerializer.Serialize(metadata, BusinessRuleConstants.JsonOptions);

		// Assert
		BusinessRuleGroupConditionMetadataDto conditionGroup = metadata.Cases[0].Condition!
			.Should().BeOfType<BusinessRuleGroupConditionMetadataDto>(
				because: "standard business rules should still persist grouped case conditions").Subject;
		conditionGroup.LogicalOperation.Should().Be(BusinessRuleConstants.LogicalAnd,
			because: "AND groups should map to the business-rule logical AND constant");
		conditionGroup.Conditions[0].ComparisonType.Should().Be(BusinessRuleConstants.ComparisonEqual,
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
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToMetadata(columnMap, rule, "UsrTask");
		BusinessRuleGroupConditionMetadataDto dateConditionGroup = metadata.Cases[0].Condition!
			.Should().BeOfType<BusinessRuleGroupConditionMetadataDto>(
				because: "standard business rules should still persist grouped case conditions").Subject;
		BusinessRuleExpressionMetadataDto rightExpression = dateConditionGroup.Conditions[0].RightExpression!;
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
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToMetadata(columnMap, rule, "UsrTask");
		BusinessRuleGroupConditionMetadataDto timeConditionGroup = metadata.Cases[0].Condition!
			.Should().BeOfType<BusinessRuleGroupConditionMetadataDto>(
				because: "standard business rules should still persist grouped case conditions").Subject;
		BusinessRuleExpressionMetadataDto rightExpression = timeConditionGroup.Conditions[0].RightExpression!;
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

	[Test]
	[Category("Unit")]
	[Description("Honors the caller-supplied rule name (trimmed) and explicit enabled=false flag instead of generating a name and defaulting to enabled.")]
	public void ToMetadata_Should_Honor_Rule_Name_And_Enabled() {
		// Arrange
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1));
		BusinessRule rule = new(
			"Named rule",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Status", null),
						"equal",
						new BusinessRuleExpression("Const", null, Json("Draft")))
				]),
			[
				new MakeReadOnlyBusinessRuleAction(["Status"])
			]) {
			Name = " BusinessRule_named ",
			Enabled = false
		};

		// Act
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToMetadata(columnMap, rule, "UsrTask");

		// Assert
		metadata.Name.Should().Be("BusinessRule_named",
			because: "the caller-supplied rule name must be trimmed and persisted as the match key");
		metadata.Enabled.Should().BeFalse(
			because: "the caller's explicit enabled=false intent must be persisted instead of the default");
	}

	[Test]
	[Category("Unit")]
	[Description("Generates an internal rule name when the caller-supplied name is whitespace.")]
	public void ToMetadata_Should_Generate_Name_When_Name_Is_Whitespace() {
		// Arrange
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1));
		BusinessRule rule = new(
			"Whitespace name",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Status", null),
						"equal",
						new BusinessRuleExpression("Const", null, Json("Draft")))
				]),
			[
				new MakeReadOnlyBusinessRuleAction(["Status"])
			]) {
			Name = "   "
		};

		// Act
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToMetadata(columnMap, rule, "UsrTask");

		// Assert
		metadata.Name.Should().StartWith("BusinessRule_",
			because: "a whitespace name is treated as omitted and a fresh internal name is generated");
	}

	[Test]
	[Category("Unit")]
	[Description("Honors valid caller-supplied block uIds on conditions, expressions, actions, and set-value items, normalizing them to canonical lowercase GUID form.")]
	public void ToMetadata_Should_Honor_Valid_Caller_Block_UIds() {
		// Arrange
		const string conditionUId = "AAAAAAAA-0000-0000-0000-000000000001";
		const string leftExpressionUId = "AAAAAAAA-0000-0000-0000-000000000002";
		const string rightExpressionUId = "AAAAAAAA-0000-0000-0000-000000000003";
		const string readOnlyActionUId = "AAAAAAAA-0000-0000-0000-000000000004";
		const string setValuesActionUId = "AAAAAAAA-0000-0000-0000-000000000005";
		const string setValueItemUId = "AAAAAAAA-0000-0000-0000-000000000006";
		const string itemExpressionUId = "AAAAAAAA-0000-0000-0000-000000000007";
		const string itemValueUId = "AAAAAAAA-0000-0000-0000-000000000008";
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1),
			CreateColumn("Amount", 6));
		BusinessRule rule = new(
			"Stable identity",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Status", null) { UId = leftExpressionUId },
						"equal",
						new BusinessRuleExpression("Const", null, Json("Draft")) { UId = rightExpressionUId }) {
						UId = conditionUId
					}
				]),
			[
				new MakeReadOnlyBusinessRuleAction(["Status"]) { UId = readOnlyActionUId },
				new SetValuesBusinessRuleAction([
					new BusinessRuleSetValueItem(
						new BusinessRuleExpression("AttributeValue", "Amount", null) { UId = itemExpressionUId },
						new BusinessRuleExpression("Const", null, Json(5)) { UId = itemValueUId }) {
						UId = setValueItemUId
					}
				]) { UId = setValuesActionUId }
			]);

		// Act
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToMetadata(columnMap, rule, "UsrTask");

		// Assert
		BusinessRuleGroupConditionMetadataDto group =
			(BusinessRuleGroupConditionMetadataDto)metadata.Cases[0].Condition!;
		group.Conditions[0].UId.Should().Be(conditionUId.ToLowerInvariant(),
			because: "the caller-supplied condition uId must be honored (normalized to canonical GUID form)");
		group.Conditions[0].LeftExpression.UId.Should().Be(leftExpressionUId.ToLowerInvariant(),
			because: "the caller-supplied left expression uId must be honored");
		group.Conditions[0].RightExpression!.UId.Should().Be(rightExpressionUId.ToLowerInvariant(),
			because: "the caller-supplied right expression uId must be honored");
		metadata.Cases[0].Actions[0].UId.Should().Be(readOnlyActionUId.ToLowerInvariant(),
			because: "the caller-supplied field-selection action uId must be honored");
		metadata.Cases[0].Actions[1].UId.Should().Be(setValuesActionUId.ToLowerInvariant(),
			because: "the caller-supplied set-values action uId must be honored");
		List<BusinessRuleSetValueItemMetadataDto> items =
			(List<BusinessRuleSetValueItemMetadataDto>)((FieldSelectionBusinessRuleActionMetadataDto)metadata.Cases[0].Actions[1]).Items!;
		items[0].UId.Should().Be(setValueItemUId.ToLowerInvariant(),
			because: "the caller-supplied set-value item uId must be honored");
		items[0].Expression.UId.Should().Be(itemExpressionUId.ToLowerInvariant(),
			because: "the caller-supplied item target expression uId must be honored");
		items[0].Value.UId.Should().Be(itemValueUId.ToLowerInvariant(),
			because: "the caller-supplied item value expression uId must be honored");
	}

	[Test]
	[Category("Unit")]
	[Description("Throws when a caller-supplied block uId is not a valid GUID instead of persisting a broken block identity.")]
	public void ToMetadata_Should_Throw_When_Block_UId_Is_Not_A_Guid() {
		// Arrange
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = CreateColumnMap(
			CreateColumn("Status", 1));
		BusinessRule rule = new(
			"Broken identity",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Status", null),
						"equal",
						new BusinessRuleExpression("Const", null, Json("Draft"))) {
						UId = "not-a-guid"
					}
				]),
			[
				new MakeReadOnlyBusinessRuleAction(["Status"])
			]);

		// Act
		Action act = () => SimpleToFullBusinessRuleConverter.ToMetadata(columnMap, rule, "UsrTask");

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("Block uId 'not-a-guid' is not a valid GUID.",
				because: "a malformed caller-supplied block uId must fail conversion instead of persisting a broken identity");
	}

	private const string ExistingRuleUId = "11111111-1111-1111-1111-111111111111";
	private const string ExistingCaseUId = "22222222-2222-2222-2222-222222222222";
	private const string ExistingGroupUId = "33333333-3333-3333-3333-333333333333";

	[Test]
	[Category("Unit")]
	[Description("Produces the updated rule with the existing persisted rule, case, and top-level group-condition uIds when an existingRule is supplied, discarding the throwaway generated identities.")]
	public void ToEntityMetadata_Should_Carry_Existing_Rule_Case_And_Group_UIds_When_ExistingRule_Provided() {
		// Arrange
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["Status"] = new("Status", "Text", null)
			};
		BusinessRule rule = CreateStandardRule();
		JsonObject existingRule = CreateExistingRule();

		// Act
		IReadOnlyList<BusinessRuleMetadataDto> metadata = SimpleToFullBusinessRuleConverter.ToEntityMetadata(
			attributeMap, rule, "UsrTask", filterSchemaProvider: null, lookupValueResolver: null, existingRule);

		// Assert
		metadata[0].UId.Should().Be(ExistingRuleUId,
			because: "the replacement rule must carry the persisted rule uId produced during construction");
		metadata[0].Cases[0].UId.Should().Be(ExistingCaseUId,
			because: "the single generated case must carry the persisted case uId");
		metadata[0].Cases[0].Condition!.UId.Should().Be(ExistingGroupUId,
			because: "the top-level group condition must carry the persisted group uId");
	}

	[Test]
	[Category("Unit")]
	[Description("Mints fresh rule, case, and group uIds on the create path when no existingRule is supplied.")]
	public void ToEntityMetadata_Should_Mint_Fresh_UIds_When_No_ExistingRule() {
		// Arrange
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["Status"] = new("Status", "Text", null)
			};
		BusinessRule rule = CreateStandardRule();

		// Act
		IReadOnlyList<BusinessRuleMetadataDto> metadata = SimpleToFullBusinessRuleConverter.ToEntityMetadata(
			attributeMap, rule, "UsrTask", filterSchemaProvider: null, lookupValueResolver: null);

		// Assert
		metadata[0].UId.Should().NotBe(ExistingRuleUId,
			because: "the create path mints a fresh rule uId instead of reusing a persisted one");
		Guid.TryParse(metadata[0].UId, out _).Should().BeTrue(
			because: "the freshly minted rule uId must be a valid GUID");
	}

	[Test]
	[Category("Unit")]
	[Description("Carries the existing trigger uIds onto generated triggers matched by case-insensitive name plus type, keeps fresh uIds where nothing matches, and consumes each existing trigger uId at most once.")]
	public void ToEntityMetadata_Should_Carry_Existing_Trigger_UIds_Matched_By_Name_And_Type() {
		// Arrange
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["Status"] = new("Status", "Text", null)
			};
		BusinessRule rule = CreateStandardRule();
		JsonObject existingRule = CreateExistingRule(triggersJson: $$"""
			[
			  { "typeName": "Trigger", "uId": "44444444-4444-4444-4444-444444444444", "name": "STATUS", "type": 0 },
			  { "typeName": "Trigger", "uId": "55555555-5555-5555-5555-555555555555", "name": "", "type": 2 }
			]
			""");

		// Act
		IReadOnlyList<BusinessRuleMetadataDto> metadata = SimpleToFullBusinessRuleConverter.ToEntityMetadata(
			attributeMap, rule, "UsrTask", filterSchemaProvider: null, lookupValueResolver: null, existingRule);

		// Assert
		BusinessRuleTriggerMetadataDto changeTrigger = metadata[0].Triggers.Single(trigger =>
			trigger.Type == BusinessRuleConstants.ChangeAttributeValueTriggerType && trigger.Name == "Status");
		changeTrigger.UId.Should().Be("44444444-4444-4444-4444-444444444444",
			because: "a change trigger matching by case-insensitive name and type must carry the persisted trigger uId");
		BusinessRuleTriggerMetadataDto dataLoadedTrigger = metadata[0].Triggers.Single(trigger =>
			trigger.Type == BusinessRuleConstants.DataLoadedTriggerType);
		dataLoadedTrigger.UId.Should().Be("55555555-5555-5555-5555-555555555555",
			because: "the DataLoaded trigger (empty name, type 2) must carry the persisted trigger uId");
	}

	[Test]
	[Category("Unit")]
	[Description("Matches a persisted change trigger whose zero-valued 'type' property was stripped by the platform's addon-metadata normalization, treating an absent type as ChangeAttributeValue (0).")]
	public void ToEntityMetadata_Should_Match_Change_Trigger_When_Persisted_Type_Property_Is_Absent() {
		// Arrange - Creatio omits zero-valued properties on read-back, so change triggers (type 0)
		// come back without a "type" property at all.
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["Status"] = new("Status", "Text", null)
			};
		BusinessRule rule = CreateStandardRule();
		JsonObject existingRule = CreateExistingRule(triggersJson: """
			[
			  { "typeName": "Trigger", "uId": "44444444-4444-4444-4444-444444444444", "name": "Status" }
			]
			""");

		// Act
		IReadOnlyList<BusinessRuleMetadataDto> metadata = SimpleToFullBusinessRuleConverter.ToEntityMetadata(
			attributeMap, rule, "UsrTask", filterSchemaProvider: null, lookupValueResolver: null, existingRule);

		// Assert
		metadata[0].Triggers.Single(trigger => trigger.Name == "Status").UId
			.Should().Be("44444444-4444-4444-4444-444444444444",
				because: "a persisted change trigger stripped of its zero-valued type must still match a generated type-0 trigger");
	}

	[Test]
	[Category("Unit")]
	[Description("Throws when the existing persisted rule carries no uId, because identity cannot be preserved without it.")]
	public void ToEntityMetadata_Should_Throw_When_Existing_Rule_Has_No_UId() {
		// Arrange
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["Status"] = new("Status", "Text", null)
			};
		BusinessRule rule = CreateStandardRule();
		JsonObject existingRule = (JsonObject)JsonNode.Parse("""{ "name": "Rule_A", "enabled": true }""")!;

		// Act
		Action act = () => SimpleToFullBusinessRuleConverter.ToEntityMetadata(
			attributeMap, rule, "UsrTask", filterSchemaProvider: null, lookupValueResolver: null, existingRule);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("Existing business rule has no uId.",
				because: "a persisted rule without a uId cannot donate its identity to the replacement");
	}

	[Test]
	[Category("Unit")]
	[Description("Builds apply-filter child rules anchored to the existing parent uId from the start, so child parentUId, autogenerated names, and captions embed the persisted uId without a post-conversion re-anchor.")]
	public void ToEntityMetadata_Should_Anchor_ApplyFilter_Children_To_Existing_UId_When_ExistingRule_Provided() {
		// Arrange
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["Country"] = new("Country", "Lookup", "Country"),
				["City"] = new("City", "Lookup", "City"),
				["City.Country"] = new("City.Country", "Lookup", "Country")
			};
		BusinessRule rule = new(
			"Filter city by country",
			new BusinessRuleConditionGroup("AND", []),
			[
				new ApplyFilterBusinessRuleAction(
					"City",
					"Country",
					"Country",
					null,
					clearValue: true,
					populateValue: true)
			]);
		JsonObject existingRule = CreateExistingRule();

		// Act
		IReadOnlyList<BusinessRuleMetadataDto> metadata = SimpleToFullBusinessRuleConverter.ToEntityMetadata(
			attributeMap, rule, "UsrOrder", filterSchemaProvider: null, lookupValueResolver: null, existingRule);

		// Assert
		metadata[0].UId.Should().Be(ExistingRuleUId,
			because: "the apply-filter parent rule must carry the persisted rule uId");
		metadata.Skip(1).Should().OnlyContain(child => child.ParentUId == ExistingRuleUId,
			because: "every apply-filter child must be anchored to the existing parent uId from construction");
		metadata[1].Name.Should().Be($"Autogenerated_{ExistingRuleUId}_ClearValue",
			because: "the parent uId embedded in the autogenerated clear child name must be the persisted uId");
		metadata[1].Caption.Should().Be($"ChildRule-{ExistingRuleUId}-ClearValue",
			because: "the parent uId embedded in the autogenerated clear child caption must be the persisted uId");
	}

	[Test]
	[Category("Unit")]
	[Description("Carries the existing rule, case, and group uIds onto a page rule when an existingRule is supplied.")]
	public void ToPageMetadata_Should_Carry_Existing_UIds_When_ExistingRule_Provided() {
		// Arrange
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["PDS_Status"] = new("PDS_Status", "Text", null)
			};
		BusinessRule rule = new(
			"Change page element state",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "PDS_Status", null),
						"is-filled-in")
				]),
			[
				new ShowElementBusinessRuleAction(["NameInput"])
			]);
		JsonObject existingRule = CreateExistingRule();

		// Act
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToPageMetadata(attributeMap, rule, existingRule);

		// Assert
		metadata.UId.Should().Be(ExistingRuleUId,
			because: "the page replacement rule must carry the persisted rule uId");
		metadata.Cases[0].UId.Should().Be(ExistingCaseUId,
			because: "the single page case must carry the persisted case uId");
		metadata.Cases[0].Condition!.UId.Should().Be(ExistingGroupUId,
			because: "the top-level page group condition must carry the persisted group uId");
	}

	private static BusinessRule CreateStandardRule() =>
		new(
			"Readonly status",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", "Status", null),
						"equal",
						new BusinessRuleExpression("Const", null, Json("Draft")))
				]),
			[
				new MakeReadOnlyBusinessRuleAction(["Status"])
			]) {
			Name = "Rule_A"
		};

	private static JsonObject CreateExistingRule(bool enabled = true, string triggersJson = "[]") =>
		(JsonObject)JsonNode.Parse($$"""
			{
			  "typeName": "{{BusinessRuleConstants.BusinessRuleTypeName}}",
			  "uId": "{{ExistingRuleUId}}",
			  "name": "Rule_A",
			  "enabled": {{(enabled ? "true" : "false")}},
			  "caption": "Rule A",
			  "cases": [
			    {
			      "typeName": "{{BusinessRuleConstants.BusinessRuleCaseTypeName}}",
			      "uId": "{{ExistingCaseUId}}",
			      "condition": {
			        "typeName": "{{BusinessRuleConstants.BusinessRuleGroupConditionTypeName}}",
			        "uId": "{{ExistingGroupUId}}",
			        "logicalOperation": 1,
			        "conditions": []
			      },
			      "actions": []
			    }
			  ],
			  "triggers": {{triggersJson}}
			}
			""")!;

	[Test]
	[Category("Unit")]
	[Description("Emits a BusinessRuleSysSettingExpression carrying the resolved data value type and setting code, and the compared Const inherits that type.")]
	public void ToMetadata_Should_Emit_SysSetting_Expression_With_Resolved_Type() {
		// Arrange
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal);
		IReadOnlyDictionary<string, SysSettingOperandDescriptor> sysSettingMap =
			new Dictionary<string, SysSettingOperandDescriptor>(StringComparer.Ordinal) {
				["DisableEquipmentDelivery"] = new("DisableEquipmentDelivery", "Boolean", null)
			};
		BusinessRule rule = new(
			"Hide shipping address when equipment delivery is disabled",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("SysSetting", sysSettingName: "DisableEquipmentDelivery"),
						"equal",
						new BusinessRuleExpression("Const", null, JsonRaw("true")))
				]),
			[
				new MakeReadOnlyBusinessRuleAction(["Status"])
			]);

		// Act
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToMetadata(attributeMap, rule, sysSettingMap);

		// Assert
		BusinessRuleConditionMetadataDto condition =
			((BusinessRuleGroupConditionMetadataDto)metadata.Cases[0].Condition!).Conditions[0];
		condition.LeftExpression.TypeName.Should().Be(BusinessRuleConstants.BusinessRuleSysSettingExpressionTypeName,
			because: "a SysSetting operand must serialize as the platform BusinessRuleSysSettingExpression");
		condition.LeftExpression.Type.Should().Be(BusinessRuleConstants.SysSettingExpressionType,
			because: "the expression type discriminator must be SysSetting");
		condition.LeftExpression.SysSettingName.Should().Be("DisableEquipmentDelivery",
			because: "the setting code must round-trip into the persisted metadata");
		condition.LeftExpression.DataValueTypeName.Should().Be("Boolean",
			because: "the setting's data value type resolved from the environment must be persisted on the operand");
		condition.RightExpression!.DataValueTypeName.Should().Be("Boolean",
			because: "a Const operand inherits its data value type from the SysSetting it is compared against");
	}

	[Test]
	[Category("Unit")]
	[Description("Emits a SysSetting expression on the RIGHT side and lets a Const on the LEFT inherit the setting's resolved type.")]
	public void ToMetadata_Should_Emit_Right_SysSetting_And_Const_Inherits_Type() {
		// Arrange
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal);
		IReadOnlyDictionary<string, SysSettingOperandDescriptor> sysSettingMap =
			new Dictionary<string, SysSettingOperandDescriptor>(StringComparer.Ordinal) {
				["DisableEquipmentDelivery"] = new("DisableEquipmentDelivery", "Boolean", null)
			};
		BusinessRule rule = new(
			"Const on left, setting on right",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("Const", null, JsonRaw("true")),
						"equal",
						new BusinessRuleExpression("SysSetting", sysSettingName: "DisableEquipmentDelivery"))
				]),
			[
				new MakeReadOnlyBusinessRuleAction(["Status"])
			]);

		// Act
		BusinessRuleMetadataDto metadata = SimpleToFullBusinessRuleConverter.ToMetadata(attributeMap, rule, sysSettingMap);

		// Assert
		BusinessRuleConditionMetadataDto condition =
			((BusinessRuleGroupConditionMetadataDto)metadata.Cases[0].Condition!).Conditions[0];
		condition.RightExpression!.TypeName.Should().Be(BusinessRuleConstants.BusinessRuleSysSettingExpressionTypeName,
			because: "a SysSetting operand must serialize as a SysSetting expression on the right side too");
		condition.LeftExpression.DataValueTypeName.Should().Be("Boolean",
			because: "a Const on the left inherits its data value type from the SysSetting it is compared against");
	}

	[Test]
	[Category("Unit")]
	[Description("Throws when a SysSetting condition operand has no resolved descriptor in the sys-setting map.")]
	public void ToMetadata_Should_Throw_When_SysSetting_Is_Unresolved() {
		// Arrange
		IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> attributeMap =
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal);
		BusinessRule rule = new(
			"Unresolved setting",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("SysSetting", sysSettingName: "MissingSetting"),
						"equal",
						new BusinessRuleExpression("Const", null, JsonRaw("true")))
				]),
			[
				new MakeReadOnlyBusinessRuleAction(["Status"])
			]);

		// Act
		Action act = () => SimpleToFullBusinessRuleConverter.ToMetadata(attributeMap, rule);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*was not resolved*",
				because: "an unresolved SysSetting operand cannot be typed and must not silently serialize as Text");
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

