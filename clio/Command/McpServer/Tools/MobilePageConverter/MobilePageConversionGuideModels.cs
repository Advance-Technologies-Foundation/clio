namespace Clio.Command.McpServer.Tools.MobilePageConverter;

using System;
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

	/// <summary>
	/// Whether this source element holds child components. Derived from the TREE — a node with a child
	/// component slot is a container whatever any registry says — falling back to a published registry
	/// flag and then to a name-suffix heuristic only for an EMPTY element, which the tree cannot settle.
	/// Always present: unlike <see cref="MobileComponentContract.Container"/> this is never unknown.
	/// </summary>
	[JsonPropertyName("isContainer")]
	public bool IsContainer { get; init; }
}

/// <summary>
/// One change to a TEMPLATE-OWNED data-section value that no mobile diff operation can express, so the
/// converted page cannot carry it. Reported per occurrence rather than described in prose because the three
/// <see cref="Kind"/>s have DIFFERENT outcomes and two different remedies.
/// </summary>
/// <remarks>
/// No diff operation in the mobile vocabulary edits an existing array element in place: the path applier
/// identifies elements by <c>_id</c> while these config elements are keyed by <c>name</c>, so a
/// name-addressed merge has no <c>_id</c> to resolve and an insert would duplicate the name. The converter
/// therefore lets the template's native value win and reports the loss here instead of shipping a silently
/// lossy body.
/// </remarks>
public sealed record DataSectionConflict {
	/// <summary>
	/// Which data section the conflict is in — <c>"modelConfig"</c> or <c>"viewModelConfig"</c>. It names the
	/// diff the caller has to hand-edit if the page's value must win.
	/// </summary>
	[JsonPropertyName("section")]
	public string Section { get; init; }

	/// <summary>
	/// Path to what changed, as segments (same shape as a diff operation's <c>path</c>) — e.g.
	/// <c>["attributes","Items","modelConfig","filterAttributes"]</c>.
	/// </summary>
	[JsonPropertyName("path")]
	public IReadOnlyList<string> Path { get; init; } = [];

	/// <summary>
	/// The <c>name</c> of the array element that changed. Present only for
	/// <c>"changed-named-element"</c> — the other two kinds have nothing to name.
	/// </summary>
	[JsonPropertyName("entry")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Entry { get; init; }

	/// <summary>
	/// What kind of change it is, which determines the outcome AND the remedy:
	/// <list type="bullet">
	/// <item><description><c>"changed-named-element"</c> — the array element exists in the template under the
	/// same <c>name</c> but the page changed its content. NOT re-applied: the template keeps its own value.
	/// Remedy: if the page's value must win, edit that entry in the diff by hand before pasting.</description></item>
	/// <item><description><c>"changed-scalar"</c> — a scalar inside a template-owned collection config changed
	/// (e.g. a collection's <c>modelConfig.path</c>). DROPPED from the emitted diff so the mobile-correct value
	/// is not clobbered. Same remedy as above.</description></item>
	/// <item><description><c>"nameless-changed-in-place"</c> — the page edited an array element that carries no
	/// <c>name</c>, so it cannot be matched. NOTHING is dropped — the page's element IS inserted — but it will
	/// DUPLICATE the template's own at runtime. Remedy: remove one of the two.</description></item>
	/// </list>
	/// </summary>
	[JsonPropertyName("kind")]
	public string Kind { get; init; }
}

/// <summary>
/// A web→mobile container-name correspondence from the matched template pair. The model uses it
/// to set each component's <c>parentName</c> to the correct mobile container.
/// </summary>
/// <remarks>
/// Names only. The matching rules-file entry also carries a <c>note</c>, and this used to copy it here —
/// free prose, authored in a file that resolves at runtime (env var → cache → CDN) and constrained by no
/// allowlist, landing in the calling agent's context. <c>ContainerMappingRule.Note</c> says on the field
/// itself that it has no reader and must not gain one; this was that reader. Both names are gated on the
/// converter's own container-name syntax before they are copied, for the same reason.
/// </remarks>
public sealed class ContainerMapEntry {
	[JsonPropertyName("web")]
	public string Web { get; init; }

	[JsonPropertyName("mobile")]
	public string Mobile { get; init; }
}

/// <summary>
/// What the conversion DID to every source component of one web type, summarized per type. Derived from the
/// finished <see cref="MobilePageConversionGuide.ViewConfigDiff"/> and
/// <see cref="MobilePageConversionGuide.DroppedElements"/> — it is a report, never a prediction, and never
/// an instruction: the operations are the instruction. A type gets no row here when NO instance of it has
/// an operation of any kind AND the type is observed inside another element's <c>values</c> — a passenger
/// the caller pastes without ever addressing it, so there is nothing to do about it. One instance with an
/// entry of any kind keeps the row for the whole type, so a loss is never erased by a passenger that
/// happens to share its type.
/// </summary>
public sealed class ComponentSuggestion {
	[JsonPropertyName("sourceType")]
	public string SourceType { get; init; }

	/// <summary>Names of the source-page components that have this type.</summary>
	[JsonPropertyName("sourceNames")]
	public IReadOnlyList<string> SourceNames { get; init; } = [];

	/// <summary>
	/// One of the five ComponentMappingCategory values, in the vocabulary the mandated guidance article
	/// defines, decided by the element map's OUTCOME first:
	/// <list type="bullet">
	/// <item><description><c>DirectMapping</c> — an operation was emitted and this same type is the only
	/// suggested one ("same component type exists on mobile").</description></item>
	/// <item><description><c>AlternativeAvailable</c> — an operation was emitted under a DIFFERENT mobile
	/// type ("maps to a different mobile type"): a web grid ships as a finished <c>crt.List</c>. Also the
	/// label when nothing was emitted and the rules file names something to use instead.</description></item>
	/// <item><description><c>WithAdaptation</c> — "transferred, but layout/properties need adjusting". A
	/// judgement no operation carries, so it is reachable ONLY from a rules file that declares it, and only
	/// for a type the map produced nothing for.</description></item>
	/// <item><description><c>Unsupported</c> — no nameable operation and no advice, for a type the WEB
	/// registry knows.</description></item>
	/// <item><description><c>RequiresManualDecision</c> — the same for a type unknown to BOTH registries,
	/// so probably a custom component.</description></item>
	/// </list>
	/// </summary>
	/// <remarks>
	/// <para>
	/// Read this to REPORT the conversion, not to plan it — the operations are the plan. Where an operation
	/// was emitted AND its target can be named, a rules file cannot speak here at all; everywhere else it
	/// may substitute its own declared value, because there is no outcome for it to contradict.
	/// </para>
	/// <para>
	/// Presence in the MOBILE registry is deliberately not evidence of anything — only an emitted operation
	/// is. One consequence to expect rather than read as a bug: an operation CAN be emitted under a type the
	/// mobile registry cannot name (a name-mapped container twin whose mobile type resolves to null), and
	/// such a row falls back to <c>Unsupported</c> / <c>RequiresManualDecision</c> with an empty
	/// <see cref="SuggestedMobileTypes"/> — there is no target to report, and a category asserting one would
	/// name nothing.
	/// </para>
	/// </remarks>
	[JsonPropertyName("category")]
	public string Category { get; init; }

	/// <summary>
	/// The distinct mobile type(s) this web type resolves to: every type the diff ACTUALLY emitted an
	/// operation under, plus any the rules file declares that the diff emits no operation for — a
	/// <c>crt.List</c>'s <c>crt.ListItem</c> row lives inside that list's <c>itemLayout</c>, so no operation
	/// ever names it. A rule can only ADD here; it can never subtract an emitted type. Sorted
	/// case-insensitively, NOT emitted-then-declared: the order carries no meaning and must not be read as
	/// primary-first. One web type can list several because instances diverge — a page's <c>crt.Button</c>s
	/// ship mostly as <c>crt.Button</c>, with one <c>crt.MenuItem</c> where the action moved into a menu.
	/// Empty when nothing was emitted and no rule names an alternative, and also in the rarer case where an
	/// operation WAS emitted under a type the mobile registry cannot name (see <see cref="Category"/>).
	/// </summary>
	[JsonPropertyName("suggestedMobileTypes")]
	public IReadOnlyList<string> SuggestedMobileTypes { get; init; } = [];

