using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Command.BusinessRules;

/// <summary>
/// Structured entity business-rule definition used both as MCP tool contract and internal domain model.
/// </summary>
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
/// Structured top-level condition group.
/// </summary>
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
/// Structured leaf condition definition.
/// </summary>
public sealed record BusinessRuleCondition(
	[property: JsonPropertyName("leftExpression")]
	[property: Description("Left expression. Must be an attribute reference with type AttributeValue.")]
	[property: Required]
	BusinessRuleExpression LeftExpression,

	[property: JsonPropertyName("comparisonType")]
	[property: Description("Condition comparison. Supported values: equal, not-equal.")]
	[property: Required]
	string ComparisonType,

	[property: JsonPropertyName("rightExpression")]
	[property: Description("Right expression. Supports AttributeValue or Const. For lookup constants pass only a GUID string.")]
	[property: Required]
	BusinessRuleExpression RightExpression
);

/// <summary>
/// Structured expression definition (flat — discriminated by <see cref="Type"/>).
/// </summary>
public sealed record BusinessRuleExpression(
	[property: JsonPropertyName("type")]
	[property: Description("Expression type. Supported values: AttributeValue, Const.")]
	[property: Required]
	string Type,

	[property: JsonPropertyName("path")]
	[property: Description("Attribute path when type is AttributeValue.")]
	string? Path,

	[property: JsonPropertyName("value")]
	[property: Description("Constant value when type is Const. For lookup constants pass a GUID string.")]
	JsonElement? Value
);

/// <summary>
/// Structured action definition.
/// </summary>
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
