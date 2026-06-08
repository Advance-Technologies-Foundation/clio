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
