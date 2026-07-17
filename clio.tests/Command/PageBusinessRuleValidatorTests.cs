using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command;
using Clio.Command.BusinessRules;
using Clio.Command.EntitySchemaDesigner;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class PageBusinessRuleValidatorTests {
	[Test]
	[Category("Unit")]
	[Description("Rejects datasource paths in left page condition expressions before shared attribute validation.")]
	public void Validate_Should_Reject_Left_Datasource_Path() {
		// Arrange
		BusinessRule rule = CreatePageRule(
			leftPath: "PDS.Name");

		// Act
		Action act = () => CreateValidator().Validate(rule, CreateAttributeMap(), CreateElementNames());

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.condition.conditions[*].leftExpression.path must use the declared page attribute name from bundle.viewModelConfig.attributes, not datasource path 'PDS.Name'.*",
				because: "page rules must target declared view-model attributes rather than datasource paths");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects datasource paths in right page condition attribute expressions before shared attribute validation.")]
	public void Validate_Should_Reject_Right_Datasource_Path() {
		// Arrange
		BusinessRule rule = CreatePageRule(
			rightExpression: new BusinessRuleExpression("AttributeValue", "PDS.Status"));

		// Act
		Action act = () => CreateValidator().Validate(rule, CreateAttributeMap(), CreateElementNames());

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.condition.conditions[*].rightExpression.path must use the declared page attribute name from bundle.viewModelConfig.attributes, not datasource path 'PDS.Status'.*",
				because: "right-side page attribute comparisons must also use declared page attribute names");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts supported page field-state actions when every target exists in the page viewConfig.")]
	[TestCase("make-editable")]
	[TestCase("make-read-only")]
	[TestCase("make-required")]
	[TestCase("make-optional")]
	public void Validate_Should_Accept_Page_Field_State_Action_Type(string actionType) {
		// Arrange
		BusinessRule rule = CreatePageRule(
			action: CreateAction(actionType, ["NameInput"]));

		// Act
		Action act = () => CreateValidator().Validate(rule, CreateAttributeMap(), CreateElementNames());

		// Assert
		act.Should().NotThrow(
			because: "page business rules should allow editable read-only required and optional actions for named page elements");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects object-only or otherwise unsupported actions for page business rules.")]
	public void Validate_Should_Reject_Unsupported_Page_Action_Type() {
		// Arrange
		BusinessRule rule = CreatePageRule(
			action: new SetValuesBusinessRuleAction([
				new BusinessRuleSetValueItem(
					new BusinessRuleExpression("AttributeValue", "NameInput"),
					new BusinessRuleExpression("Const", value: default))
			]));

		// Act
		Action act = () => CreateValidator().Validate(rule, CreateAttributeMap(), CreateElementNames());

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Unsupported rule.actions[*].type 'set-values'. Supported values: hide-element, show-element, make-editable, make-read-only, make-required, make-optional. Available page elements: NameInput, StatusInput.",
				because: "page business rules should reject unsupported action types with the full page action allow-list");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects empty page element names in page show or hide actions.")]
	public void Validate_Should_Reject_Blank_Page_Element_Item() {
		// Arrange
		BusinessRule rule = CreatePageRule(
			action: new HideElementBusinessRuleAction([" "]));

		// Act
		Action act = () => CreateValidator().Validate(rule, CreateAttributeMap(), CreateElementNames());

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.actions[*].items cannot contain empty page element names. Available page elements: NameInput, StatusInput.",
				because: "blank page element action targets cannot be converted into valid add-on metadata");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects unknown page element names and includes candidate element names.")]
	public void Validate_Should_Reject_Unknown_Page_Element_And_Show_Candidates() {
		// Arrange
		BusinessRule rule = CreatePageRule(
			action: new ShowElementBusinessRuleAction(["MissingInput"]));

		// Act
		Action act = () => CreateValidator().Validate(rule, CreateAttributeMap(), CreateElementNames());

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Unknown page element 'MissingInput' in rule.actions[*].items. Available page elements: NameInput, StatusInput.",
				because: "validation errors should help callers choose a real named page element from viewConfig");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects unknown page condition attributes and includes candidate page attribute names.")]
	public void Validate_Should_Reject_Unknown_Page_Condition_Attribute_And_Show_Candidates() {
		// Arrange
		BusinessRule rule = CreatePageRule(
			leftPath: "MissingAttribute");

		// Act
		Action act = () => CreateValidator().Validate(rule, CreateAttributeMap(), CreateElementNames());

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Unknown or unsupported datasource-bound page attribute 'MissingAttribute' in rule.condition.conditions[*].leftExpression.path. Available condition attributes: PDS_Name, PDS_Status.",
				because: "validation errors should help callers choose a supported datasource-bound page attribute");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts a DataSource-field condition operand (scopeId = datasource name) when the feature is enabled.")]
	public void Validate_Should_Accept_DataSource_Field_When_Feature_Enabled() {
		// Arrange
		BusinessRule rule = CreateScopedRule(new BusinessRuleCondition(
			new BusinessRuleExpression("AttributeValue", "Contact", scopeId: "PDS"),
			"is-filled-in"));

		// Act
		Action act = () => CreateValidator(conditionSourcesEnabled: true)
			.Validate(rule, CreateScopedMap(), CreateElementNames());

		// Assert
		act.Should().NotThrow(
			because: "a DataSource column addressed by scopeId is a valid page condition source when the feature is on");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts a page-parameter operand compared to a constant when the feature is enabled.")]
	public void Validate_Should_Accept_Page_Parameter_Compared_To_Const_When_Feature_Enabled() {
		// Arrange
		BusinessRule rule = CreateScopedRule(new BusinessRuleCondition(
			new BusinessRuleExpression("AttributeValue", "RequestType", scopeId: BusinessRuleConstants.PageParametersScope),
			"equal",
			new BusinessRuleExpression("Const", value: Const("Service request"))));

		// Act
		Action act = () => CreateValidator(conditionSourcesEnabled: true)
			.Validate(rule, CreateScopedMap(), CreateElementNames());

		// Assert
		act.Should().NotThrow(
			because: "comparing a page parameter to a boolean/text constant is a stated acceptance criterion");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts a page parameter compared to a system setting when the feature is enabled.")]
	public void Validate_Should_Accept_Page_Parameter_Compared_To_SysSetting_When_Feature_Enabled() {
		// Arrange
		BusinessRule rule = CreateScopedRule(new BusinessRuleCondition(
			new BusinessRuleExpression("AttributeValue", "MaxAmount", scopeId: BusinessRuleConstants.PageParametersScope),
			"greater-than",
			new BusinessRuleExpression("SysSetting", sysSettingName: "MaxOrderAmount")));

		// Act
		Action act = () => CreateValidator(conditionSourcesEnabled: true)
			.Validate(rule, CreateScopedMap(), CreateElementNames());

		// Assert
		act.Should().NotThrow(
			because: "comparing a page parameter to a system setting is a stated acceptance criterion");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects an unknown scopeId with the list of available scopes when the feature is enabled.")]
	public void Validate_Should_Reject_Unknown_Scope_When_Feature_Enabled() {
		// Arrange
		BusinessRule rule = CreateScopedRule(new BusinessRuleCondition(
			new BusinessRuleExpression("AttributeValue", "Foo", scopeId: "NotADataSource"),
			"is-filled-in"));

		// Act
		Action act = () => CreateValidator(conditionSourcesEnabled: true)
			.Validate(rule, CreateScopedMap(), CreateElementNames());

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("*Unknown scopeId 'NotADataSource'*Available scopes: PageParameters, PDS.*",
				because: "an unknown scope should fail early and list the valid scopes");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a scoped condition operand when the feature is disabled, pointing at the feature flag.")]
	public void Validate_Should_Reject_Scope_When_Feature_Disabled() {
		// Arrange
		BusinessRule rule = CreateScopedRule(new BusinessRuleCondition(
			new BusinessRuleExpression("AttributeValue", "Contact", scopeId: "PDS"),
			"is-filled-in"));

		// Act
		Action act = () => CreateValidator().Validate(rule, CreateScopedMap(), CreateElementNames());

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("*scopeId 'PDS' is not supported for this rule*page-business-rule-condition-sources*",
				because: "scoped operands must be rejected while the feature is off, guiding the caller to the flag");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a SysSetting operand when the feature is disabled.")]
	public void Validate_Should_Reject_SysSetting_When_Feature_Disabled() {
		// Arrange
		BusinessRule rule = CreateScopedRule(new BusinessRuleCondition(
			new BusinessRuleExpression("AttributeValue", "PDS_Name"),
			"equal",
			new BusinessRuleExpression("SysSetting", sysSettingName: "MaxOrderAmount")));

		// Act
		Action act = () => CreateValidator().Validate(rule, CreateScopedMap(), CreateElementNames());

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("*'SysSetting' is not supported for this rule*page-business-rule-condition-sources*",
				because: "the SysSetting operand is part of the gated page condition sources");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a cross-scope comparison whose operands resolve to different data value types when the feature is enabled.")]
	public void Validate_Should_Reject_Cross_Scope_Type_Mismatch_When_Feature_Enabled() {
		// Arrange
		BusinessRule rule = CreateScopedRule(new BusinessRuleCondition(
			new BusinessRuleExpression("AttributeValue", "Priority", scopeId: "PDS"),
			"equal",
			new BusinessRuleExpression("AttributeValue", "RequestType", scopeId: BusinessRuleConstants.PageParametersScope)));

		// Act
		Action act = () => CreateValidator(conditionSourcesEnabled: true)
			.Validate(rule, CreateScopedMap(), CreateElementNames());

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("*Both operands must resolve to the same data value type*",
				because: "a Lookup DataSource field and a Text page parameter are not type-compatible");
	}

	private static BusinessRule CreateScopedRule(BusinessRuleCondition condition) =>
		new(
			"Toggle element",
			new BusinessRuleConditionGroup("AND", [condition]),
			[new HideElementBusinessRuleAction(["NameInput"])]);

	private static PageScopedBusinessRuleAttributeMap CreateScopedMap() {
		IEntityBusinessRuleAttributeProvider entityAttributeProvider = Substitute.For<IEntityBusinessRuleAttributeProvider>();
		entityAttributeProvider.GetAttributes("Case", Arg.Any<Guid>()).Returns(new EntityBusinessRuleAttributeContext(
			new EntityDesignSchemaDto(),
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["Contact"] = new("Contact", "Lookup", "Contact"),
				["Priority"] = new("Priority", "Lookup", "CasePriority"),
				["Subject"] = new("Subject", "Text", null)
			}));
		return new PageScopedBusinessRuleAttributeMap(
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["PDS_Name"] = new("PDS_Name", "Text", null)
			},
			new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
				["RequestType"] = new("RequestType", "Text", null),
				["MaxAmount"] = new("MaxAmount", "Integer", null)
			},
			new Dictionary<string, string>(StringComparer.Ordinal) { ["PDS"] = "Case" },
			entityAttributeProvider,
			Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
	}

	private static JsonElement Const(string value) => JsonSerializer.SerializeToElement(value);

	private static BusinessRule CreatePageRule(
		string leftPath = "PDS_Name",
		BusinessRuleExpression? rightExpression = null,
		BusinessRuleAction? action = null) =>
		new(
			"Toggle element",
			new BusinessRuleConditionGroup(
				"AND",
				[
					new BusinessRuleCondition(
						new BusinessRuleExpression("AttributeValue", leftPath),
						rightExpression is null ? "is-filled-in" : "equal",
						rightExpression)
				]),
			[
				action ?? new HideElementBusinessRuleAction(["NameInput"])
			]);

	private static IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> CreateAttributeMap() =>
		new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
			["PDS_Name"] = new("PDS_Name", "Text", null),
			["PDS_Status"] = new("PDS_Status", "Text", null)
		};

	private static IReadOnlySet<string> CreateElementNames() =>
		new HashSet<string>(StringComparer.Ordinal) {
			"NameInput",
			"StatusInput"
		};

	private static BusinessRuleAction CreateAction(string actionType, List<string> items) =>
		actionType switch {
			"make-editable" => new MakeEditableBusinessRuleAction(items),
			"make-read-only" => new MakeReadOnlyBusinessRuleAction(items),
			"make-required" => new MakeRequiredBusinessRuleAction(items),
			"make-optional" => new MakeOptionalBusinessRuleAction(items),
			_ => throw new ArgumentOutOfRangeException(nameof(actionType), actionType, null)
		};

	private static PageBusinessRuleValidator CreateValidator(bool conditionSourcesEnabled = false) {
		IFeatureToggleService featureToggleService = Substitute.For<IFeatureToggleService>();
		featureToggleService
			.IsFeatureEnabled(BusinessRuleConstants.PageConditionSourcesFeatureName)
			.Returns(conditionSourcesEnabled);
		return new PageBusinessRuleValidator(
			new BusinessRuleValidator(Substitute.For<IBusinessRuleLookupReferenceValidator>()),
			featureToggleService);
	}
}
