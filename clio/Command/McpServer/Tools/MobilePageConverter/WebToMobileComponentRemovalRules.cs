namespace Clio.Command.McpServer.Tools.MobilePageConverter;

using System.Collections.Generic;
using System.Text.Json.Serialization;

// The componentRemovals and actionComponents rule shapes (ENG-96178). The filter LANGUAGE these rules are
// written in lives beside this file in ComponentPropertyFilters.cs, because what it expresses — "this named
// property resolves to nothing" — is not about removal and should be reusable by any section that needs the
// same question.

/// <summary>
/// One removal rule: a component of <see cref="Type"/> whose <see cref="Filters"/> match does not reach the
/// mobile page.
/// </summary>
public sealed class ComponentRemovalRule {

	/// <summary>
	/// The component type this rule judges. Matched against the MOBILE type for an element-map entry and
	/// against the raw <c>type</c> for a verbatim-carried node — the same divergence
	/// <c>ExcludedComponentFilterRule</c> documents, and for the same reason: a carried node was never
	/// resolved, so its web type is the only one it has.
	/// </summary>
	[JsonPropertyName("type")]
	public string Type { get; init; }

	/// <summary>
	/// The condition. A rule with NO filters removes nothing — a rule is never allowed to mean "every
	/// component of this type", which would be a page wipe one missing key away.
	/// </summary>
	/// <remarks>
	/// A match is necessary but NOT sufficient: three safety conditions in code veto any rule, and a rule
	/// that appears not to fire is usually meeting one of them. A component is never removed while it still
	/// owns a live event binding, still carries a nested component anywhere in its values, or is still named
	/// as the parent of a surviving operation. See
	/// <c>WebToMobileAnalysisService.StillHasSomethingToLose</c> — each is a way the removal pass could do
	/// damage that nothing reports, so none of them is expressible as a rule an author could switch off.
	/// </remarks>
	[JsonPropertyName("filters")]
	public ComponentPropertyFilter Filters { get; init; }

	/// <summary>
	/// The reason reported for a component this rule removes; defaults to
	/// <see cref="ReasonCodes.DropUnsupportedRequest"/>. Validated on load against the codes valid on a
	/// <c>droppedElements</c> record for THIS pass — a narrower set than the published vocabulary, because
	/// most published codes would state a cause that did not happen.
	/// </summary>
	[JsonPropertyName("reason")]
	public string Reason { get; init; }
}

/// <summary>
/// Declares a component type that exists only to fire an action, and which of its properties carry that
/// action. Such a type is dropped outright when its request cannot convert, where any other type keeps its
/// binding and is merely flagged.
/// </summary>
public sealed class ActionComponentRule {

	/// <summary>The component type (e.g. <c>crt.Button</c>).</summary>
	[JsonPropertyName("type")]
	public string Type { get; init; }

	/// <summary>
	/// The property names whose <c>{ request, params }</c> binding IS this component's action. Absent or
	/// empty falls back to <c>clicked</c>.
	/// </summary>
	/// <remarks>
	/// Read by ONE of the three gates that consult this section, and that asymmetry is deliberate rather
	/// than an oversight. <c>ClassifyClicked</c> asks "does THIS component's action convert", to decide
	/// whether a header control may be retargeted into the FAB, so it needs to know which property carries
	/// the action. The leaf drop and its verbatim-carry twin ask a wider question — "does this component
	/// carry ANY binding the Mobile app cannot fire" — and a component shipping a dead <c>valueChange</c>
	/// is just as broken as one shipping a dead <c>clicked</c>.
	/// <para>
	/// So editing this list moves the FAB gate and leaves the two drop gates where they are. Narrowing those
	/// two to the named properties would be a behaviour change of its own — a dead binding on an unnamed
	/// property would start shipping — and is not what declaring an action property means today.
	/// </para>
	/// </remarks>
	[JsonPropertyName("actionPropertyNames")]
	public IReadOnlyList<string> ActionPropertyNames { get; init; } = [];
}
