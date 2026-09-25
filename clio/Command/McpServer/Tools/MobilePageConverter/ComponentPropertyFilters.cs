namespace Clio.Command.McpServer.Tools.MobilePageConverter;

using System.Collections.Generic;
using System.Text.Json.Serialization;

// A small filter language over a COMPONENT'S PROPERTIES, carried in the versioned rules document. Two node
// kinds, one predicate, no comparison operators and no negation — every primitive added here becomes a
// contract a rules push can rely on and a clio release cannot take back.
//
// Its only consumer today is `componentRemovals` (see ComponentRemovalRule), which asks "does this control
// still have anything to do". The types live apart from that section on purpose: what they express — "this
// named property resolves to nothing" — is not about removal, and a second rules section that needs the same
// question should reuse the grammar rather than grow a third spelling of it.
//
// WHY NOT ElementFilterRule. That type is the converter's other filter, and it is EQUALITY-only by design: a
// value matches on deep equality, and "an ABSENT property never matches" is a stated rule of it
// (WebToMobileAnalysisService.MatchesValueConstraints). An emptiness test is the exact inverse of that rule,
// so teaching it there would change what every existing components/componentPropertyOverrides filter means.
// Two grammars for two questions is the cheaper answer; sharing no code means neither can change what the
// other MEANS. They can still drift on a rule both restate by hand — the empty-group polarity below is one.
//
// PROPERTY ORDER MUST NOT MATTER. A rules file is authored by hand in another repository, and STJ's
// discriminator historically had to come FIRST in the object — an author who wrote `leftExpression` first
// would get a failure with nothing in the data to show why. `AllowOutOfOrderMetadataProperties` on the
// catalog's serializer options removes that constraint, which is what BindingsModule.CreateMcpSerializerOptions
// does for the same class of reason. An unrecognised `filterType` then throws, and what that costs is stated
// on WebToMobilePageConversionRulesCatalog.ValidateComponentRemovals.

/// <summary>The closed set of <see cref="ComponentPropertyFilter"/> kinds.</summary>
public static class ComponentPropertyFilterTypes {

	/// <summary>Combines nested filters with a <see cref="ComponentPropertyLogicalOperations"/> value.</summary>
	public const string Group = "Group";

	/// <summary>
	/// True when the named expression resolves to nothing — see
	/// <c>WebToMobileAnalysisService.IsEmptyExpression</c> for what "resolves" means.
	/// </summary>
	public const string IsEmpty = "IsEmpty";
}

/// <summary>How a <see cref="ComponentPropertyGroupFilter"/> combines its items.</summary>
public static class ComponentPropertyLogicalOperations {

	/// <summary>Every item must match. The DEFAULT for an unrecognized or absent value.</summary>
	public const string And = "and";

	/// <summary>Any item matching is enough.</summary>
	public const string Or = "or";
}

/// <summary>
/// One node of a filter tree over a component's properties, discriminated by its <c>filterType</c> member.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "filterType")]
[JsonDerivedType(typeof(ComponentPropertyGroupFilter), ComponentPropertyFilterTypes.Group)]
[JsonDerivedType(typeof(ComponentPropertyIsEmptyFilter), ComponentPropertyFilterTypes.IsEmpty)]
public abstract class ComponentPropertyFilter { }

/// <summary>A boolean combination of nested filters.</summary>
public sealed class ComponentPropertyGroupFilter : ComponentPropertyFilter {

	/// <summary>
	/// One of <see cref="ComponentPropertyLogicalOperations"/>. Anything else — including absent — is read as
	/// <see cref="ComponentPropertyLogicalOperations.And"/>, the narrower of the two: a misspelled operation
	/// must not silently WIDEN what a rule matches.
	/// </summary>
	[JsonPropertyName("logicalOperation")]
	public string LogicalOperation { get; init; }

	/// <summary>
	/// The nested filters. An EMPTY group matches nothing, deliberately rather than everything — the same
	/// polarity <c>ElementFilterRule</c>'s own <c>Declares</c> gate chose, and for the same reason: a rule
	/// that declares no condition is an authoring mistake, and the safe reading of a mistake is "match
	/// nothing".
	/// </summary>
	[JsonPropertyName("items")]
	public IReadOnlyList<ComponentPropertyFilter> Items { get; init; } = [];
}

/// <summary>True when <see cref="LeftExpression"/> resolves to nothing on the component being judged.</summary>
public sealed class ComponentPropertyIsEmptyFilter : ComponentPropertyFilter {

	/// <summary>
	/// A component PROPERTY name (e.g. <c>clicked</c>, <c>menuItems</c>). It is resolved against the MERGED
	/// state of that property — every operation the conversion emits for it, not the raw source value — so a
	/// slot filled by child operations reads as non-empty even though the owner's own values never carry it.
	/// </summary>
	[JsonPropertyName("leftExpression")]
	public string LeftExpression { get; init; }
}
