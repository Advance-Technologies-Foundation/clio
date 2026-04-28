using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Command.BusinessRules;

public sealed record BusinessRule {
	public BusinessRule() {
	}

	public BusinessRule(string caption, BusinessRuleConditionGroup condition, List<BusinessRuleAction> actions) {
		Caption = caption;
		Condition = condition;
		Actions = actions;
	}

	[JsonPropertyName("caption")]
	[Description("Business-rule caption shown to No-code developers.")]
	[Required]
	public string Caption { get; init; } = null!;

	[JsonPropertyName("condition")]
	[Description("Top-level condition group. Supports one group with logicalOperation AND or OR.")]
	[Required]
	public BusinessRuleConditionGroup Condition { get; init; } = null!;

	[JsonPropertyName("actions")]
	[Description("One or more actions to execute when the condition group matches.")]
	[Required]
	public List<BusinessRuleAction> Actions { get; init; } = null!;
}

public sealed record BusinessRuleConditionGroup {
	public BusinessRuleConditionGroup() {
	}

	public BusinessRuleConditionGroup(string logicalOperation, List<BusinessRuleCondition> conditions) {
		LogicalOperation = logicalOperation;
		Conditions = conditions;
	}

	[JsonPropertyName("logicalOperation")]
	[Description("Logical operator. Supported values: AND, OR.")]
	[Required]
	public string LogicalOperation { get; init; } = null!;

	[JsonPropertyName("conditions")]
	[Description("List of conditions evaluated by the condition group.")]
	[Required]
	public List<BusinessRuleCondition> Conditions { get; init; } = null!;
}

public sealed record BusinessRuleCondition {
	public BusinessRuleCondition() {
	}

	public BusinessRuleCondition(
		BusinessRuleExpression leftExpression,
		string comparisonType,
		BusinessRuleExpression? rightExpression = null) {
		LeftExpression = leftExpression;
		ComparisonType = comparisonType;
		RightExpression = rightExpression;
	}

	[JsonPropertyName("leftExpression")]
	[Description("Left expression. Must be an attribute reference with type AttributeValue.")]
	[Required]
	public BusinessRuleExpression LeftExpression { get; init; } = null!;

	[JsonPropertyName("comparisonType")]
	[Description("Condition comparison. Supported values: equal, not-equal, is-filled-in, is-not-filled-in, greater-than, greater-than-or-equal, less-than, less-than-or-equal.")]
	[Required]
	public string ComparisonType { get; init; } = null!;

	[JsonPropertyName("rightExpression")]
	[Description("Right expression. Supports AttributeValue or Const for equal, not-equal, and relational comparisons. Omit or null for is-filled-in and is-not-filled-in.")]
	public BusinessRuleExpression? RightExpression { get; init; }
}

public sealed record BusinessRuleExpression {
	public BusinessRuleExpression() {
	}

	public BusinessRuleExpression(string type, string? path = null, JsonElement? value = null) {
		Type = type;
		Path = path;
		Value = value;
	}

	[JsonPropertyName("type")]
	[Description("Expression type. Supported values: AttributeValue, Const.")]
	[Required]
	public string Type { get; init; } = null!;

	[JsonPropertyName("path")]
	[Description("Attribute path when type is AttributeValue.")]
	public string? Path { get; init; }

	[JsonPropertyName("value")]
	[Description("Constant value when type is Const. Boolean constants must use JSON booleans true/false without quotes. Numeric constants must use JSON numbers like 123 or 12.5 without quotes. For lookup constants pass a GUID string. Date constants use yyyy-MM-dd. DateTime constants use ISO 8601 with a timezone suffix ('Z' or '+/-HH:mm'). Time constants use ISO 8601 time with a timezone suffix ('Z' or '+/-HH:mm').")]
	public JsonElement? Value { get; init; }
}

public sealed record BusinessRuleAction {
	public BusinessRuleAction() {
	}

	public BusinessRuleAction(string type, List<string> items) {
		Type = type;
		Items = items;
	}

	[JsonPropertyName("type")]
	[Description("Business-rule action. Supported values: make-editable, make-read-only, make-required, make-optional.")]
	[Required]
	public string Type { get; init; } = null!;

	[JsonPropertyName("items")]
	[Description("One or more target attributes affected by the action.")]
	[Required]
	public List<string> Items { get; init; } = null!;
}
