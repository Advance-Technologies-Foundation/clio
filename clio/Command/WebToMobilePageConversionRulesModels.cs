namespace Clio.Command;

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

// Conversion RULES contract (ENG-89620). Version-dependent, loaded as JSON today and
// CDN-endpoint-ready (mirrors the academy component registries).
// This is the GENERAL group structure only — detailed matrix content is filled in a later step.

/// <summary>
/// Versioned set of page-conversion rule groups. New groups are added as new properties;
/// unmapped producer fields are preserved via <see cref="Extensions"/> (forward-compat guard).
/// </summary>
public sealed class WebToMobilePageConversionRules {
	/// <summary>Creatio/registry version these rules apply to (e.g. "latest" or "8.3.3").</summary>
	[JsonPropertyName("version")]
	public string Version { get; init; } = "latest";

	/// <summary>Group: base page template (schema) equivalence between web and mobile.</summary>
	[JsonPropertyName("templates")]
	public IReadOnlyList<TemplateMappingRule> Templates { get; init; } = [];

	/// <summary>Group: equivalent components — web↔mobile mappings that are not a same-type match.</summary>
	[JsonPropertyName("components")]
	public IReadOnlyList<ComponentEquivalenceRule> Components { get; init; } = [];

	/// <summary>
	/// Group: web↔mobile request (action) equivalence. Requests are wired declaratively to a
	/// component's event binding (e.g. a button's <c>clicked: { request, params }</c>); the mobile app
	/// supports only a subset of web requests. Used to remap a supported request, strip an unsupported
	/// one, or flag an unknown/custom one during conversion.
	/// </summary>
	[JsonPropertyName("requests")]
	public IReadOnlyList<RequestMappingRule> Requests { get; init; } = [];

	/// <summary>Any future producer field not yet mapped to a typed group.</summary>
	[JsonExtensionData]
	public IDictionary<string, JsonElement> Extensions { get; init; }
}

/// <summary>
/// Maps a web base page template (schema) to its mobile counterpart. Used to recommend the
/// mobile template to create for a converted page. Details (per-template specifics) come later.
/// </summary>
public sealed class TemplateMappingRule {
	/// <summary>Web base page template schema name (e.g. "PageWithTabsFreedomTemplate").</summary>
	[JsonPropertyName("web")]
	public string Web { get; init; }

	/// <summary>Mobile base page template schema name (e.g. "MobilePageWithTabsFreedomTemplate").</summary>
	[JsonPropertyName("mobile")]
	public string Mobile { get; init; }

	/// <summary>
	/// Container-name correspondence between the web template and the mobile template. Used to
	/// remap each converted element's <c>parentName</c> from its web container to the mobile one.
	/// </summary>
	[JsonPropertyName("containers")]
	public IReadOnlyList<ContainerMappingRule> Containers { get; init; } = [];

	/// <summary>
	/// Named CONTENT-component correspondence between the web template and the mobile template
	/// (analogous to <see cref="Containers"/>, but for components rather than layout containers).
	/// A web element listed here is NOT removed as inherited template chrome; instead it is kept and
	/// recorded as a merge-by-name twin onto the mapped mobile element. HOW to convert / reconcile its
	/// values is type-driven and comes from the general components rule (surfaced in
	/// <c>componentSuggestions</c>) — the model transfers them; clio prebuilds no values here.
	/// </summary>
	[JsonPropertyName("components")]
	public IReadOnlyList<ComponentMappingRule> Components { get; init; } = [];

	[JsonPropertyName("note")]
	public string Note { get; init; }
}

/// <summary>
/// Maps a web container name to its mobile counterpart within a template pair.
/// </summary>
public sealed class ContainerMappingRule {
	/// <summary>Web container name (e.g. "SideAreaProfileContainer").</summary>
	[JsonPropertyName("web")]
	public string Web { get; init; }

	/// <summary>Mobile container name (e.g. "AreaProfileContainer").</summary>
	[JsonPropertyName("mobile")]
	public string Mobile { get; init; }

	[JsonPropertyName("note")]
	public string Note { get; init; }
}

