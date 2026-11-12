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
	/// </summary>
	/// <param name="element">The view-config element of <see cref="ComponentType"/>.</param>
	/// <param name="elementName">The element's <c>name</c>.</param>
	/// <param name="pageAttributes">The raw <c>bundle.viewModelConfig.attributes</c> object of the page.</param>
	/// <param name="descriptor">The condition attribute the element exposes; <c>null</c> when it exposes none.</param>
	/// <returns><c>true</c> when the element exposes a condition attribute; otherwise <c>false</c>.</returns>
	bool TryResolve(
		JsonObject element,
		string elementName,
		JsonObject pageAttributes,
		[NotNullWhen(true)] out BusinessRuleAttributeDescriptor? descriptor);
}
