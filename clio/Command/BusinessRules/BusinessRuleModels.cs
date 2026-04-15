using System.Collections.Generic;
using System.Text.Json;

namespace Clio.Command.BusinessRules;

/// <summary>
/// Structured entity business-rule definition.
/// </summary>
public sealed record BusinessRule(
	string Caption,
	bool? Enabled,
	BusinessRuleConditionGroup ConditionGroup,
	IReadOnlyList<BusinessRuleAction> Actions
);

/// <summary>
/// Structured top-level condition group.
/// </summary>
public sealed record BusinessRuleConditionGroup(
	string Operator,
	IReadOnlyList<BusinessRuleCondition> Conditions
);

/// <summary>
/// Structured leaf condition definition.
/// </summary>
public sealed record BusinessRuleCondition(
	BusinessRuleOperand Left,
	string Comparison,
	BusinessRuleOperand Right
);

/// <summary>
/// Structured operand definition.
/// </summary>
public sealed record BusinessRuleOperand(
	string Kind,
	string? Path,
	JsonElement? Value,
	string? DataValueTypeName
);

/// <summary>
/// Structured action definition.
/// </summary>
public sealed record BusinessRuleAction(
	string Action,
	IReadOnlyList<string> Targets
);
