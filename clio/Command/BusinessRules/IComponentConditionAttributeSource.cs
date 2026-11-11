using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Clio.Command.BusinessRules;

/// <summary>
/// Exposes the state of one view-config component type as a page business-rule condition attribute.
/// </summary>
internal interface IComponentConditionAttributeSource {
	/// <summary>Component type the source handles, for example <c>crt.Stepper</c>.</summary>
	string ComponentType { get; }

	/// <summary>
	/// Resolves the condition attribute a named element of <see cref="ComponentType"/> exposes.
	/// Returns <c>false</c> when the element exposes none.
	/// </summary>
	bool TryResolve(
		JsonObject element,
		string elementName,
		JsonObject pageAttributes,
		[NotNullWhen(true)] out BusinessRuleAttributeDescriptor? descriptor);
}
