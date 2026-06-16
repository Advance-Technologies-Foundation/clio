namespace Clio.Command;

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

// Advisory contract for the `get-mobile-page-conversion-guide` MCP tool (ENG-89620).
// The tool is REPORT-ONLY: it never builds a mobile page body and never writes to Creatio.
// It detects the source page type and returns a deterministic "conversion guide" that an LLM uses
// to build the mobile page body itself (via create-page / update-page / validate-page +
// get-component-info). The guide is intentionally extensible — new advisory sections (and new
// source page types) can be added over time.

/// <summary>
/// One node of the source page's resolved (merged) component tree, surfaced so the model can
/// see the full structure including components inherited from the base template.
/// </summary>
public sealed class SourceComponentInfo {
	[JsonPropertyName("name")]
	public string Name { get; init; }

	[JsonPropertyName("type")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Type { get; init; }

	[JsonPropertyName("parentName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ParentName { get; init; }

	[JsonPropertyName("isContainer")]
	public bool IsContainer { get; init; }
}

/// <summary>
/// A web→mobile container-name correspondence from the matched template pair. The model uses it
/// to set each component's <c>parentName</c> to the correct mobile container.
/// </summary>
public sealed class ContainerMapEntry {
	[JsonPropertyName("web")]
	public string Web { get; init; }

	[JsonPropertyName("mobile")]
	public string Mobile { get; init; }

	[JsonPropertyName("note")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Note { get; init; }
}

/// <summary>
/// A deterministic suggestion for one source component type: how it classifies and which mobile
/// type(s) it maps to (from the WebToMobilePageConversionRules matrix + registry type comparison).
/// </summary>
public sealed class ComponentSuggestion {
	[JsonPropertyName("sourceType")]
	public string SourceType { get; init; }

	/// <summary>Names of the source-page components that have this type.</summary>
	[JsonPropertyName("sourceNames")]
	public IReadOnlyList<string> SourceNames { get; init; } = [];

	/// <summary>One of the five ComponentMappingCategory values, as a string.</summary>
	[JsonPropertyName("category")]
	public string Category { get; init; }

	/// <summary>Suggested mobile component type(s). Empty for unsupported / manual-decision.</summary>
	[JsonPropertyName("suggestedMobileTypes")]
	public IReadOnlyList<string> SuggestedMobileTypes { get; init; } = [];

	/// <summary>When several web types collapse to one mobile component, explains the merge (many→one).</summary>
	[JsonPropertyName("primaryWebMerge")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string PrimaryWebMerge { get; init; }

	[JsonPropertyName("note")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Note { get; init; }
}

/// <summary>
/// Caption/resource convention for a newly inserted named element. The caller passes
/// <see cref="Key"/> = <see cref="SourceValue"/> to <c>update-page resources</c> verbatim.
/// </summary>
public sealed class CaptionResource {
	[JsonPropertyName("key")]
	public string Key { get; init; }

	[JsonPropertyName("sourceValue")]
	public string SourceValue { get; init; }
}

/// <summary>
/// Instance-level conversion decision for ONE named element of the source page (ENG-89620). One
/// entry per named element of <c>sourceStructure</c>. The <see cref="Operation"/> tells the caller
/// exactly what to do with this element on the mobile page; it never has to infer merge-vs-insert
/// from <c>containerMap</c> + <c>componentSuggestions</c>.
/// </summary>
public sealed class ElementMapEntry {
	[JsonPropertyName("webName")]
	public string WebName { get; init; }

	[JsonPropertyName("webType")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string WebType { get; init; }

	/// <summary>One of: <c>merge</c> | <c>insert</c> | <c>drop</c> | <c>relocate-children</c>.</summary>
	[JsonPropertyName("operation")]
	public string Operation { get; init; }

	/// <summary>Target element name on mobile (merge / insert).</summary>
	[JsonPropertyName("mobileName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string MobileName { get; init; }

	/// <summary>Target mobile type (insert / merge), when known to the mobile registry.</summary>
	[JsonPropertyName("mobileType")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string MobileType { get; init; }

	/// <summary>
	/// Mobile parent element to attach to. For <c>insert</c> it is the element's parent; for
	/// <c>relocate-children</c> it is the container the element's children are placed into instead.
	/// </summary>
	[JsonPropertyName("parentName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ParentName { get; init; }

	/// <summary>Parent property to insert into (insert); defaults to <c>items</c>.</summary>
	[JsonPropertyName("propertyName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string PropertyName { get; init; }

	/// <summary>For an <c>insert</c> of a named element with a localizable caption.</summary>
	[JsonPropertyName("captionResource")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public CaptionResource CaptionResource { get; init; }

	[JsonPropertyName("reason")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Reason { get; init; }
}

/// <summary>
/// Compact, inline contract for a suggested mobile component type, drawn from the mobile registry,
/// so the model can build the component's <c>values</c> without extra get-component-info round-trips.
/// </summary>
public sealed class MobileComponentContract {
	[JsonPropertyName("componentType")]
	public string ComponentType { get; init; }

	[JsonPropertyName("container")]
	public bool Container { get; init; }

	[JsonPropertyName("description")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Description { get; init; }

	/// <summary>Property/input names this mobile component accepts (Properties ∪ Inputs).</summary>
	[JsonPropertyName("allowedProperties")]
	public IReadOnlyList<string> AllowedProperties { get; init; } = [];

	[JsonPropertyName("example")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonElement? Example { get; init; }

	[JsonPropertyName("designerDefaults")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonElement? DesignerDefaults { get; init; }
}

/// <summary>
/// A workplace (SysWorkplace) the converted section could be registered in. <see cref="IsMobile"/>
/// marks workplaces of the Mobile client type; <see cref="ContainsSection"/> is true when the source
/// section is already a member of this workplace.
/// </summary>
public sealed class WorkplaceInfo {
	[JsonPropertyName("id")]
	public string Id { get; init; }

	[JsonPropertyName("name")]
	public string Name { get; init; }

	[JsonPropertyName("isMobile")]
	public bool IsMobile { get; init; }

	[JsonPropertyName("containsSection")]
	public bool ContainsSection { get; init; }
}

/// <summary>
/// Read-only facts about whether the source page is registered as a section (SysModule) and what it
/// takes to make that section available in the Creatio Mobile app. The tool only DETECTS and reports
/// this; the model performs the writes (odata-update / odata-create) after the user approves (Gate S).
/// </summary>
public sealed class SectionRegistrationInfo {
	/// <summary>True when a SysModule row references the source page as its section / list page.</summary>
	[JsonPropertyName("sourcePageIsSection")]
	public bool SourcePageIsSection { get; init; }

	/// <summary>Id of the matched SysModule row — the odata-update target for MobileSectionSchemaUId.</summary>
	[JsonPropertyName("sysModuleId")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string SysModuleId { get; init; }

	[JsonPropertyName("sectionCode")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string SectionCode { get; init; }

	[JsonPropertyName("sectionCaption")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string SectionCaption { get; init; }

	[JsonPropertyName("entitySchemaName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string EntitySchemaName { get; init; }

	/// <summary>Current MobileSectionSchemaUId on the SysModule row (null/empty when not yet registered).</summary>
	[JsonPropertyName("mobileSectionSchemaUId")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string MobileSectionSchemaUId { get; init; }

	/// <summary>True when MobileSectionSchemaUId is already set to a non-empty schema UId.</summary>
	[JsonPropertyName("mobileSectionRegistered")]
	public bool MobileSectionRegistered { get; init; }

	/// <summary>True when the source page is an edit/form page (vs a list/section page).</summary>
	[JsonPropertyName("isFormPage")]
	public bool IsFormPage { get; init; }

	/// <summary>Best-effort: the source page is the entity's default edit page (RelatedPage addon).</summary>
	[JsonPropertyName("sourcePageIsDefaultEditPage")]
	public bool SourcePageIsDefaultEditPage { get; init; }

	/// <summary>Best-effort: a mobile default edit page (MobileRelatedPage addon) already exists.</summary>
	[JsonPropertyName("mobileDefaultEditPageExists")]
	public bool MobileDefaultEditPageExists { get; init; }

	/// <summary>Workplaces the source section is currently a member of.</summary>
	[JsonPropertyName("currentWorkplaces")]
	public IReadOnlyList<WorkplaceInfo> CurrentWorkplaces { get; init; } = [];

	/// <summary>Workplaces of the Mobile client type the section could be added to.</summary>
	[JsonPropertyName("availableMobileWorkplaces")]
	public IReadOnlyList<WorkplaceInfo> AvailableMobileWorkplaces { get; init; } = [];

	/// <summary>Human-readable registration steps the model should propose to the user (Gate S).</summary>
	[JsonPropertyName("registrationActions")]
	public IReadOnlyList<string> RegistrationActions { get; init; } = [];

	[JsonPropertyName("note")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Note { get; init; }

	/// <summary>False when the environment could not be queried (registration facts are best-effort/unknown).</summary>
	[JsonPropertyName("probeOk")]
	public bool ProbeOk { get; init; }
}

/// <summary>
/// Deterministic advisory "conversion guide" for turning a source page into a Freedom UI mobile
/// page. The model executes the conversion using this guide; the tool builds nothing. The
/// <see cref="SourceType"/> records which source page type was detected (today: <c>freedom-web</c>).
/// </summary>
public sealed class MobilePageConversionGuide {
	// ── Source analysis ───────────────────────────────────────────────
	[JsonPropertyName("sourcePage")]
	public string SourcePage { get; init; }

	/// <summary>Detected source page type, e.g. <c>freedom-web</c> (future: other source types).</summary>
	[JsonPropertyName("sourceType")]
	public string SourceType { get; init; }

	/// <summary>The source page's parent (base) template schema name.</summary>
	[JsonPropertyName("sourceTemplate")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string SourceTemplate { get; init; }

	/// <summary>Full resolved component tree (incl. inherited template components).</summary>
	[JsonPropertyName("sourceStructure")]
	public IReadOnlyList<SourceComponentInfo> SourceStructure { get; init; } = [];

	/// <summary>Web-only body sections present on the source (handlers / validators / converters).</summary>
	[JsonPropertyName("webOnlySections")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> WebOnlySections { get; init; }

	/// <summary>Data source names declared on the source page (mobile supports one).</summary>
	[JsonPropertyName("dataSources")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> DataSources { get; init; }

	// ── Data sections (apply to the mobile body via *Diff) ────────────
	/// <summary>
	/// The source page's full merged <c>modelConfig</c> (data sources + attributes). Mobile has identical
	/// structural support, so APPLY IT VERBATIM via <c>modelConfigDiff</c> — keep every attribute and ALL of
	/// its properties exactly as provided (do not omit, rename, or reconstruct any fields). Dropping or
	/// altering an attribute's declared metadata can make its binding unresolvable in Mobile Designer
	/// (<c>Item with the path … not found</c>). Null when the source page declares no model config.
	/// </summary>
	[JsonPropertyName("modelConfig")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonNode ModelConfig { get; init; }

	/// <summary>
	/// The source page's merged <c>viewModelConfig</c>, already FILTERED for mobile: attributes referenced
	/// only by dropped/unsupported components are removed (see <see cref="ElementMap"/>). Apply it via
	/// <c>viewModelConfigDiff</c>. Reference only OOTB mobile converters — a definitive mobile converter
	/// list is forthcoming; flag any custom converter for manual review. Null when none is declared.
	/// </summary>
	[JsonPropertyName("viewModelConfig")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonNode ViewModelConfig { get; init; }

	// ── Template recommendation ───────────────────────────────────────
	[JsonPropertyName("recommendedMobileTemplate")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string RecommendedMobileTemplate { get; init; }

	[JsonPropertyName("templateNote")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string TemplateNote { get; init; }

	[JsonPropertyName("containerMap")]
	public IReadOnlyList<ContainerMapEntry> ContainerMap { get; init; } = [];

	// ── Component mapping suggestions ─────────────────────────────────
	[JsonPropertyName("componentSuggestions")]
	public IReadOnlyList<ComponentSuggestion> ComponentSuggestions { get; init; } = [];

	/// <summary>
	/// Instance-level decision (merge / insert / drop / relocate-children) for every named element of
	/// the source page. Iterate this to build the body — do not infer merge-vs-insert from containerMap.
	/// </summary>
	[JsonPropertyName("elementMap")]
	public IReadOnlyList<ElementMapEntry> ElementMap { get; init; } = [];

	/// <summary>Inline contracts for every suggested / direct-mapped mobile component type.</summary>
	[JsonPropertyName("mobileContracts")]
	public IReadOnlyList<MobileComponentContract> MobileContracts { get; init; } = [];

	// ── Section / workplace registration (read-only facts) ────────────
	/// <summary>
	/// Whether the source page is a registered section and what it takes to make it available in the
	/// Mobile app (set MobileSectionSchemaUId, add to a workplace). Read-only — the model performs the
	/// writes after the user approves (Gate S). Null when the source page is not list/section-like.
	/// </summary>
	[JsonPropertyName("sectionRegistration")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public SectionRegistrationInfo SectionRegistration { get; init; }

	// ── Guidance ──────────────────────────────────────────────────────
	[JsonPropertyName("constraints")]
	public IReadOnlyList<string> Constraints { get; init; } = [];

	[JsonPropertyName("nextSteps")]
	public IReadOnlyList<string> NextSteps { get; init; } = [];

	[JsonPropertyName("guidanceArticle")]
	public string GuidanceArticle { get; init; }

	[JsonPropertyName("suggestedTargetSchemaName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string SuggestedTargetSchemaName { get; init; }
}

/// <summary>
/// Response envelope for the <c>get-mobile-page-conversion-guide</c> MCP tool.
/// </summary>
public sealed class MobilePageConversionGuideResponse {
	[JsonPropertyName("success")]
	public bool Success { get; init; }

	[JsonPropertyName("sourceSchemaName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string SourceSchemaName { get; init; }

	/// <summary>Detected source page type even on failure (e.g. an unsupported type).</summary>
	[JsonPropertyName("sourceType")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string SourceType { get; init; }

	[JsonPropertyName("guide")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public MobilePageConversionGuide Guide { get; init; }

	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }
}