	/// <summary>When several web types collapse to one mobile component, explains the merge (many→one).</summary>
	[JsonPropertyName("primaryWebMerge")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string PrimaryWebMerge { get; init; }

	/// <summary>
	/// Advisory text a RULES AUTHOR wrote for this web type, verbatim. Absent unless the published rules file
	/// carries one — the converter synthesizes none. It explains nothing the other fields already state; the
	/// three sentences it used to synthesize were a function of <see cref="Category"/> and were deleted with
	/// the classification that produced them (ENG-95827).
	/// </summary>
	[JsonPropertyName("note")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Note { get; init; }
}

/// <summary>
/// A source element that did NOT reach the mobile page, and why. Separate from
/// <see cref="MobilePageConversionGuide.ViewConfigDiff"/> on purpose: that list holds operations to APPLY,
/// while this is the audit trail of what was not built — for the caller to REPORT, never to act on.
/// </summary>
/// <remarks>
/// Nothing here is derivable from the element map, because a dropped element produces no operation to read a
/// cause off. And the cause is not derivable from the element's TYPE either: on a real
/// <c>Leads_FormPage</c>, 11 of 12 dropped elements have
/// <c>componentSuggestions[].category = "DirectMapping"</c> — a type that converts perfectly well — so a
/// caller seeing only the name and type would read every one of them as conversion loss and re-insert it
/// (ENG-95827).
/// </remarks>
public sealed class DroppedElement {
	/// <summary>The source element's name.</summary>
	[JsonPropertyName("webName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string WebName { get; init; }

	/// <summary>The source element's web component type.</summary>
	[JsonPropertyName("webType")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string WebType { get; init; }

	/// <summary>
	/// Why it was dropped: one or more codes from <see cref="ReasonCodes"/>, each optionally carrying
	/// <c>params</c>. Branch on <c>code</c>; the guidance article says what to tell the user about each.
	/// </summary>
	[JsonPropertyName("reason")]
	public IReadOnlyList<ReasonCode> Reason { get; init; } = [];
}

/// <summary>
/// One coded reason. <see cref="Code"/> is drawn from the closed vocabulary in <see cref="ReasonCodes"/> and
/// is the thing to branch on; <see cref="Params"/> carries the values that would otherwise have been
/// interpolated into a sentence.
/// </summary>
/// <remarks>
/// The reason type of FIVE collections, not of dropped elements alone: <see cref="DroppedElement.Reason"/>,
/// <see cref="DroppedRequest.Reason"/>, <see cref="FlaggedRequest.Reason"/>,
/// <see cref="DroppedPageBusinessRule.Reason"/> and <see cref="NormalizationSkip.Reason"/>. One vocabulary
/// across all five, because a caller reads them the same way and a per-collection vocabulary would let one
/// cause acquire two spellings. Everything a caller must DO about a code lives in the guidance article,
/// keyed by the code — not here and not in the payload.
/// </remarks>
public sealed class ReasonCode {
	/// <summary>The classification, from <see cref="ReasonCodes"/>.</summary>
	[JsonPropertyName("code")]
	public string Code { get; init; }

	/// <summary>
	/// Values specific to this occurrence — a target container name, a row count, the carried property
	/// names. Omitted when the code needs none, which is the common case.
	/// </summary>
	[JsonPropertyName("params")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, JsonNode> Params { get; init; }
}

/// <summary>
/// The closed vocabulary carried by every <see cref="ReasonCode"/>, across all five collections that hold
/// one: why an element, a request binding, a business rule or a normalization did not make it.
/// </summary>
/// <remarks>
/// Every code answers a question the rest of the payload cannot. A code for something that DID convert would
/// not: an entry in <see cref="MobilePageConversionGuide.ViewConfigDiff"/> is a deterministic instruction to
/// apply, and its <c>operation</c>, <c>parentName</c>, <c>propertyName</c> and <c>index</c> already carry the
/// result — so nothing here explains a conversion.
/// <para>
/// A DROP is the opposite: nothing gets built, so there is no instruction to read the cause off, and the
/// dropped element's TYPE does not supply it either — a type that converts perfectly well elsewhere on the
/// page is the common case, which without a code reads as conversion loss, and the natural response to
/// conversion loss is to re-insert. The causes need four DIFFERENT things said to the user: inherited chrome
/// and a positional exclusion are not loss and must not be re-added, an unsupported request IS a lost
/// action, and an emptied container is automatic housekeeping.
/// </para>
/// <para>
/// Named constants rather than inline literals because these are asserted verbatim by the unit and E2E
/// suites and documented one-for-one in the guidance article — the same reason the
/// <c>dataSectionConflicts</c> kinds and the <see cref="ElementMapEntry.ParentSource"/> values are.
/// </para>
/// </remarks>
public static class ReasonCodes {
	// ── Why an element did NOT convert ────────────────
	/// <summary>A container left with no surviving mobile child.</summary>
	public const string DropEmptyContainer = "drop-empty-container";

	/// <summary>
	/// A container with no mobile equivalent: it is NOT recreated, and its children were reparented to
	/// <c>params.newParent</c> (each carries that parent in its own operation, so there is nothing to
	/// apply). Params: <c>newParent</c>.
	/// </summary>
	public const string DropContainerNoMobileEquivalent = "drop-container-no-mobile-equivalent";

	/// <summary>
	/// An <c>excludedComponents</c> rule matched. Params: <c>hostType</c>, <c>host</c>, <c>slot</c> —
	/// <c>slot</c> absent when the rule bans the type from the host's default child collection.
	/// </summary>
	public const string DropExcludedByRule = "drop-excluded-by-rule";

	/// <summary>An ancestor was removed by an <c>excludedComponents</c> rule. Params: <c>ancestor</c>.</summary>
	public const string DropParentExcluded = "drop-parent-excluded";

	/// <summary>
	/// Chrome inherited from the WEB template, which the mobile template provides natively at
	/// <c>params.targetParent</c>.<c>params.targetSlot</c> — the two are separate keys on purpose: a caller
	/// re-adding the element needs a parent NAME, and a dotted <c>"Parent.slot"</c> string used as one is
	/// accepted by the applier and saves the element at the viewConfig root, outside every container.
	/// Params: <c>targetParent</c>, <c>targetSlot</c>, <c>scope</c> — <c>scope</c> absent unless the drop
	/// happened inside a non-converting scope container.
	/// </summary>
	public const string DropInheritedChrome = "drop-inherited-chrome";

	/// <summary>
	/// The conversion target is absent from the produced mobile page — either the mobile template never had
	/// it, or this conversion removed it (a <c>declaredElements</c> receiver that nothing landed in), which
	/// are the same fact to a caller and have the same remedy.
	/// Params: <c>missingParent</c>, <c>scope</c> — named <c>missingParent</c> rather than <c>target</c>
	/// because it is the one parent name in the vocabulary that does NOT exist; reusing the key that
	/// elsewhere names a parent that does exist is how it gets pasted into an operation.
	/// <c>scope</c> absent outside a non-converting scope container.
	/// </summary>
	public const string DropTargetMissing = "drop-target-missing";

	/// <summary>
	/// A component whose request the Mobile app does not support — a <c>crt.Button</c> on the element path,
	/// or any action inside a non-converting scope container. Params: <c>request</c>, <c>scope</c>
	/// (<c>scope</c> absent on the element path). Not to be confused with
	/// <see cref="DropRequestUnsupported"/>: there the element SURVIVES and only its binding is removed.
	/// </summary>
	public const string DropUnsupportedRequest = "drop-unsupported-request";

	/// <summary>
	/// A container the rules declare NON-CONVERTING (<c>nonConvertingScopeContainers</c>, e.g. the web
	/// page's <c>MainHeader</c>): it produces no mobile element of its own, and each of its children is
	/// reported separately with its own code. No params — the record's <c>webName</c> is the scope, and the
	/// children name it in their own <c>params.scope</c>.
	/// </summary>
	/// <remarks>
	/// This code exists because without it the container was the one source element the response reported
	/// NOWHERE: the walk recursed its subtree in scope mode and continued, so no operation and no drop
	/// mentioned it, while the article promises droppedElements accounts for every source element that did
	/// not reach the page (ENG-95827).
	/// </remarks>
	public const string DropNonConvertingScope = "drop-non-converting-scope";

	/// <summary>
	/// The web type has no mobile counterpart in the registry. No params: the type is the record's own
	/// <c>webType</c>, and a param that echoes a sibling field is a second place for the same fact to drift.
	/// </summary>
	public const string DropTypeNotInMobileRegistry = "drop-type-not-in-mobile-registry";

	/// <summary>
	/// A request absent from the conversion map — CUSTOM or unknown, not known-unsupported. clio cannot
	/// assert it is unavailable on mobile, only that it does not know it. Params: <c>request</c>,
	/// <c>scope</c>.
	/// </summary>
	public const string DropUnknownRequest = "drop-unknown-request";

	/// <summary>
	/// No conversion rule matches this component inside a non-converting scope. Params: <c>scope</c>.
	/// </summary>
	public const string DropNoRuleInScope = "drop-no-rule-in-scope";

	/// <summary>
	/// Inside a non-converting scope and not itself a placeable action (no own convertible <c>clicked</c>).
	/// Its nested actions are still flattened. Params: <c>scope</c>.
	/// </summary>
	public const string DropNotAnActionInScope = "drop-not-an-action-in-scope";

	// ── Why a request BINDING did not convert ────────
	// These describe the BINDING, not the element. The rule is narrower than an earlier draft of this comment
	// claimed, and the difference matters: a binding reuses the ELEMENT's own code object only where nothing
	// distinguishes the binding's loss from the element's — the scope path and the leaf missing-target path,
	// where the two records are one fact and a second vocabulary could only drift from the first. Where the
	// binding carries something the element's code does not, it gets a code of its own that names the entry
	// to look up instead: inherited chrome (the native control's request may differ from the web one) and
	// the two reconciliation passes. Read as "always reuse", this comment would invite collapsing three
	// distinct codes into one (ENG-95827).

	/// <summary>
	/// The element was dropped as inherited chrome and the mobile template's native control carries its own
	/// action. Nothing is lost when the source bound the platform's standard request; a CUSTOM request on an
	/// inherited button IS lost, which is the reason the binding is reported at all rather than dropped
	/// silently. Params: NONE — the record's own <c>elementName</c> and <c>webRequest</c> already name the
	/// element and the request, and a param repeating a sibling field is the redundancy this ticket removes.
	/// </summary>
	public const string DropRequestChromeNative = "drop-request-chrome-native";

	/// <summary>
	/// The request is KNOWN-unsupported on mobile, so the binding was removed while the component itself
	/// still renders. Params: <c>note</c> only, when the conversion rule authors one — the request is already
	/// the record's <c>webRequest</c>.
	/// </summary>
	public const string DropRequestUnsupported = "drop-request-unsupported";

	/// <summary>
	/// The request TYPE converts, but its navigation TARGET cannot exist on mobile, so the binding was
	/// removed while the component itself still renders. Emitted only for a DEFINITIONAL absence — a
	/// verdict that needed no environment read — never for one a probe merely failed to confirm; the
	/// softer verdicts are reported in <c>requestConversions.unresolvedTargetRequests</c> and remove
	/// nothing. Params: <c>targetKind</c> and <c>target</c>, which is the pair that says what to fix.
	/// </summary>
	public const string DropRequestTargetMissing = "drop-request-target-missing";

	/// <summary>
	/// The binding was discarded with its container, which the empty-container pass removed after the
	/// binding had been recorded. Params: none — the container's own <c>droppedElements</c> entry
	/// (<see cref="DropEmptyContainer"/>) carries the detail.
	/// </summary>
	public const string DropRequestElementEmptyContainer = "drop-request-element-empty-container";

	/// <summary>
	/// The binding was discarded with its element, which an <c>excludedComponents</c> rule removed after the
	/// binding had been recorded. Params: none — the element's own <c>droppedElements</c> entry
	/// (<see cref="DropExcludedByRule"/> or <see cref="DropParentExcluded"/>) carries the detail.
	/// </summary>
	public const string DropRequestElementExcluded = "drop-request-element-excluded";

	// ── Why a request was KEPT but needs review ──────
	/// <summary>
	/// The request is in neither the conversion map nor the bundled set, so the binding was kept VERBATIM
	/// for manual verification — the component works, the action may or may not. Params: NONE — the record's
	/// own <c>request</c> field names it.
	/// </summary>
	public const string FlagRequestUnmapped = "flag-request-unmapped";

	// ── Why a page BUSINESS RULE did not convert ─────
	/// <summary>
	/// The condition mixes AND and OR across nested groups; the flat single-operator mobile input cannot
	/// represent it without changing when the rule fires. Params: none.
	/// </summary>
	public const string DropRuleConditionMixedAndOr = "drop-rule-condition-mixed-and-or";

	/// <summary>
	/// The condition uses a comparison operator with no supported mobile equivalent; emitting it would
	/// silently change the comparison. Params: none.
	/// </summary>
	public const string DropRuleConditionUnsupportedComparison = "drop-rule-condition-unsupported-comparison";

	/// <summary>
	/// The condition cannot be converted and the cause is not classified further. Params: none.
	/// </summary>
	/// <remarks>
	/// UNREACHABLE today and deliberately kept: <see cref="PageRuleConditionIssue"/> has exactly
	/// <c>MixedAndOr</c> and <c>UnrecognizedComparison</c> besides <c>None</c>, and both have their own code,
	/// so this is the default arm of that switch. It exists so a FUTURE condition issue reports as an
	/// unclassified drop instead of crashing the converter or silently reusing a code that names the wrong
	/// cause. There is therefore no test that produces it — see <c>MobileDropReasonCodeVocabularyTests</c>.
	/// </remarks>
	public const string DropRuleConditionUnconvertible = "drop-rule-condition-unconvertible";

	/// <summary>
	/// Every element the rule's actions reference was dropped, so no action converts. Params: none.
	/// </summary>
	public const string DropRuleNoActionConverts = "drop-rule-no-action-converts";

	// ── Why a NORMALIZATION was skipped ──────────────
	/// <summary>
	/// The element carries a NON-OBJECT value at the path — typically a whole-value binding — and a merging
	/// rule never overwrites one, so the element keeps its source value there. Params: none; the entry's own
	/// <c>properties</c> name the refused paths.
	/// </summary>
	public const string SkipNormalizationPathBlocked = "skip-normalization-path-blocked";

}

/// <summary>
/// Caption/resource convention for a newly inserted named element. <see cref="Key"/> is UNIQUE to the
/// element (<c>&lt;mobileName&gt;_caption</c>) — never the web element's inherited key — so it cannot collide
/// with a caption key the mobile template already owns or change other elements sharing that key.
/// The caller registers <see cref="Key"/> = <see cref="SourceValue"/>
/// (the web caption's resolved en-US text) via <c>update-page resources</c>; the inserted element's caption
/// token references the same <see cref="Key"/>.
/// <para>
/// Present ONLY when the source page declares the key the web caption references. Its presence is therefore
/// the invariant that the invented <see cref="Key"/> always has a registration behind it: an element whose
/// caption key the page never declared gets none of this — its token is carried verbatim so the platform
/// resolves it from the entity column, exactly as every mobile template does.
/// </para>
/// </summary>
public sealed class CaptionResource {
	[JsonPropertyName("key")]
	public string Key { get; init; }

	[JsonPropertyName("sourceValue")]
	public string SourceValue { get; init; }
}

/// <summary>
/// ONE operation of the mobile page's <c>viewConfigDiff</c>, in the mobile diff applier's own shape —
/// nothing else. Apply the list in order; there is nothing to add.
/// </summary>
/// <remarks>
/// The shape is the applier's, verified against it rather than invented: <c>Insert</c> resolves its
/// target through <c>parentName</c> + <c>propertyName</c>, reads the position from <c>index</c> and the
/// component from <c>values</c>, and a <c>merge</c> resolves by <c>name</c> alone. Only <c>insert</c>
/// and <c>merge</c> appear here; <c>set</c>, <c>move</c> and <c>remove</c> exist in the applier but the
/// converter emits none of them today.
/// <para>
/// It carries NO conversion metadata, so there is nothing to transcribe: the source correspondence is
/// <c>nameMap</c> (renames only — everything else joins to <c>sourceStructure</c> by name), an
/// unresolvable parent is <c>unresolvedParents</c>, a referenced localized string is
/// <c>resourceStrings</c>, and an element that did not convert is <c>droppedElements</c>.
/// </para>
/// </remarks>
public sealed class ViewConfigDiffOperation {
	/// <summary><c>insert</c> or <c>merge</c>.</summary>
	[JsonPropertyName("operation")]
	public string Operation { get; init; }

	/// <summary>The mobile element this operation addresses.</summary>
	[JsonPropertyName("name")]
	public string Name { get; init; }

	/// <summary>The container to insert into. Absent on a <c>merge</c>, which resolves by name.</summary>
	[JsonPropertyName("parentName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ParentName { get; init; }

	/// <summary>
	/// The parent's child collection. ALWAYS present on an <c>insert</c> — including when it is the default
	/// <c>items</c>, which the applier would have assumed anyway — because this list is meant to be pasted
	/// and an explicit slot is one less thing a reader must know about the applier to trust what they are
	/// pasting. Absent on a <c>merge</c>, which resolves by <c>name</c> alone. (This summary previously said
	/// "absent when it is the default items", which would have a caller read a present
	/// <c>propertyName: "items"</c> as a NON-default slot — the one inference the field exists to prevent.)
	/// </summary>
	[JsonPropertyName("propertyName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string PropertyName { get; init; }

	/// <summary>0-based position within the parent's collection. Absent to append.</summary>
	[JsonPropertyName("index")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public int? Index { get; init; }

	/// <summary>
	/// The component values. On an <c>insert</c> this carries the <c>type</c> and EVERY source property
	/// except the element's <c>name</c> — the value binding (<c>control</c>, the same wire name on both
	/// sides) included, and deliberately without pruning against the mobile registry while that registry
	/// publishes no real per-component property list; on a <c>merge</c> only the delta over what the
	/// template provides, with no <c>type</c>. A merge with no delta carries an EMPTY object <c>{}</c>,
	/// never null and never absent: the applier lists <c>values</c> as a required parameter of
	/// <c>merge</c> and validates every operation before applying any, so one missing value fails the
	/// whole array.
	/// </summary>
	[JsonPropertyName("values")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonNode Values { get; init; }
}

/// <summary>
/// An <c>insert</c> whose <c>parentName</c> is provided by NEITHER the diff nor the probed mobile
/// template. Report the name and stop; do not guess.
/// </summary>
/// <remarks>
/// Inserting into it throws, and authoring it may duplicate something the template owns under another
/// name. It is a conversion-RULES defect, not a page defect: a <c>containers</c> mapping names a mobile
/// container the target template does not have. The shipped rules reach it —
/// <c>BlankPageTemplate</c> maps <c>MainContainer -&gt; MainContainer</c>, but
/// <c>BlankMobilePageTemplate</c> is a standalone bare <c>crt.Scaffold</c> with no
/// <c>MainContainer</c> (ENG-95827).
/// <para>
/// Only this case is reported. A parent the diff itself inserts, or one the probed template provides,
/// needs no field: the caller can see whether the name appears in <c>viewConfigDiff</c>.
/// </para>
/// </remarks>
public sealed class UnresolvedParent {
	/// <summary>The mobile element whose parent could not be resolved.</summary>
	[JsonPropertyName("name")]
	public string Name { get; init; }

	/// <summary>The parent name nothing provides.</summary>
	[JsonPropertyName("parentName")]
	public string ParentName { get; init; }
}

/// <summary>
/// The closed vocabulary of <see cref="ElementMapEntry.Operation"/>. Named constants rather than inline
/// literals for the same reason <see cref="ReasonCodes"/> is: one spelling, one comparer. Only
/// <see cref="Insert"/> and <see cref="Merge"/> reach the wire — the wire projection is an ALLOW-list, so a
/// working-map operation added here fails to reach the applier payload rather than landing in it silently.
/// </summary>
internal static class ElementMapOperations {
	/// <summary>Create this element on the mobile page. Reaches <c>viewConfigDiff</c>.</summary>
	internal const string Insert = "insert";

	/// <summary>Layer onto an element the mobile template already provides. Reaches <c>viewConfigDiff</c>.</summary>
	internal const string Merge = "merge";

	/// <summary>The element did not reach the page. Projected into <c>droppedElements</c>.</summary>
	internal const string Drop = "drop";

	/// <summary>
	/// The container is not recreated and its children are reparented. Projected into
	/// <c>droppedElements</c> too — from the page's point of view the container is gone.
	/// </summary>
	internal const string RelocateChildren = "relocate-children";
}

/// <summary>
/// Instance-level conversion decision for ONE named element of the source page (ENG-89620). CONVERTER
/// BOOKKEEPING ONLY — never serialized. It is the working shape every pass mutates; the response is
/// projected out of it into <c>viewConfigDiff</c> + <c>droppedElements</c> + the metadata siblings.
/// </summary>
public sealed class ElementMapEntry {
	/// <summary>
	/// Source element name. Omitted for an entry with no web counterpart: a SYNTHESIZED container (the
	/// tab-body / Area layers of a converted tab) or an element the template rule DECLARES
	/// (<c>declaredElements</c>, see <see cref="DeclaredByRule"/>). A synthesized entry converts and carries
	/// no reason of any kind; either way it is applied exactly like any other <c>insert</c>.
	/// </summary>
	public string WebName { get; init; }

	public string WebType { get; init; }

	/// <summary>One of <see cref="ElementMapOperations"/>.</summary>
	public string Operation { get; init; }

	/// <summary>Target element name on mobile (merge / insert).</summary>
	public string Name { get; init; }

	/// <summary>Target mobile type (insert / merge), when known to the mobile registry.</summary>
	public string MobileType { get; init; }

	/// <summary>
	/// Mobile parent element to attach to. For <c>insert</c> it is the element's parent; for
	/// <c>relocate-children</c> it is the container the element's children are placed into instead.
	/// Settable (like <see cref="Values"/>): the tab-area pass retargets a tab's
	/// top-level content onto the synthesized Area container after the element map is built.
	/// </summary>
	public string ParentName { get; set; }

	/// <summary>
	/// Parent property to insert into (insert); defaults to <c>items</c>. Settable (like
	/// <see cref="ParentName"/>) for the same reason: the tab-area pass retargets a tab's top-level content onto
	/// the synthesized Area container, and the slot travels with the parent — a child the web page kept in the
	/// tab's <c>tools</c> strip lands in the Area's <c>items</c>, the only child collection a
	/// <c>crt.GridContainer</c> declares.
	/// </summary>
	public string PropertyName { get; set; }

	/// <summary>
	/// Where this entry's <see cref="ParentName"/> comes from. Set on EVERY <c>insert</c> that names a parent,
	/// and only on those — the other operations do not insert anything into a parent. One of:
	/// <list type="bullet">
	/// <item><description><c>"template"</c> — this map does not create the parent AND the probed mobile
	/// template provides it (<c>MainContainer</c>, or <c>FloatingActionButton</c> via the Scaffold's
	/// <c>floatAction</c> slot). The child insert stands on its own; the parent may still carry its own
	/// <c>merge</c>, which is how a shifted <c>layoutConfig</c> and the empty-slot seeding reach the page.
	/// </description></item>
	/// <item><description><c>"page"</c> — the parent is inserted by this map, from the source page.
	/// </description></item>
	/// <item><description><c>"converter"</c> — the parent is inserted by this map and was synthesized (a
	/// tab-body grid or its Area card), so it carries no <see cref="WebName"/>.</description></item>
	/// <item><description><c>"unknown"</c> — NEITHER this map nor the template provides it. This is the one
	/// value that reaches the caller, projected into
	/// <see cref="MobilePageConversionGuide.UnresolvedParents"/>, which owns the caller-facing contract.
	/// </description></item>
	/// </list>
	/// </summary>
	/// <remarks>
	/// Derived in ONE pass over the finished map (<c>WebToMobileAnalysisService.StampParentSource</c>) rather
	/// than per retarget path, so an ORDINARY insert into a template-provided parent is answered the same way
	/// a retargeted one is.
	/// <para>
	/// "Not created by this map" is NOT the same question as "the template provides it", and conflating the
	/// two is why <c>"unknown"</c> exists. The shipped rules reach that state: <c>BlankPageTemplate</c> maps to
	/// <c>BlankMobilePageTemplate</c> with a <c>MainContainer -&gt; MainContainer</c> container pair, but
	/// mobile blank is a STANDALONE root — a bare <c>crt.Scaffold</c> — and <c>MainContainer</c> comes from
	/// <c>BaseMobileTemplate</c>, a different root it does not derive from. So the template's node set is
	/// consulted here and <c>"template"</c> is claimed only when that set actually contains the parent.
	/// </para>
	/// </remarks>
	public string ParentSource { get; set; }

	/// <summary>
	/// Optional 0-based insert position within the parent's <c>items</c>. Set for a positional insert — a
	/// web element mapped above/below an anchor container via a <c>&lt;container&gt;:top</c> /
	/// <c>:bottom</c> template rule (<c>:top</c> elements get an ascending index from 0 so they land before
	/// the anchor, e.g. above the mobile <c>Tabs</c>; <c>:bottom</c> elements are appended, no index) — and
	/// for every CONVERTED WEB TAB under the mobile Tabs (indexed right after the template's general tab so
	/// the template's Feed/Attachments tabs stay last — always, the converter owns this ordering).
	/// Add it to the insert operation verbatim when present. Omitted for every other element — the mobile
	/// designer owns ordering. Settable (like <see cref="ParentName"/>): the empty-container removal pass
	/// re-compacts sibling indexes after dropping an empty positional sibling, and the
	/// converted-tab placement pass assigns tab indexes after the element map is built.
	/// </summary>
	public int? Index { get; set; }

	/// <summary>For an <c>insert</c> of a named element with a localizable caption.</summary>
	public CaptionResource CaptionResource { get; init; }

	/// <summary>
	/// True for the entry of an element DECLARED by the template rule's <c>declaredElements</c> (no web counterpart,
	/// so <see cref="WebName"/> is null) — its <c>insert</c>, or the <c>drop</c> that replaces it when nothing lands in
	/// it. Converter bookkeeping only — it lets the empty-container pass treat the
	/// declared container like a converted one (removed when nothing lands in it), which a synthesized tab-body
	/// layer must never be. Not part of the guide contract; the entry's <c>reason</c> says where it came from.
	/// </summary>
	[JsonIgnore]
	internal bool DeclaredByRule { get; init; }

	/// <summary>
	/// The prebuilt, ready-to-paste mobile component <c>values</c>. For an <c>insert</c> it carries the
	/// component <c>type</c> and EVERY source property the mobile component supports (per the mobile
	/// registry) — copied verbatim, with only mobile-unsupported properties pruned; paste it as the inserted
	/// component's <c>values</c> WITHOUT dropping anything, then add ONLY the value binding (e.g.
	/// <c>control</c>, or <c>value</c> for lookups), which is type-specific and intentionally left out. For a
	/// <c>merge</c> twin it carries the page's parameters onto the template-provided element with no
	/// <c>type</c> — the whitelisted keys when the rule declares <c>carryProperties</c>, otherwise the page's
	/// DELTA over the web-template baseline for a same-component twin (e.g. crt.FileList → crt.FileList): only
	/// what the page changed, so a property left at the template default is omitted and the mobile element
	/// keeps its own default; merge them by name. Null when there is nothing prebuilt (a structural/advisory
	/// merge, an unchanged same-component twin, or an operation that carries no values).
	/// </summary>
	public JsonNode Values { get; set; }

	/// <summary>
	/// Converter bookkeeping, NEVER serialized on this entry: why a <c>drop</c> happened. Projected into
	/// <see cref="MobilePageConversionGuide.DroppedElements"/> when the response is assembled.
	/// </summary>
	/// <remarks>
	/// It lives on the entry because the passes need it there: a drop REPLACES an entry in place
	/// (<c>elementMap[i] = Drop(...)</c>) so that the orphan cascade and the empty-container cascade can see
	/// it while they walk. Only the split into <c>elementMap</c> + <c>droppedElements</c> happens at the end.
	/// <para>
	/// An entry that CONVERTS carries no reason at all. <see cref="ReasonCodes"/> explains why.
	/// </para>
	/// </remarks>
	public IReadOnlyList<ReasonCode> Reason { get; set; }

	/// <summary>
	/// Converter bookkeeping, never serialized: the MOBILE anchor name (e.g. <c>Tabs</c>) when this
	/// <c>insert</c> was routed by a <c>&lt;anchor&gt;:top</c> / <c>:bottom</c> template rule. The anchor-row
	/// pass counts the entries the RULE routed, never "every indexed insert under that parent" — an ordinary
	/// insert can legitimately target the same mobile container and must not shift the anchor.
	/// </summary>
	internal string PositionalAnchor { get; set; }

	/// <summary>
	/// Converter bookkeeping, never serialized: for a container TWIN (<c>merge</c>) the mobile container it
	/// sits inside. A merge carries no <c>parentName</c> on purpose — the caller reuses the element the
	/// template already provides and inserts nothing — but the adaptive-layout pass still needs to know the
	/// twin is a SIBLING of the inserts it places: a mobile <c>crt.GridContainer</c> positions children by
	/// <c>layoutConfig</c> only, so a twin left unplaced beside placed siblings is not rendered at all.
	/// </summary>
	internal string MergeParentName { get; set; }
}

/// <summary>
/// Compact, inline contract for one mobile component type the diff EMITS, drawn from the mobile registry, so
/// the model can read and adjust the component's <c>values</c> without extra get-component-info round-trips.
/// </summary>
/// <remarks>
/// The set follows <see cref="ComponentSuggestion.SuggestedMobileTypes"/>, which is now derived from the
/// emitted operations, intersected with what the mobile registry carries. While it was derived from a type
/// table instead, the set was wrong in both directions on the reference page — no contract for the
/// <c>crt.List</c> the diff inserted five times, and a contract for a <c>crt.SearchFilter</c> every
/// instance of which was dropped. The registry intersection is a REMAINING hole of the same shape, not a
/// design choice: an emitted type the registry does not carry still ships without its contract
/// (ENG-95827).
/// </remarks>
public sealed class MobileComponentContract {
	[JsonPropertyName("componentType")]
	public string ComponentType { get; init; }

	/// <summary>
	/// Whether the mobile registry declares this type a container. ABSENT when it declares nothing, which
	/// today is every type — read an absent value as "unknown", never as "no". It used to ship a hard
	/// <c>false</c> for a key no entry publishes, contradicting this same response's own parent graph, while
	/// the sibling <c>get-component-info</c> surface omitted the same silence (ENG-95827).
	/// </summary>
	[JsonPropertyName("container")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? Container { get; init; }

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
	/// <summary>
	/// True when a SysModule row references the source page as its section / list page. ABSENT when the
	/// environment could not be queried — see <see cref="ProbeOk"/>. It used to be a non-nullable bool, so a
	/// failed probe shipped <c>false</c> for it and for every flag below, indistinguishable from a measured
	/// "no", under a section header that calls these read-only FACTS (ENG-95827).
	/// </summary>
	[JsonPropertyName("sourcePageIsSection")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? SourcePageIsSection { get; init; }

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

	/// <summary>
	/// True when MobileSectionSchemaUId is already set to a non-empty schema UId. Absent when the probe
	/// failed, and absent when the source page is not a section at all.
	/// </summary>
	[JsonPropertyName("mobileSectionRegistered")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public bool? MobileSectionRegistered { get; init; }

	/// <summary>
	/// True when the source page is an edit/form page (vs a list/section page). Present even on a failed
	/// probe: it is read from the page itself, not from the environment.
	/// </summary>
	[JsonPropertyName("isFormPage")]
	public bool IsFormPage { get; init; }

	/// <summary>Workplaces the source section is currently a member of.</summary>
	[JsonPropertyName("currentWorkplaces")]
	public IReadOnlyList<WorkplaceInfo> CurrentWorkplaces { get; init; } = [];

	/// <summary>Workplaces of the Mobile client type the section could be added to.</summary>
	[JsonPropertyName("availableMobileWorkplaces")]
	public IReadOnlyList<WorkplaceInfo> AvailableMobileWorkplaces { get; init; } = [];

	/// <summary>
	/// The registration steps to propose to the user at Gate S, as authored text. The one prose channel
	/// this section keeps on purpose: each step names a clio tool and its arguments, so it is a procedure
	/// the caller executes rather than a description of the fields beside it.
	/// </summary>
	[JsonPropertyName("registrationActions")]
	public IReadOnlyList<string> RegistrationActions { get; init; } = [];

	[JsonPropertyName("note")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Note { get; init; }

	/// <summary>
	/// False when the environment could not be queried. READ THIS FIRST: on false, every environment-derived
	/// flag above is ABSENT rather than false, and nothing here may be reported to the user as established.
	/// </summary>
	[JsonPropertyName("probeOk")]
	public bool ProbeOk { get; init; }
}

/// <summary>
/// One mobile page that already exists for the entity/page currently being converted — the fact behind
/// the "reuse or convert again" check (playbook step 2a). See
/// <see cref="MobilePageConversionGuide.ExistingMobilePages"/> for scope and construction.
/// </summary>
public sealed class ExistingMobilePageInfo {
	[JsonPropertyName("schemaName")]
	public string SchemaName { get; init; }

	[JsonPropertyName("schemaUId")]
	public string SchemaUId { get; init; }

	/// <summary>
	/// How this existing page was found: <c>section</c> (the source list/section page's
	/// <c>SysModule.MobileSectionSchemaUId</c> is already set) or <c>entity-default-mobile-page</c> (the
	/// source page's bound entity already has a default mobile edit page, via the same
	/// <c>MobileRelatedPage</c> add-on read <c>MobileActionTargetProbe</c> performs for other objects).
	/// </summary>
	[JsonPropertyName("source")]
	public string Source { get; init; }
}

/// <summary>
/// The page-side inputs the reuse-vs-convert check reads. Grouped into a record rather than passed as loose
/// parameters (mirrors <see cref="MobileActionTargetProbeRequest"/>): they all describe ONE source page, and
/// they travel together to <see cref="ExistingMobilePageProbe.Probe"/>.
/// </summary>
/// <param name="SectionRegistration">The source page's SysModule registration, already probed by <c>MobileSectionRegistrationProbe</c>.</param>
/// <param name="IsFormPage">Whether the source page is an edit/form page (vs a list/section page).</param>
/// <param name="ModelConfig">The source page's merged <c>modelConfig</c>, used to find its bound entities.</param>
/// <param name="PagePackageUId">The source page's package UId, used to address the entity add-on read.</param>
/// <param name="TargetName">The schema name this conversion is about to create/update.</param>
public sealed record ExistingMobilePageProbeRequest(
	SectionRegistrationInfo SectionRegistration,
	bool IsFormPage,
	JsonObject ModelConfig,
	string PagePackageUId,
	string TargetName);

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

	/// <summary>
	/// Diagnostic set only when the converted layout came back empty despite the source page having
	/// components — e.g. <c>"empty: …"</c> when the web-template baseline could not be resolved to a
	/// distinct template (a replacing schema over a same-named base). Null in the normal case; a caller
	/// must not mistake an empty layout for a legitimately layout-less page.
	/// </summary>
	[JsonPropertyName("layoutResolution")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string LayoutResolution { get; init; }

	/// <summary>Web-only body sections present on the source (handlers / validators / converters).</summary>
	[JsonPropertyName("webOnlySections")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> WebOnlySections { get; init; }

	/// <summary>
	/// Data source names declared on the source page. All of them: a real record page declares a dozen, and
	/// they are carried over in full. This used to say "(mobile supports one)", which contradicted
	/// <see cref="ModelConfig"/> two fields below — whose own doc orders every attribute kept exactly as
	/// provided and names the failure that pruning causes (<c>Item with the path … not found</c>). Reading
	/// the old parenthesis as an instruction meant deleting eleven data sources and breaking every
	/// <c>modelConfig.path</c> in the view-model diff (ENG-95827).
	/// </summary>
	[JsonPropertyName("dataSources")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<string> DataSources { get; init; }

	// ── Data sections (apply to the mobile body via *Diff) ────────────
	/// <summary>
	/// The source page's full merged <c>modelConfig</c> (data sources + attributes), for REFERENCE only.
	/// The paste target is <see cref="ModelConfigDiff"/>, which is derived from this and is the only one of
	/// the two the caller writes. Do NOT apply this object: the cheapest way to "apply it verbatim" is a
	/// single root merge, which <see cref="ModelConfigDiff"/>'s own doc forbids because it drops the
	/// template's baseline arrays. Present so the caller can SEE what the diff was built from — in
	/// particular that every attribute keeps all of its declared metadata, since dropping any of it makes
	/// the binding unresolvable in Mobile Designer (<c>Item with the path … not found</c>). Null when the
	/// source page declares no model config.
	/// </summary>
	[JsonPropertyName("modelConfig")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonNode ModelConfig { get; init; }

	/// <summary>
	/// The source page's merged <c>viewModelConfig</c>, already FILTERED for mobile: attributes referenced
	/// only by dropped/unsupported components are removed (see <see cref="ViewConfigDiff"/>). For REFERENCE
	/// only — the paste target is <see cref="ViewModelConfigDiff"/>, derived from this. Reference only OOTB
	/// mobile converters — a definitive mobile converter list is forthcoming; flag any custom converter for
	/// manual review. Null when none is declared.
	/// </summary>
	[JsonPropertyName("viewModelConfig")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonNode ViewModelConfig { get; init; }

	/// <summary>
	/// Ready-to-paste <c>modelConfigDiff</c> built from <see cref="ModelConfig"/> — a set of FOCUSED targeted
	/// merges (one merge per top-level key, e.g. <c>["dataSources"]</c>, plus per-array overrides unioned with
	/// the mobile template's own native arrays), not a single root merge — mirroring the diff shape a hand-built
	/// mobile page emits so the diff engine's array-replace never silently drops the template baseline (see
	/// <c>WebToMobileAnalysisService.SplitModelConfigRootMerge</c>). Paste it VERBATIM as the mobile page's
	/// <c>modelConfigDiff</c>; do NOT hand-build it, collapse it back into one root merge, or source it from a
	/// pre-existing body (that is how attribute <c>type</c> metadata gets dropped). Null when there is no model config.
	/// </summary>
	[JsonPropertyName("modelConfigDiff")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonNode ModelConfigDiff { get; init; }

	/// <summary>
	/// Ready-to-paste <c>viewModelConfigDiff</c> built from the filtered <see cref="ViewModelConfig"/> — a
	/// set of FOCUSED targeted merges (a page-owned <c>["attributes"]</c> merge, per-collection
	/// <c>viewModelConfig.attributes</c> augments, and per-array <c>modelConfig</c> overrides unioned with the
	/// mobile template's own native arrays), not a single root merge — mirroring the diff shape a hand-built
	/// mobile page emits so the diff engine's array-replace never silently drops the template baseline (see
	/// <c>WebToMobileAnalysisService.SplitRootMergeIntoTargetedMerges</c>). Paste it VERBATIM as the mobile
	/// page's <c>viewModelConfigDiff</c>. Null when none.
	/// </summary>
	[JsonPropertyName("viewModelConfigDiff")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public JsonNode ViewModelConfigDiff { get; init; }

	/// <summary>
	/// Every change to a template-owned data-section value that neither diff can express, one entry per
	/// occurrence. Null when there are none, which is the normal case. See <see cref="DataSectionConflict"/>
	/// for what each <c>kind</c> costs and how to fix it — the three do not share one outcome, and two of them
	/// need opposite remedies, so read them individually rather than as one warning.
	/// </summary>
	[JsonPropertyName("dataSectionConflicts")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<DataSectionConflict> DataSectionConflicts { get; init; }

	// ── Template recommendation ───────────────────────────────────────
	[JsonPropertyName("recommendedMobileTemplate")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string RecommendedMobileTemplate { get; init; }

	/// <summary>
	/// How <see cref="RecommendedMobileTemplate"/> was chosen: <c>"matched"</c> when a conversion rule pairs
	/// this page's web template with a mobile counterpart, <c>"generic-fallback"</c> when none does and the
	/// recommendation is the rules' generic mobile base. Absent when the rules declare no default and no
	/// rule matched, i.e. there is no recommendation to qualify.
	/// </summary>
	/// <remarks>
	/// On <c>"generic-fallback"</c> NO container or component name correspondence is known —
	/// <see cref="ContainerMap"/> is empty and every element is placed where the source tree puts it. Read
	/// this field rather than inferring the same thing from an empty <c>containerMap</c>: that inference was
	/// regression-pinned and documented nowhere, and the alternative it replaced was an English
	/// <c>templateNote</c> on a response whose tool description asserts it carries no prose. The
	/// review-in-the-designer advisory that note also carried is procedure, and lives in the guidance
	/// article (ENG-95827).
	/// </remarks>
	[JsonPropertyName("templateMatch")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string TemplateMatch { get; init; }

	[JsonPropertyName("containerMap")]
	public IReadOnlyList<ContainerMapEntry> ContainerMap { get; init; } = [];

	// ── What happened, per source component type ────────────────────
	/// <summary>
	/// One row per distinct source web type, saying what the conversion did to its instances. Derived from
	/// the finished diff, for the conversion gate's report — never a plan, and never a second opinion the
	/// caller weighs against the operations.
	/// </summary>
	[JsonPropertyName("componentSuggestions")]
	public IReadOnlyList<ComponentSuggestion> ComponentSuggestions { get; init; } = [];

	/// <summary>
	/// The mobile page's <c>viewConfigDiff</c>, ready to apply in order. PASTE IT as the page's
	/// <c>viewConfigDiff</c> — there is nothing to add: every source property, the value binding
	/// (<c>control</c>) included, is already in each operation's <c>values</c>. Do not rebuild the
	/// operations, rename their fields, or infer merge-vs-insert from <c>containerMap</c>.
	/// </summary>
	/// <remarks>
	/// <c>name</c> is NOT unique in this array: apply the operations IN ORDER and never deduplicate them by
	/// name. Two operations may legitimately target one element — a merge that shifts a template-provided
	/// container's <c>layoutConfig</c> beside one that fills its properties — and keeping "the cleaner one"
	/// discards a shift nothing else reports. The one duplicate that USED to invite that choice, a
	/// payload-free merge twin beside an operation that already declares the element, is no longer emitted;
	/// a payload-free merge that arrives alone is still meant to be applied as-is.
	/// <para>
	/// Every entry is an applier operation and nothing else. What did NOT convert is not an operation, so
	/// it is in <see cref="DroppedElements"/>; the source correspondence is in <see cref="NameMap"/>; a
	/// parent nothing provides is in <see cref="UnresolvedParents"/> (ENG-95827).
	/// </remarks>
	[JsonPropertyName("viewConfigDiff")]
	public IReadOnlyList<ViewConfigDiffOperation> ViewConfigDiff { get; init; } = [];

	/// <summary>
	/// Source element name → mobile element name, for the elements the converter RENAMED. Everything else
	/// keeps its name, so it joins to <c>sourceStructure</c> directly; a name in
	/// <see cref="ViewConfigDiff"/> that appears in neither was synthesized by the converter. Null when
	/// nothing was renamed.
	/// </summary>
	/// <remarks>
	/// Renames are rare — 5 of 155 entries on a real <c>Leads_FormPage</c> — which is why this is a map of
	/// the exceptions rather than a per-operation <c>webName</c>/<c>webType</c> pair repeated 155 times.
	/// </remarks>
	[JsonPropertyName("nameMap")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, string> NameMap { get; init; }

	/// <summary>
	/// Inserts whose parent is provided by NEITHER this diff nor the probed mobile template — a
	/// conversion-rules defect to report rather than work around. Null in the normal case.
	/// </summary>
	[JsonPropertyName("unresolvedParents")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<UnresolvedParent> UnresolvedParents { get; init; }

	/// <summary>
	/// Source elements that did NOT reach the mobile page, with a coded reason each. Nothing to apply —
	/// REPORT these to the user, and re-insert none of them. Null when every element converted.
	/// </summary>
	[JsonPropertyName("droppedElements")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<DroppedElement> DroppedElements { get; init; }

	/// <summary>
	/// Inline contracts for the mobile component types the <c>viewConfigDiff</c> emits THAT THE MOBILE
	/// REGISTRY CARRIES. Not every emitted type: a type absent from the registry silently gets no contract,
	/// and <c>crt.MenuItem</c> — which the bundled rules emit for a header action retargeted into a
	/// floating-action menu — is absent from the captured registry today. Read a missing contract as "not
	/// described here", never as "do not build this": the operation is still in the diff and still gets
	/// pasted.
	/// </summary>
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

	/// <summary>
	/// Mobile page(s) that already exist for the entity/page being converted — the reuse-vs-convert fact
	/// (playbook step 2a). Checked identically on the original page and on every step-8a follow-up: a
	/// section source checks <see cref="SectionRegistrationInfo.MobileSectionRegistered"/>, a form source
	/// checks the bound entity's <c>MobileRelatedPage</c> add-on. A match naming the SAME schema this run
	/// is about to create/update is excluded (nothing to "reuse vs convert again" when it is the very page
	/// being built). Empty when none was found or the probe could not run.
	/// </summary>
	[JsonPropertyName("existingMobilePages")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<ExistingMobilePageInfo> ExistingMobilePages { get; init; } = [];

	// ── Page-level business rules (advisory conversion) ───────────────
	/// <summary>
	/// Page-level business rules of the source page, deterministically converted for the mobile page.
	/// Object-/entity-level rules are shared across web and mobile and are intentionally NOT touched.
	/// Each converted rule keeps its condition verbatim and only the actions that survive on mobile
	/// (a hide/show/make-* action survives only for the referenced elements that convert); a rule whose
	/// every action drops is reported under <c>droppedRules</c> instead. Null when no environment probe ran.
	/// </summary>
	[JsonPropertyName("pageBusinessRules")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public PageBusinessRuleConversionInfo PageBusinessRules { get; init; }

	/// <summary>
	/// Requests (actions) referenced by the source page's component event bindings (a button's
	/// <c>clicked</c>, a field's <c>valueChange</c>/<c>updated</c>), deterministically converted for
	/// mobile. Supported requests are remapped in-place inside the affected element's
	/// <c>viewConfigDiff[].values</c>. An unsupported or unknown/custom request is handled by component
	/// type: on a <c>crt.Button</c> the whole element is DROPPED — a dead button, read in
	/// <see cref="DroppedElement"/> under <see cref="ReasonCodes.DropUnsupportedRequest"/>, which is the
	/// only place a plain leaf button's loss is reported; on any other component type the binding is kept
	/// verbatim and flagged for manual review (the component stays). <c>droppedRequests</c> reports a
	/// BINDING: one lost while its element survived, and one lost on the paths that place or remove the
	/// element itself (retarget onto a native, missing retarget target, non-converting scope, empty
	/// container, exclusion). A missing action target (<see cref="UnresolvedTargetRequest"/>) still leaves
	/// the converted binding in <see cref="ViewConfigDiff"/>: a definitional absence blanks only the target
	/// param (see <see cref="UnresolvedTargetRequest.BindingRemoved"/>) rather than removing the binding, so
	/// the caller PATCHES that param in place once the target resolves rather than re-adding anything. What
	/// is NOT applied is the TELLING: every
	/// <see cref="RequestConversionInfo.UnresolvedTargetRequests"/> entry is a diagnosis the caller must
	/// report itself.
	/// Null when the source page references no requests AND no action target needed reporting.
	/// (Page <c>handlers</c> are web-only and never transferred.)
	/// </summary>
	[JsonPropertyName("requestConversions")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public RequestConversionInfo RequestConversions { get; init; }

	// ── Adaptive (per-breakpoint) layout proposal ─────────────────────
	/// <summary>
	/// The responsive layout applied to each MULTI-column mobile grid container: how many grid columns per
	/// breakpoint (<c>small</c> phone = 1, <c>medium</c>/<c>large</c> tablet = the web columns) and which
	/// cell each child occupies. Both sides are ALREADY baked into the operations' values — the container's
	/// <c>adaptive</c> columns into its own values and each child's placement into
	/// <c>viewConfigDiff[].values.layoutConfig.adaptive</c> — so there is nothing separate to apply. Present
	/// it at the conversion gate as what the conversion DID, and if the user wants it different, the change
	/// is an edit to those <c>values</c> before pasting: this section is a readable index of them, not a
	/// switch, and there is no mechanism here to decline it.
	/// Null when no multi-column grid container is present (a single-column grid gets no adaptive).
	/// </summary>
	[JsonPropertyName("adaptiveLayout")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<AdaptiveLayoutGroup> AdaptiveLayout { get; init; }

	// ── Tab body / Area layers synthesized inside a converted tab ──────
	/// <summary>
	/// The containers the converter SYNTHESIZES inside every tab it creates: the designer's
	/// tab-body grid and the Area card inside it that receives the tab's content. Already baked into
	/// <see cref="ViewConfigDiff"/> as ordinary
	/// <c>insert</c> entries placed right after the tab's own entry — there is nothing separate to apply.
	/// This is an informational summary of a MANDATORY structure, NOT a proposal: report it at the
	/// conversion gate as fact, never offer to skip or replace it. Null when the page has no
	/// converter-created tab with content (a tab the mobile template provides is a merge twin and is never
	/// touched; an empty tab gets no layers at all).
	/// </summary>
	[JsonPropertyName("tabAreaLayers")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<TabAreaLayerGroup> TabAreaLayers { get; init; }

	// ── Every property normalization the conversion rules declare ──────
	/// <summary>
	/// One section per normalization standard the CONVERSION RULES declare, keyed by the section name the
	/// standard's target component TYPE maps to (e.g. <c>"spacing"</c>, <c>"metricStyle"</c>). The set of
	/// keys is open: the rules file is resolved at runtime, so a standard added there appears here without
	/// a binary change, and a type this build has never seen gets its own section under its own name rather
	/// than being folded into another standard's.
	/// <para>
	/// Each section lists the elements normalized (with the dotted paths actually written) and anything the
	/// stamp had to skip; both are already applied in <see cref="ViewConfigDiff"/>, so nothing here is
	/// separate to apply. Read the section rather than assuming a fixed set of properties. The rule that
	/// the web page's own values for those properties are discarded rather than translated is a standing
	/// one and lives in the guidance article, not in this section. Null when nothing was normalized at all.
	/// </para>
	/// </summary>
	[JsonPropertyName("normalizations")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, NormalizationInfo> Normalizations { get; init; }

	/// <summary>
	/// Every localized string the converted body references, keyed by resource name and resolved to its
	/// en-US text (e.g. <c>{ "EmailsSentNewMetric_title": "Emails sent" }</c>). The converted <c>values</c>
	/// carry the <c>#ResourceString(key)#</c> tokens verbatim (top-level captions AND nested ones like
	/// <c>config.title</c>); register this whole map on the mobile page via <c>update-page resources</c> so
	/// every token resolves. Null when the page references no resolvable localized strings.
	/// </summary>
	[JsonPropertyName("resourceStrings")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyDictionary<string, string> ResourceStrings { get; init; }

	/// <summary>
	/// The guidance article that owns the conversion flow and every standing mobile rule. This response
	/// carries NO advisory prose of its own: a finding gets a structured field, a rule gets a validator or
	/// the article. Adding a prose array here is a regression (ENG-95827).
	/// </summary>
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

	/// <summary>The component-registry / rules version the guide was built against (a concrete version or "latest").</summary>
	[JsonPropertyName("resolvedTargetVersion")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ResolvedTargetVersion { get; init; }

	/// <summary>How the version was resolved: "environment", "environment-superset", or "latest-fallback".</summary>
	[JsonPropertyName("resolvedFrom")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ResolvedFrom { get; init; }

	/// <summary>Caveat when the catalog is approximate or the target version is unknown; null when the version is exact.</summary>
	[JsonPropertyName("versionWarning")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string VersionWarning { get; init; }

	/// <summary>True only on "latest-fallback": the target version is unknown, so the caller must confirm with the user before acting on the guide.</summary>
	[JsonPropertyName("requiresVersionConfirmation")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
	public bool RequiresVersionConfirmation { get; init; }

	/// <summary>Stable kebab-case reason on the "latest-fallback" tier (e.g. "no-active-environment", "probe-error"); null otherwise.</summary>
	[JsonPropertyName("resolvedFromReason")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ResolvedFromReason { get; init; }

	[JsonPropertyName("error")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Error { get; init; }
}

/// <summary>
/// Result of converting the source page's PAGE-level business rules for the mobile page.
/// Advisory only: the model recreates the supported rules on the mobile page schema with
/// <c>create-page-business-rule</c> after approval; the tool writes nothing.
/// </summary>
public sealed class PageBusinessRuleConversionInfo {
	/// <summary>Whether the source page's business-rule add-on metadata could be read from the environment.</summary>
	[JsonPropertyName("probeOk")]
	public bool ProbeOk { get; init; }

	/// <summary>Human-readable status (e.g. "no page-level business rules found", or why the probe failed).</summary>
	[JsonPropertyName("note")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Note { get; init; }

	/// <summary>Rules where at least one action converts. Recreate on the mobile page (see each entry).</summary>
	[JsonPropertyName("convertedRules")]
	public IReadOnlyList<ConvertedPageBusinessRule> ConvertedRules { get; init; } = [];

	/// <summary>Rules dropped because no action converts (every referenced element drops, no data action).</summary>
	[JsonPropertyName("droppedRules")]
	public IReadOnlyList<DroppedPageBusinessRule> DroppedRules { get; init; } = [];
}

/// <summary>
/// A source page-level rule whose condition and surviving actions were carried to the mobile page.
/// </summary>
public sealed class ConvertedPageBusinessRule {
	[JsonPropertyName("caption")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Caption { get; init; }

	/// <summary>
	/// Ready-to-paste <c>rule</c> argument for <c>create-page-business-rule</c> on the mobile page —
	/// the condition verbatim plus the actions that survive (element names remapped web→mobile). Pass
	/// it to <c>create-page-business-rule</c> verbatim.
	/// </summary>
	[JsonPropertyName("rule")]
	public JsonNode Rule { get; init; }
}

/// <summary>A source page-level rule that does not convert (no surviving action).</summary>
public sealed class DroppedPageBusinessRule {
	[JsonPropertyName("caption")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string Caption { get; init; }

	/// <summary>Coded cause. See <c>ReasonCodes.DropRule*</c>; decoded by the guidance article.</summary>
	[JsonPropertyName("reason")]
	public IReadOnlyList<ReasonCode> Reason { get; init; } = [];
}

/// <summary>
/// Advisory summary of how the source page's component event-binding requests (actions) were converted
/// for mobile. The actionable result is already applied to each affected element's
/// <c>viewConfigDiff[].values</c>; this section explains what happened so the user can review.
/// </summary>
public sealed class RequestConversionInfo {
	/// <summary>Requests carried to mobile (kept in the binding; remapped when the mobile name differs).</summary>
	[JsonPropertyName("convertedRequests")]
	public IReadOnlyList<ConvertedRequest> ConvertedRequests { get; init; } = [];

	/// <summary>Requests with no mobile equivalent: the binding was stripped (the component still renders).</summary>
	[JsonPropertyName("droppedRequests")]
	public IReadOnlyList<DroppedRequest> DroppedRequests { get; init; } = [];

	/// <summary>Unknown/custom requests kept verbatim but flagged: verify they exist on mobile.</summary>
	[JsonPropertyName("flaggedRequests")]
	public IReadOnlyList<FlaggedRequest> FlaggedRequests { get; init; } = [];

	/// <summary>
	/// Actions whose REQUEST converts but whose NAVIGATION TARGET could not be confirmed to exist on
	/// mobile: a <c>crt.OpenPageRequest</c> naming a web page, or a create/update-record request naming an
	/// object with no default mobile edit page. The CONTROL always survives — every entry here
	/// names an element the converted page still carries. Whether the target param is also blanked depends
	/// on how confidently the absence is judged (see <see cref="UnresolvedTargetRequest.BindingRemoved"/>):
	/// a definitional absence blanks the target param on the binding that is ALREADY sitting on the element,
	/// so once a target that converts later in the same session (the missing-target-page queue,
	/// <see cref="MissingTargetPages"/>) makes the action work again, the caller PATCHES that one param
	/// in place instead of the action silently vanishing for good or shipping broken.
	/// <para>
	/// Branch on <see cref="UnresolvedTargetRequest.State"/> for HOW CONFIDENTLY the absence is reported, and
	/// on <see cref="UnresolvedTargetRequest.BindingRemoved"/> for WHAT WAS ACTUALLY DONE about it — the two
	/// are independent:
	/// </para>
	/// <list type="bullet">
	/// <item><c>state: missing</c>, <c>bindingRemoved: true</c> — a DEFINITIONAL absence (a web page cannot
	/// open on mobile at all). The binding stays in <c>viewConfigDiff[].values</c> with its target param
	/// blanked to <c>""</c>, and also appears in <see cref="DroppedRequests"/> under
	/// <see cref="ReasonCodes.DropRequestTargetMissing"/>. Do not treat it as usable as-is — it opens nothing
	/// until the target resolves; at that point patch
	/// <c>values[elementName][binding].params[targetParam]</c> to the resolved mobile schema name.</item>
	/// <item><c>state: missing</c>, <c>bindingRemoved: false</c> — an environment READ reported the target
	/// absent (an object with no default mobile page). A read cannot PROVE absence, so nothing was blanked:
	/// build the element exactly as the element map says, and tell the user which action needs a working
	/// target.</item>
	/// <item><c>state: unknown</c> — the environment could not answer; the action still works if the target
	/// is really there, so <c>bindingRemoved</c> is always <see langword="false"/> here too. Ask the user to
	/// confirm rather than reporting it broken.</item>
	/// </list>
	/// <para>
	/// Empty when every target resolved, and empty when nothing could be settled at all — a degraded probe
	/// still reports what it settled offline, so an empty list is NOT by itself "all clear": read
	/// <see cref="TargetsProbed"/> to find out whether the object targets were verified.
	/// </para>
	/// </summary>
	[JsonPropertyName("unresolvedTargetRequests")]
	public IReadOnlyList<UnresolvedTargetRequest> UnresolvedTargetRequests { get; init; } = [];

	/// <summary>
	/// Whether the environment was actually queried for action-target existence. <c>false</c> means every
	/// target that NEEDS a read (an object's default mobile page) is treated as unverified and is NOT reported
	/// individually — do not look for <c>state: unknown</c> entries for them, there are none — so an empty
	/// <see cref="UnresolvedTargetRequests"/> is "not checked" rather than "all clear". The causes are: no
	/// environment was supplied, the reads failed, the page inputs were missing, the page body could not be
	/// walked, or the conversion rules declare no navigation targets at all.
	/// <para>
	/// It does NOT mean the list is empty: a <c>web-page</c> target is settled without any read, so those
	/// findings are reported even here (and their bindings removed — see
	/// <see cref="UnresolvedTargetRequest.BindingRemoved"/> — since that verdict needs no read either).
	/// Treat this as "were the object targets verified", and <see cref="TargetsNote"/> as why not.
	/// </para>
	/// </summary>
	[JsonPropertyName("targetsProbed")]
	public bool TargetsProbed { get; init; }

	/// <summary>
	/// What limited the check, when anything did: no environment, an unreadable response, or rules that
	/// declare no navigation targets. Null when the check ran in full — so it can be present even with
	/// <see cref="TargetsProbed"/> true, and reporting it is what lets the user tell "not asked" from "asked,
	/// and the answer was no".
	/// </summary>
	[JsonPropertyName("targetsNote")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string TargetsNote { get; init; }

	/// <summary>
	/// Deduplicated queue of missing mobile pages the caller can offer to convert next, built from
	/// <see cref="UnresolvedTargetRequests"/>. Covers BOTH kinds: every <c>web-page</c> target
	/// (<c>MobileActionTargetProbe.KindWebPage</c>) — settled offline and final by construction, so queued
	/// unconditionally — and every <c>entity-default-mobile-page</c> target verified <c>missing</c> (a
	/// <c>state: unknown</c> entity target is reported only, via <see cref="UnresolvedTargetRequests"/>, and
	/// never queued here). An entity target is keyed by its
	/// <see cref="UnresolvedTargetRequest.ResolvedCandidateSchemaName"/> when the environment resolved one,
	/// else by its raw object <c>target</c> name. When a <c>web-page</c> target's schema name coincides with
	/// an entity target's resolved candidate (a direct <c>crt.OpenPageRequest</c> on a page that also happens
	/// to be some object's default mobile edit page), they collapse into ONE entry carrying every reference
	/// from both sources — the caller no longer needs to cross-reference the two sources itself. One entry
	/// per distinct key (case-insensitive), carrying every element/binding pair that references it. Locate
	/// each one's already-converted binding on the element by <c>elementName</c>/<c>binding</c> and patch its
	/// target param once the target resolves. Empty when no target was found missing.
	/// </summary>
	[JsonPropertyName("missingTargetPages")]
	public IReadOnlyList<MissingTargetPage> MissingTargetPages { get; init; } = [];
}

/// <summary>
/// One place a <see cref="MissingTargetPage"/> is referenced from the source page: the component and the
/// event binding that names it.
/// </summary>
public sealed class MissingTargetPageReference {
	[JsonPropertyName("elementName")]
	public string ElementName { get; init; }

	/// <summary>
	/// The event binding this reference names (e.g. <c>clicked</c>). The converted binding is never removed
	/// anymore — the mobile-shaped config (correct request name, <c>paramMap</c> already applied, every other
	/// param intact) is already sitting on the element, only its target param is blanked — so repointing once
	/// the target resolves means locating the existing binding by <see cref="ElementName"/>/<see cref="Binding"/>
	/// and patching just the target param (<c>values[elementName][binding].params[targetParam]</c>) to the
	/// resolved mobile schema name; the param name comes from the conversion rule's <c>targetParam</c>.
	/// </summary>
	[JsonPropertyName("binding")]
	public string Binding { get; init; }
}

/// <summary>
/// One distinct missing mobile page target, deduplicated across every element that references it.
/// See <see cref="RequestConversionInfo.MissingTargetPages"/> for scope and construction.
/// </summary>
public sealed class MissingTargetPage {
	/// <summary>
	/// The target value: a page schema name for a <c>web-page</c> row, or for an entity row
	/// <see cref="UnresolvedTargetRequest.ResolvedCandidateSchemaName"/> when resolved, else the raw object
	/// name.
	/// </summary>
	[JsonPropertyName("target")]
	public string Target { get; init; }

	/// <summary>
	/// <c>web-page</c> or <c>entity-default-mobile-page</c>. When references from BOTH kinds collapsed into
	/// this one row (they resolved to the same schema name), this is <c>web-page</c> — that is the kind the
	/// step-8a repoint sub-step keys on.
	/// </summary>
	[JsonPropertyName("targetKind")]
	public string TargetKind { get; init; }

	/// <summary>Every element/binding pair on the source page that references <see cref="Target"/>.</summary>
	[JsonPropertyName("references")]
	public IReadOnlyList<MissingTargetPageReference> References { get; init; } = [];
}

/// <summary>A request carried to mobile from a component's event binding.</summary>
public sealed class ConvertedRequest {
	/// <summary>Name of the component that carries the binding (e.g. "SaveButton").</summary>
	[JsonPropertyName("elementName")]
	public string ElementName { get; init; }

	/// <summary>Event binding the request is wired to (e.g. "clicked", "valueChange").</summary>
	[JsonPropertyName("binding")]
	public string Binding { get; init; }

	[JsonPropertyName("webRequest")]
	public string WebRequest { get; init; }

	[JsonPropertyName("mobileRequest")]
	public string MobileRequest { get; init; }
}

/// <summary>A request stripped from a component's event binding (no mobile equivalent).</summary>
public sealed class DroppedRequest {
	[JsonPropertyName("elementName")]
	public string ElementName { get; init; }

	[JsonPropertyName("binding")]
	public string Binding { get; init; }

	[JsonPropertyName("webRequest")]
	public string WebRequest { get; init; }

	/// <summary>
	/// Coded cause, from the one vocabulary — never the element's <c>webType</c> or a sentence. Three
	/// shapes, and which one you get says what happened:
	/// <list type="bullet">
	/// <item><description>The element SURVIVED and only its binding went:
	/// <see cref="ReasonCodes.DropRequestUnsupported"/>.</description></item>
	/// <item><description>The element went and the binding's own fate has a name of its own —
	/// <see cref="ReasonCodes.DropRequestChromeNative"/> (the mobile native it was retargeted onto carries
	/// its own action), <see cref="ReasonCodes.DropRequestElementEmptyContainer"/>,
	/// <see cref="ReasonCodes.DropRequestElementExcluded"/>.</description></item>
	/// <item><description>The element went for a reason that already explains the binding — a
	/// non-converting scope, a missing retarget target — and the binding then carries the SAME code object
	/// the element's <c>droppedElements</c> entry reports, because restating it in a second vocabulary
	/// could only drift from the first.</description></item>
	/// </list>
	/// So join to <c>droppedElements</c> by <c>elementName</c>, never by code.
	/// </summary>
	[JsonPropertyName("reason")]
	public IReadOnlyList<ReasonCode> Reason { get; init; } = [];
}

/// <summary>An unknown/custom request kept in the binding but flagged for manual verification.</summary>
public sealed class FlaggedRequest {
	[JsonPropertyName("elementName")]
	public string ElementName { get; init; }

	[JsonPropertyName("binding")]
	public string Binding { get; init; }

	[JsonPropertyName("request")]
	public string Request { get; init; }

	/// <summary>Coded cause — <see cref="ReasonCodes.FlagRequestUnmapped"/>.</summary>
	[JsonPropertyName("reason")]
	public IReadOnlyList<ReasonCode> Reason { get; init; } = [];
}

/// <summary>
/// One action whose navigation target was not confirmed to exist on mobile. The control it names ALWAYS
/// stays on the converted page, and so does its converted binding — UNLESS the absence is definitional —
/// see <see cref="BindingRemoved"/> — in which case only the target param is blanked to <c>""</c> rather
/// than shipping a binding that fails every time it fires. Because the binding is never removed, repointing
/// it once its target converts later in the same session (<c>requestConversions.missingTargetPages</c>) is
/// a point patch of that one param, not a reconstruction. Fully typed: what to do about each outcome arrives
/// as a guide <c>constraint</c> composed from these findings, not as prose carried on this record.
/// </summary>
public sealed class UnresolvedTargetRequest {
	/// <summary>
	/// The component that carries the binding, named as the CONVERTED element is — the same name
	/// <see cref="ConvertedRequest.ElementName"/> and <see cref="DroppedRequest.ElementName"/> use for the
	/// same binding, so the collections line up on one key.
	/// </summary>
	[JsonPropertyName("elementName")]
	public string ElementName { get; init; }

	/// <summary>Event binding the request is wired to (e.g. "clicked", "valueChange").</summary>
	[JsonPropertyName("binding")]
	public string Binding { get; init; }

	/// <summary>The web request type whose target this is, e.g. "crt.OpenPageRequest".</summary>
	[JsonPropertyName("webRequest")]
	public string WebRequest { get; init; }

	/// <summary>
	/// What <see cref="Target"/> names — <c>web-page</c> (a WEB page schema, which the Creatio Mobile app
	/// cannot open at all) or <c>entity-default-mobile-page</c> (an object that must have a default mobile
	/// edit page). The SET is open by design — the conversion rules declare which requests carry a target —
	/// but the value here is the canonical constant clio recognized the rules' <c>targetKind</c> as, not the
	/// rules file's own spelling. Compare it case-insensitively all the same; a kind clio does not recognize
	/// produces no finding at all rather than an echoed literal.
	/// </summary>
	[JsonPropertyName("targetKind")]
	public string TargetKind { get; init; }

	/// <summary>The target value read from the binding's params: a page schema name, or an object name.</summary>
	[JsonPropertyName("target")]
	public string Target { get; init; }

	/// <summary>
	/// How confidently the target is reported: <see cref="StateMissing"/> — the target was established absent;
	/// <see cref="StateUnknown"/> — nothing could be established, so it may well be fine. This says nothing
	/// about what happened to the action — read <see cref="BindingRemoved"/> for that.
	/// </summary>
	[JsonPropertyName("state")]
	public string State { get; init; }

	/// <summary>
	/// Whether the converter BLANKED this action's target param on the element's <c>mobileValues</c> binding
	/// (the control, and the binding itself, always stay — only the target param's value is cleared to
	/// <c>""</c>). True only for a DEFINITIONAL absence — <see cref="TargetKind"/> <c>web-page</c> with
	/// <see cref="State"/> <see cref="StateMissing"/>, whose verdict needs no environment read and cannot be
	/// wrong for a reason outside this process. Always <see langword="false"/> for
	/// <c>entity-default-mobile-page</c> (an add-on declaring no default page is a fact about the add-on, not
	/// proof the action is dead) and for <see cref="StateUnknown"/> (nothing was established either way). When
	/// <see langword="true"/>, repoint by patching <c>values[elementName][binding].params[targetParam]</c>
	/// once the target resolves — the binding itself is never removed, so there is nothing to reconstruct.
	/// </summary>
	[JsonPropertyName("bindingRemoved")]
	public bool BindingRemoved { get; init; }

	/// <summary>
	/// The object's default WEB edit page, when one was found — set only for a
	/// <see cref="TargetKind"/> of <c>entity-default-mobile-page</c> whose <see cref="State"/> is
	/// <see cref="StateMissing"/>. Offer converting this page next; it becomes the object's default mobile
	/// edit page once the conversion registers it. Null when no candidate could be found (the object's web
	/// <c>RelatedPage</c> add-on declares none) — never a guessed name, so a null here means "the caller must
	/// find or ask for the source page", not "there is none".
	/// </summary>
	[JsonPropertyName("resolvedCandidateSchemaName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ResolvedCandidateSchemaName { get; init; }

	/// <summary>Wire value for a target established ABSENT on mobile.</summary>
	public const string StateMissing = "missing";

	/// <summary>Wire value for a target the environment could not answer for — the action stands.</summary>
	public const string StateUnknown = "unknown";
}

/// <summary>
/// The adaptive (per-breakpoint) layout applied to one multi-column mobile grid container. Both sides are
/// ALREADY in <see cref="MobilePageConversionGuide.ViewConfigDiff"/> — the container's <c>adaptive</c>
/// columns in its own <c>values</c>, and each child's placement in that child's
/// <c>values.layoutConfig.adaptive</c> — so there is nothing separate to apply (no duplicate merge). A
/// readable index of what the conversion did, not a switch.
/// </summary>
public sealed class AdaptiveLayoutGroup {
	/// <summary>The mobile container these fields are grouped into (e.g. "AreaProfileContainer").</summary>
	[JsonPropertyName("containerName")]
	public string ContainerName { get; init; }

	/// <summary>
	/// Advisory overview of the grid columns per breakpoint (already in the container's <c>values</c>):
	/// keys <c>small</c> / <c>medium</c> / <c>large</c>, each a list of CSS column sizes (e.g. ["1fr","1fr"]).
	/// </summary>
	[JsonPropertyName("columnsByBreakpoint")]
	public IReadOnlyDictionary<string, IReadOnlyList<string>> ColumnsByBreakpoint { get; init; }
		= new Dictionary<string, IReadOnlyList<string>>();

	/// <summary>The fields placed in this container and the per-breakpoint cell each occupies.</summary>
	[JsonPropertyName("items")]
	public IReadOnlyList<AdaptiveLayoutItem> Items { get; init; } = [];
}

/// <summary>
/// The tab-body / Area layers synthesized inside ONE converter-created tab. Mirrors what is
/// already baked into the element map: mobile design puts a tab's content inside a colored Area card that
/// sits in a tab-body grid, and a tab converted from web carries neither.
/// </summary>
public sealed class TabAreaLayerGroup {
	/// <summary>The converted tab the layers were synthesized into (its mobile name).</summary>
	[JsonPropertyName("tabName")]
	public string TabName { get; init; }

	/// <summary>Name of the synthesized tab-body grid (the tab's direct child).</summary>
	[JsonPropertyName("mainTabContainerName")]
	public string MainTabContainerName { get; init; }

	/// <summary>
	/// Name of the synthesized Area card (child of the tab-body grid). Null when the tab's top-level
	/// content is routing hints only — no Area is synthesized then, so an empty card never appears (AC#5).
	/// </summary>
	[JsonPropertyName("areaName")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string AreaName { get; init; }

	/// <summary>
	/// The components moved out of the tab and into the Area, in the order they are stacked there
	/// (the source page's own order — the first entry is row 1). Already reflected in each element's
	/// <c>parentName</c> and <c>layoutConfig</c>, so there is nothing to re-parent by hand.
	/// </summary>
	[JsonPropertyName("movedChildren")]
	public IReadOnlyList<string> MovedChildren { get; init; } = [];
}


/// <summary>
/// One normalization standard's report: the caller-facing wording carried by the conversion rule that
/// declared it, what was normalized, and what could not be. Shared by every standard — a new one is a
/// rules-file entry, not another pair of identical DTOs.
/// </summary>
public sealed class NormalizationInfo {
	/// <summary>One entry per element this standard normalized.</summary>
	[JsonPropertyName("normalized")]
	public IReadOnlyList<NormalizationEntry> Normalized { get; init; } = [];

	/// <summary>
	/// Elements the standard could NOT be applied to, with the branch it refused and why. Present only when
	/// something was skipped. Without it a silent no-op is indistinguishable from "nothing to normalize" —
	/// these elements keep the WEB values and may need a manual pass in the designer.
	/// </summary>
	[JsonPropertyName("skipped")]
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public IReadOnlyList<NormalizationSkip> Skipped { get; init; }
}

/// <summary>One element normalized to a standard, and the properties actually written on it.</summary>
public sealed class NormalizationEntry {
	/// <summary>The element's mobile name.</summary>
	[JsonPropertyName("name")]
	public string Name { get; init; }

	/// <summary>The element's mobile component type.</summary>
	[JsonPropertyName("type")]
	public string Type { get; init; }

	/// <summary>
	/// The properties stamped onto this element's <c>values</c>. A replacing rule reports the top-level key
	/// it replaced (e.g. <c>["gap"]</c>); a merging rule reports the dotted paths of the leaves it actually
	/// changed (e.g. <c>["config.layout.border.hidden"]</c>) — never the merged root, which would
	/// under-report, and never a leaf that already held the target value.
	/// </summary>
	[JsonPropertyName("properties")]
	public IReadOnlyList<string> Properties { get; init; } = [];
}

/// <summary>One element a standard could not be stamped onto, and why.</summary>
public sealed class NormalizationSkip {
	/// <summary>The element's mobile name.</summary>
	[JsonPropertyName("name")]
	public string Name { get; init; }

	/// <summary>The element's mobile component type.</summary>
	[JsonPropertyName("type")]
	public string Type { get; init; }

	/// <summary>
	/// The dotted paths the stamp refused to enter (e.g. <c>["config.text"]</c> when the element binds its
	/// whole text config). Other paths of the same rule may still have been stamped.
	/// </summary>
	[JsonPropertyName("properties")]
	public IReadOnlyList<string> Properties { get; init; } = [];

	/// <summary>Coded cause — <see cref="ReasonCodes.SkipNormalizationPathBlocked"/>.</summary>
	[JsonPropertyName("reason")]
	public IReadOnlyList<ReasonCode> Reason { get; init; } = [];
}

/// <summary>One field's per-breakpoint cell placement, mirroring the one already in its <c>values.layoutConfig.adaptive</c>.</summary>
public sealed class AdaptiveLayoutItem {
	[JsonPropertyName("name")]
	public string Name { get; init; }

	/// <summary>
	/// The <c>layoutConfig.adaptive</c> object: keys <c>small</c> / <c>medium</c> / <c>large</c>, each
	/// <c>{ row, column, colSpan, rowSpan }</c> (1-based). Identical to what is already written into the
	/// field's <c>viewConfigDiff[].values.layoutConfig.adaptive</c>.
	/// </summary>
	[JsonPropertyName("layoutConfigAdaptive")]
	public JsonNode LayoutConfigAdaptive { get; init; }
}

// ── Intermediate read-model (not serialized) ──────────────────────────
// Produced by PageBusinessRuleProbe from persisted add-on metadata, consumed by
// WebToMobileAnalysisService.ConvertPageBusinessRules. Conditions/expressions are already
// reverse-mapped into the create-page-business-rule INPUT contract shape so conversion stays pure.

/// <summary>One source page-level business rule (single case) parsed from add-on metadata.</summary>
internal sealed class SourcePageBusinessRule {
	public string Caption { get; init; }

	/// <summary>Condition group in create-page-business-rule input shape ({logicalOperation, conditions}); may be null.</summary>
	public JsonNode Condition { get; init; }

	/// <summary>
	/// Why the source condition cannot be faithfully represented in the create-page-business-rule input (or
	/// <see cref="PageRuleConditionIssue.None"/> when it can). Such a rule is dropped for manual recreation
	/// rather than emitted with fabricated semantics. <see cref="PageRuleConditionIssue"/> for the cases.
	/// </summary>
	public PageRuleConditionIssue ConditionIssue { get; init; }

	public List<SourcePageRuleAction> Actions { get; init; } = [];
}

/// <summary>
/// Why a source page-rule condition cannot be converted losslessly into the flat, single-operator
/// create-page-business-rule condition input. A non-<see cref="None"/> value drops the rule for manual recreation.
/// </summary>
internal enum PageRuleConditionIssue {
	/// <summary>The condition converts faithfully (or there is no condition).</summary>
	None = 0,

	/// <summary>
	/// The condition mixes AND and OR across nested groups (e.g. <c>A AND (B OR C)</c>); the flat single-operator
	/// input cannot represent it without changing when the rule fires.
	/// </summary>
	MixedAndOr,

	/// <summary>
	/// A condition uses a present comparison operator that maps to no supported comparison (e.g. "begins with").
	/// Emitting it would silently change the comparison, so the rule is dropped instead.
	/// </summary>
	UnrecognizedComparison
}

/// <summary>One action of a source page-level business rule. Page rules support only element actions.</summary>
internal sealed class SourcePageRuleAction {
	/// <summary>Short action type: hide-element / show-element / make-editable / make-read-only / make-required / make-optional.</summary>
	public string ActionType { get; init; }

	/// <summary>Referenced page element names.</summary>
	public List<string> ElementItems { get; init; } = [];
}

/// <summary>Outcome of reading a source page's page-level business rules.</summary>
public sealed class PageBusinessRuleProbeResult {
	public bool ProbeOk { get; init; }
	public string Note { get; init; }
	internal IReadOnlyList<SourcePageBusinessRule> Rules { get; init; } = [];
}

/// <summary>
/// Whether an action's navigation target exists on mobile. <see cref="Unknown"/> is the DEFAULT (value 0)
/// on purpose: an absent dictionary entry — the shape an unreachable environment produces — must read as
/// "not answered", never as "absent", so the feature fails open structurally rather than by a flag check.
/// </summary>
public enum ActionTargetState {
	/// <summary>
	/// Not answered: not probed, not reachable, or ambiguous.
	/// </summary>
	Unknown = 0,

	/// <summary>
	/// Verified present on mobile: an object with a default mobile edit page. Nothing produces this for a page
	/// kind — a <c>web-page</c> target is settled <see cref="Missing"/> by construction.
	/// </summary>
	Resolved,

	/// <summary>
	/// Established absent on mobile. Always reported; never removes the control or the binding. Whether it
	/// blanks the action's TARGET PARAM depends on the target kind — see
	/// <c>MobileActionTargetProbe.StripsBindingOnMissing</c>.
	/// </summary>
	Missing
}

/// <summary>One distinct action target and what the environment said about it.</summary>
public sealed class ActionTargetResolution {
	/// <summary>The rules-declared kind, e.g. <c>web-page</c> or <c>entity-default-mobile-page</c>.</summary>
	public string Kind { get; init; }

	/// <summary>The target value as it appears in the binding's params (a page or object name).</summary>
	public string Target { get; init; }

	/// <summary>What the environment said. See <see cref="ActionTargetState"/>.</summary>
	public ActionTargetState State { get; init; }

	/// <summary>
	/// The object's default WEB edit page, resolved ONLY for a <see cref="Kind"/> of
	/// <c>entity-default-mobile-page</c> whose <see cref="State"/> is <see cref="ActionTargetState.Missing"/>
	/// — the page the caller can offer to convert next so the object gets a mobile default.
	/// Null on every other kind/state combination and when the object's web <c>RelatedPage</c> add-on declares
	/// no untyped default either (fail-open, never a guessed name).
	/// </summary>
	public string ResolvedCandidateSchemaName { get; init; }
}

/// <summary>
/// One place a target-carrying request appears on the source page. Collected from the page body alone
/// (no environment), so the collector is unit-testable offline and the resolution stays deduplicated:
/// many occurrences can share one <see cref="ActionTargetResolution"/>.
/// </summary>
public sealed class ActionTargetOccurrence {
	/// <summary>Component that carries the binding.</summary>
	public string ElementName { get; init; }

	/// <summary>Event binding the request is wired to (e.g. <c>clicked</c>).</summary>
	public string Binding { get; init; }

	/// <summary>The web request type.</summary>
	public string WebRequest { get; init; }

	/// <summary>The rules-declared target kind.</summary>
	public string Kind { get; init; }

	/// <summary>The literal target value read from the params.</summary>
	public string Target { get; init; }
}

/// <summary>
/// The page-side inputs one action-target probe reads. Grouped into a record rather than passed as loose
/// parameters: they all describe ONE source page, and they travel together to every future caller.
/// </summary>
/// <param name="ViewConfig">The merged <c>viewConfig</c> — the only place action bindings are read from.</param>
/// <param name="Rules">Resolved conversion rules; their <c>requests</c> section declares which targets to check.</param>
/// <param name="ModelConfig">
/// The merged <c>modelConfig</c>. Only its data-source entity names are read, to leave out the objects the
/// conversion is itself about.
/// </param>
/// <param name="PagePackageUId">The source page's package UId, used to address an object's add-on.</param>
public sealed record MobileActionTargetProbeRequest(
	JsonArray ViewConfig,
	WebToMobilePageConversionRules Rules,
	JsonObject ModelConfig,
	string PagePackageUId);

/// <summary>
/// Outcome of probing whether the source page's action targets exist on mobile. Best-effort and PER TIER:
/// on an object-tier failure <see cref="ProbeOk"/> is false and <see cref="TargetsByKey"/> keeps only the
/// entries settled WITHOUT a read (a <c>web-page</c> verdict is definitional), so every target the tier could
/// not answer for is absent from the map and therefore resolves to
/// <see cref="ActionTargetState.Unknown"/>. It is empty only when nothing could be settled at all.
/// </summary>
public sealed class MobileActionTargetProbeResult {
	/// <summary>
	/// Whether the tier that NEEDS the environment answered. False leaves object targets absent from
	/// <see cref="TargetsByKey"/> (and so unknown) but does NOT invalidate the entries settled without any
	/// read — a <c>web-page</c> verdict is definitional and survives an unreachable environment.
	/// </summary>
	public bool ProbeOk { get; init; }

	/// <summary>
	/// Human-readable note about the COMPLETENESS of the check; null when it ran in full. Set when nothing
	/// could be verified, so it is meaningful whatever <see cref="ProbeOk"/> says. Already redacted of
	/// environment detail at the point it is built.
	/// </summary>
	public string Note { get; init; }

	/// <summary>Every place a target-carrying request appears on the page, in document order.</summary>
	public IReadOnlyList<ActionTargetOccurrence> Occurrences { get; init; } = [];

	/// <summary>
	/// Resolution per DISTINCT target, keyed by <c>MobileActionTargetProbe.TargetKey</c>. An absent key
	/// means <see cref="ActionTargetState.Unknown"/> — callers must never treat absence as absence-on-mobile.
	/// </summary>
	public IReadOnlyDictionary<string, ActionTargetResolution> TargetsByKey { get; init; }
		= new Dictionary<string, ActionTargetResolution>(StringComparer.OrdinalIgnoreCase);
}
