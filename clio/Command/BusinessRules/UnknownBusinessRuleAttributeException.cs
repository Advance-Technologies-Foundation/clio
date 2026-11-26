using System;

namespace Clio.Command.BusinessRules;

/// <summary>
/// A business-rule operand or target names an attribute the rule's attribute map does not hold.
/// </summary>
/// <remarks>
/// It stays an <see cref="ArgumentException"/>, so every caller that reports validation failures keeps working;
/// the page validator reads <see cref="Path"/> and <see cref="FieldName"/> to build its own explanation.
/// </remarks>
internal sealed class UnknownBusinessRuleAttributeException(string path, string fieldName)
	: ArgumentException($"Unknown attribute '{path}' in {fieldName}.") {

	/// <summary>Gets the attribute path as the caller supplied it.</summary>
	public string Path { get; } = path;

	/// <summary>Gets the payload field that named the attribute, e.g. rule.condition.conditions[*].leftExpression.path.</summary>
	public string FieldName { get; } = fieldName;
}
