using System;
using System.Collections.Generic;
using Clio.Command.BusinessRules;
using FluentAssertions;
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
		Action act = () => PageBusinessRuleValidator.Validate(rule, CreateAttributeMap(), CreateElementNames());

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.condition.conditions[*].leftExpression.path must use the declared page attribute name from bundle.viewModelConfig.attributes, not datasource path 'PDS.Name'.",
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
		Action act = () => PageBusinessRuleValidator.Validate(rule, CreateAttributeMap(), CreateElementNames());

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("rule.condition.conditions[*].rightExpression.path must use the declared page attribute name from bundle.viewModelConfig.attributes, not datasource path 'PDS.Status'.",
				because: "right-side page attribute comparisons must also use declared page attribute names");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects entity field-state actions for page business rules.")]
	public void Validate_Should_Reject_Unsupported_Page_Action_Type() {
		// Arrange
		BusinessRule rule = CreatePageRule(
			action: new MakeReadOnlyBusinessRuleAction(["NameInput"]));

		// Act
		Action act = () => PageBusinessRuleValidator.Validate(rule, CreateAttributeMap(), CreateElementNames());

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Unsupported rule.actions[*].type 'make-read-only'. Supported values: hide-element, show-element. Available page elements: NameInput, StatusInput.",
				because: "page business rules support only show/hide element actions in this scope");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects empty page element names in page show or hide actions.")]
	public void Validate_Should_Reject_Blank_Page_Element_Item() {
		// Arrange
		BusinessRule rule = CreatePageRule(
			action: new HideElementBusinessRuleAction([" "]));

		// Act
		Action act = () => PageBusinessRuleValidator.Validate(rule, CreateAttributeMap(), CreateElementNames());

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
		Action act = () => PageBusinessRuleValidator.Validate(rule, CreateAttributeMap(), CreateElementNames());

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
		Action act = () => PageBusinessRuleValidator.Validate(rule, CreateAttributeMap(), CreateElementNames());

		// Assert
		act.Should().Throw<ArgumentException>()
			.WithMessage("Unknown or unsupported datasource-bound page attribute 'MissingAttribute' in rule.condition.conditions[*].leftExpression.path. Available condition attributes: PDS_Name, PDS_Status.",
				because: "validation errors should help callers choose a supported datasource-bound page attribute");
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

	private static IReadOnlySet<string> CreateElementNames() =>
		new HashSet<string>(StringComparer.Ordinal) {
			"NameInput",
			"StatusInput"
		};
}
