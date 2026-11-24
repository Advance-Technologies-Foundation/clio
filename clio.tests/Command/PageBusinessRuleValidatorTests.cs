using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command.BusinessRules;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class PageBusinessRuleValidatorTests {
	[Test]
	[Category("Unit")]
	[Description("Accepts a datasource-scoped left condition path that resolves to a data source column not surfaced on the page.")]
	public void Validate_Should_Accept_Left_Datasource_Scoped_Path() {
		// Arrange
		BusinessRule rule = CreatePageRule(
			leftPath: "PDS.CreatedOn");

		// Act
		Action act = () => CreateValidator().Validate(rule, CreateScopedAttributeMap(), CreateElementNames());

		// Assert
		act.Should().NotThrow(
			because: "a page condition may reference a data source column that is not on the page via the '<dataSource>.<column>' path");
	}

	[Test]
	[Category("Unit")]
	[Description("Accepts a datasource-scoped right condition path when both operands resolve to the same data value type.")]
	public void Validate_Should_Accept_Right_Datasource_Scoped_Path() {
		// Arrange
		BusinessRule rule = CreatePageRule(
			rightExpression: new BusinessRuleExpression("AttributeValue", "PDS.UsrText"));

		// Act
		Action act = () => CreateValidator().Validate(rule, CreateScopedAttributeMap(), CreateElementNames());

		// Assert
		act.Should().NotThrow(
			because: "a right-side page condition operand may also reference a data-source-scoped column not on the page");
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
	[Description("Explains a rejected page condition attribute with the provider's reason, or as not declared when the provider never saw it, redirects to an entity rule or a page handler, and keeps the candidate list.")]
	[TestCase("UsrVirtualFlag", "'UsrVirtualFlag' is a virtual view-model attribute.")]
	[TestCase("UsrMissing", "'UsrMissing' is not declared in bundle.viewModelConfig.attributes and is not a '<data source>.<column>' path of a page data source.")]
	public void Validate_Should_Explain_Rejected_Condition_Attribute(string path, string expectedReason) {
		// Arrange
		BusinessRule rule = CreatePageRule(leftPath: path);
		Dictionary<string, string> rejected = new(StringComparer.Ordinal) {
			["UsrVirtualFlag"] = "'UsrVirtualFlag' is a virtual view-model attribute."
		};

		// Act
		Action act = () => CreateValidator().Validate(
			rule, CreateAttributeMap(), CreateElementNames(), rejectedAttributes: rejected);

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage($"Unknown or unsupported datasource-bound page attribute '{path}' in rule.condition.conditions[*].leftExpression.path. {expectedReason} {PageBusinessRuleValidator.UnsupportedAttributeRedirect} Available condition attributes: PDS_Name, PDS_Status.",
				because: "the agent must learn why the attribute is not a condition source and where the condition belongs, instead of falling back to a view-config binding");
	}

	[Test]
	[Category("Unit")]
	[Description("Keeps the message without a reason or redirect when no rejected-attribute map is supplied.")]
	public void Validate_Should_Not_Explain_Rejected_Attribute_Without_Rejections() {
		// Arrange
		BusinessRule rule = CreatePageRule(leftPath: "UsrVirtualFlag");

		// Act
		Action act = () => CreateValidator().Validate(rule, CreateAttributeMap(), CreateElementNames());

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Unknown or unsupported datasource-bound page attribute 'UsrVirtualFlag' in rule.condition.conditions[*].leftExpression.path. Available condition attributes: PDS_Name, PDS_Status.",
				because: "without the provider's rejections there is nothing to explain the attribute by");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects an apply-static-filter action on a page with a redirect to create-entity-business-rules.")]
	public void Validate_Should_Redirect_Page_Apply_Static_Filter_To_Entity_Rule() {
		// Arrange
		BusinessRule rule = new(
			"Filter owners",
			new BusinessRuleConditionGroup("AND", []),
			[new ApplyStaticFilterBusinessRuleAction("UsrOwner", JsonDocument.Parse("{}").RootElement)]);

		// Act
		Action act = () => CreateValidator().Validate(rule, CreateAttributeMap(), CreateElementNames());

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage($"Unsupported rule.actions[*].type 'apply-static-filter'. Supported values: hide-element, show-element, make-editable, make-read-only, make-required, make-optional. {PageBusinessRuleValidator.EntityOnlyLookupFilterRedirect}*",
				because: "Freedom UI page rules have no lookup-filter action, so the agent must be sent to the entity rule tool");
	}

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

	private static IReadOnlyDictionary<string, BusinessRuleAttributeDescriptor> CreateScopedAttributeMap() =>
		new Dictionary<string, BusinessRuleAttributeDescriptor>(StringComparer.Ordinal) {
			["PDS_Name"] = new("PDS_Name", "Text", null),
			["PDS.CreatedOn"] = new("CreatedOn", "DateTime", null, "PDS"),
			["PDS.UsrText"] = new("UsrText", "Text", null, "PDS")
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

	private static PageBusinessRuleValidator CreateValidator() =>
		new(new BusinessRuleValidator(Substitute.For<IBusinessRuleLookupReferenceValidator>()));
}
