using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

namespace Clio.Command.BusinessRules;

/// <summary>
/// The open step of a <c>crt.Stepper</c>, compared with a step name. Unbound <c>activeStep</c> or a
/// constant step name: the generated <c>&lt;name&gt;_ActiveStep</c>, dots in the name becoming
/// underscores. Bound to a plain <c>$Attr</c> the page declares without a data source: that attribute.
/// Any other binding (a nested path, a converter, a property binding such as <c>$Attr:prop</c>, an
/// undeclared attribute, an attribute bound to a record column) leaves the stepper unable to change
/// step, so it exposes none.
/// </summary>
internal sealed class StepperActiveStepAttributeSource : IComponentConditionAttributeSource {

	private const string ActiveStepPropertyName = "activeStep";
	private const string GeneratedSuffix = "_ActiveStep";
	private const string DataValueTypeName = "Text";

	public string ComponentType => "crt.Stepper";

	public bool TryResolve(
		JsonObject element,
		string elementName,
		JsonObject pageAttributes,
		[NotNullWhen(true)] out BusinessRuleAttributeDescriptor? descriptor) {
		descriptor = null;
		if (!TryResolveAttributeName(element, elementName, pageAttributes, out string attributeName)) {
			return false;
		}
		descriptor = new BusinessRuleAttributeDescriptor(attributeName, DataValueTypeName, null) {
			IsComponentAttribute = true
		};
		return true;
	}

	private static bool TryResolveAttributeName(
		JsonObject stepper,
		string stepperName,
		JsonObject pageAttributes,
		out string attributeName) {
		attributeName = string.Empty;
		switch (stepper[ActiveStepPropertyName]) {
			case null:
			case JsonValue constant when constant.TryGetValue(out string? stepName) && !stepName.StartsWith('$'):
				attributeName = stepperName.Replace('.', '_') + GeneratedSuffix;
				return true;
			case JsonValue value when value.TryGetValue(out string? text)
				&& TryGetPlainAttributeBinding(text, out string boundName)
				&& IsPlainDeclaredAttribute(pageAttributes, boundName):
				attributeName = boundName;
				return true;
			default:
				return false;
		}
	}

	private static bool IsPlainDeclaredAttribute(JsonObject pageAttributes, string attributeName) =>
		pageAttributes[attributeName] is JsonObject attribute && attribute["modelConfig"] is null;

	private static bool TryGetPlainAttributeBinding(string binding, out string attributeName) {
		attributeName = string.Empty;
		if (binding.Length < 2 || binding[0] != '$' || char.IsAsciiDigit(binding[1])) {
			return false;
		}
		for (int i = 1; i < binding.Length; i++) {
			if (!char.IsAsciiLetterOrDigit(binding[i]) && binding[i] != '_') {
				return false;
			}
		}
		attributeName = binding[1..];
		return true;
	}
}