/// <summary>
/// Maps a web component to its mobile counterpart by element NAME within a template pair (any content
/// component, not just a list). Unlike <see cref="ComponentEquivalenceRule"/> (which maps component TYPES
/// globally and carries the conversion detail), this is per-template and keyed by the element name the
/// template provides (e.g. the list template's grid named "DataTable"). A mapped element is kept through
/// inherited-template-chrome subtraction and configured by merge-by-name; HOW to convert it is type-driven
/// and comes from the general components rule (surfaced in <c>componentSuggestions</c>) — not from here.
/// </summary>
public sealed class ComponentMappingRule {
	/// <summary>Web element name (e.g. "DataTable").</summary>
	[JsonPropertyName("web")]
	public string Web { get; init; }

	/// <summary>
	/// Mobile element name it corresponds to (e.g. "List"). The mobile template provides this element;
	/// it is configured by merge-by-name (not inserted as a duplicate).
	/// </summary>
	[JsonPropertyName("mobile")]
	public string Mobile { get; init; }

	/// <summary>Business meaning of the element (e.g. "Primary list component"), not conversion mechanics.</summary>
	[JsonPropertyName("note")]
	public string Note { get; init; }
}

/// <summary>
/// Maps a web request (action) to its mobile counterpart. A request is dispatched declaratively from a
/// component's event binding (<c>clicked</c> / <c>valueChange</c> / <c>updated</c>) as
/// <c>{ "request": "crt.X", "params": { ... } }</c>. An empty/null <see cref="Mobile"/> means the
/// request is NOT supported on mobile (the binding is stripped during conversion). A request absent from
/// this map entirely is treated as unknown/custom and flagged for manual review (kept as-is).
/// </summary>
public sealed class RequestMappingRule {
	/// <summary>Web request type, e.g. "crt.SaveRecordRequest".</summary>
	[JsonPropertyName("web")]
	public string Web { get; init; }

	/// <summary>Mobile request type (often the same name). Empty/null when unsupported on mobile.</summary>
	[JsonPropertyName("mobile")]
	public string Mobile { get; init; }

	/// <summary>One of: DirectMapping, WithAdaptation, Unsupported, RequiresManualDecision.</summary>
	[JsonPropertyName("category")]
	public string Category { get; init; }

	/// <summary>
	/// Optional web→mobile rename of <c>params</c> keys (for requests whose parameter names differ).
	/// Empty for direct mappings — params are carried verbatim.
	/// </summary>
	[JsonPropertyName("paramMap")]
	public IReadOnlyDictionary<string, string> ParamMap { get; init; } = new Dictionary<string, string>();

	[JsonPropertyName("note")]
	public string Note { get; init; }
}

/// <summary>
/// Maps equivalent components between web and mobile. Both sides are lists so the rule can express
/// one→one, one→many and many→one cardinality (and same-type-but-structurally-different cases).
/// <see cref="Category"/> is one of the five <see cref="ComponentMappingCategory"/> values
/// (parsed case-insensitively). Structural / per-property details are filled in a later step.
/// </summary>
public sealed class ComponentEquivalenceRule {
	/// <summary>Web component type(s), e.g. ["crt.Checkbox"].</summary>
	[JsonPropertyName("web")]
	public IReadOnlyList<string> Web { get; init; } = [];

	/// <summary>Mobile component type(s), e.g. ["crt.Toggle"]. Empty for unsupported components.</summary>
	[JsonPropertyName("mobile")]
	public IReadOnlyList<string> Mobile { get; init; } = [];

	/// <summary>One of: DirectMapping, WithAdaptation, AlternativeAvailable, Unsupported, RequiresManualDecision.</summary>
	[JsonPropertyName("category")]
	public string Category { get; init; }

	/// <summary>
	/// For many→one rules: the web component type that becomes the single mobile component (the
	/// "anchor" — its position is used). Other present web components from <see cref="Web"/> are
	/// consumed (their properties may be pulled via <see cref="PropertyMap"/>). Optional.
	/// </summary>
	[JsonPropertyName("primaryWeb")]
	public string PrimaryWeb { get; init; }

	[JsonPropertyName("note")]
	public string Note { get; init; }
}
