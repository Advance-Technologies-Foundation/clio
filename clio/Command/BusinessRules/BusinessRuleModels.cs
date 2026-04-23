using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Command.BusinessRules;

/// <summary>
/// Represents the MCP request payload for creating one entity-level business rule.
/// </summary>
/// <param name="Caption">Business-rule caption shown to authors.</param>
/// <param name="Condition">Top-level condition group for the rule.</param>
/// <param name="Actions">Actions to execute when the condition matches.</param>
public sealed record BusinessRule(
	[property: JsonPropertyName("caption")]
	[property: Description("Business-rule caption shown to authors.")]
	[property: Required]
	string Caption,

	[property: JsonPropertyName("condition")]
	[property: Description("Top-level condition group. Supports one group with logicalOperation AND or OR.")]
	[property: Required]
	BusinessRuleConditionGroup Condition,

	[property: JsonPropertyName("actions")]
	[property: Description("One or more actions to execute when the condition group matches.")]
	[property: Required]
	List<BusinessRuleAction> Actions
);

/// <summary>
/// Represents the top-level group of leaf business-rule conditions.
/// </summary>
/// <param name="LogicalOperation">Logical operator used to combine the leaf conditions.</param>
/// <param name="Conditions">Leaf conditions evaluated by the group.</param>
public sealed record BusinessRuleConditionGroup(
	[property: JsonPropertyName("logicalOperation")]
	[property: Description("Logical operator. Supported values: AND, OR.")]
	[property: Required]
	string LogicalOperation,

	[property: JsonPropertyName("conditions")]
	[property: Description("Leaf conditions evaluated by the condition group.")]
	[property: Required]
	List<BusinessRuleCondition> Conditions
);

/// <summary>
/// Represents one leaf business-rule comparison.
/// </summary>
/// <param name="LeftExpression">Left expression. Must be an attribute reference.</param>
/// <param name="ComparisonType">Wire comparison type accepted by clio MCP.</param>
/// <param name="RightExpression">Right expression for binary comparisons. Omit or null for unary comparisons.</param>
public sealed record BusinessRuleCondition(
	[property: JsonPropertyName("leftExpression")]
	[property: Description("Left expression. Must be an attribute reference with type AttributeValue.")]
	[property: Required]
	BusinessRuleExpression LeftExpression,

	[property: JsonPropertyName("comparisonType")]
	[property: Description("Condition comparison. Supported values: equal, not-equal, is-filled-in, is-not-filled-in, greater-than, greater-than-or-equal, less-than, less-than-or-equal.")]
	[property: Required]
	string ComparisonType,

	[property: JsonPropertyName("rightExpression")]
	[property: Description("Right expression. Supports AttributeValue or Const for equal, not-equal, and relational comparisons. Omit or null for is-filled-in and is-not-filled-in. For lookup constants pass only a GUID string. For DateTime and Time constants, include a timezone suffix ('Z' or '+/-HH:mm').")]
	BusinessRuleExpression? RightExpression
);

/// <summary>
/// Represents one business-rule expression operand.
/// </summary>
/// <param name="Type">Expression type.</param>
/// <param name="Path">Attribute path when the expression is an attribute reference.</param>
/// <param name="Value">Constant value when the expression is a constant.</param>
public sealed record BusinessRuleExpression(
	[property: JsonPropertyName("type")]
	[property: Description("Expression type. Supported values: AttributeValue, Const.")]
	[property: Required]
	string Type,

	[property: JsonPropertyName("path")]
	[property: Description("Attribute path when type is AttributeValue.")]
	string? Path,

	[property: JsonPropertyName("value")]
	[property: Description("Constant value when type is Const. For lookup constants pass a GUID string. Date constants use yyyy-MM-dd. DateTime constants use ISO 8601 with a timezone suffix ('Z' or '+/-HH:mm'). Time constants use ISO 8601 time with a timezone suffix ('Z' or '+/-HH:mm').")]
	JsonElement? Value
);

/// <summary>
/// Represents one field-state action executed by the business rule.
/// </summary>
/// <param name="Type">Action type.</param>
/// <param name="Items">Target attributes affected by the action.</param>
public sealed record BusinessRuleAction(
	[property: JsonPropertyName("type")]
	[property: Description("Business-rule action. Supported values: make-editable, make-read-only, make-required, make-optional.")]
	[property: Required]
	string Type,

	[property: JsonPropertyName("items")]
	[property: Description("One or more target attributes affected by the action.")]
	[property: Required]
	List<string> Items
);
