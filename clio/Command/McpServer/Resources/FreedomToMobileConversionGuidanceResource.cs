using System.Diagnostics.CodeAnalysis;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Canonical AI-facing guidance for converting a Freedom UI WEB page into a Freedom UI MOBILE
/// page through clio MCP (ENG-89620). Freedom-UI-only: Classic UI pages must be converted to a
/// Freedom UI web page first (separate converter). Surfaced by name through <c>get-guidance</c>
/// as <c>freedom-page-web-to-mobile-conversion</c>.
/// </summary>
[McpServerResourceType]
[FeatureToggle("mobile-page-converter")]
[SuppressMessage("Major Code Smell", "S1118:Utility classes should not have public constructors", Justification = "MCP discovers [McpServerResourceType] classes by type; mirrors the other guidance-resource classes, which are non-static for the same reason.")]
public sealed class FreedomToMobileConversionGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/freedom-page-web-to-mobile-conversion";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
			clio MCP — Freedom UI WEB → Freedom UI MOBILE page conversion guide

			PURPOSE
			Convert an existing Freedom UI WEB page into a Freedom UI MOBILE page for the Creatio
			Mobile app. The conversion is MODEL-DRIVEN: a tool gives you a deterministic advisory
			guide, and YOU build the mobile page body and persist it with the standard page tools.
			The tool decides nothing about the body — you do.

			SCOPE: Freedom UI ONLY. This does NOT handle Classic UI pages. A Classic UI page must
			first be converted to a Freedom UI WEB page (with the dedicated classic-web ->
			freedom-web converter), and only then converted to mobile.

			TOOL: get-mobile-page-conversion-guide (ADVISORY-ONLY — builds nothing, writes nothing)
			It detects the source page type (today only Freedom UI web, sourceType "freedom-web", is
			supported) and returns a conversion GUIDE. It does NOT generate a body and does NOT save to
			Creatio or disk. The guide contains:
			  - recommendedMobileTemplate + templateNote — the mobile template to create the page from.
			  - containerMap — web→mobile container-name correspondence; use it to set each
			    component's parentName to the correct mobile container.
			  - sourceStructure — the full resolved component tree (incl. components inherited from the
			    base template), with name / type / parentName / isContainer.
			  - componentSuggestions — per source component TYPE: a category (directMapping /
			    withAdaptation / alternativeAvailable / unsupported / requiresManualDecision), the
			    suggested mobile type(s), and a primaryWebMerge note for many→one mappings.
			  - elementMap — per NAMED ELEMENT, the exact instance-level decision (operation =
			    merge / insert / drop / relocate-children). Iterate this to build the body; it already
			    encodes merge-vs-insert, the mobile parent, survivability and caption resources. Do NOT
			    re-derive placement from containerMap + componentSuggestions.
			  - mobileContracts — for each suggested mobile type: allowedProperties + example +
			    designerDefaults, so you can build the component's values inline.
			  - modelConfigDiff / viewModelConfigDiff — READY-TO-PASTE diffs. modelConfigDiff is a single
			    root merge of the full data-source config; viewModelConfigDiff is a set of FOCUSED targeted
			    merges (page-owned ["attributes"] merge + per-collection augments + per-array modelConfig
			    overrides unioned with the template's own natives), NOT a single root merge. Paste them
			    VERBATIM as the page's modelConfigDiff / viewModelConfigDiff (see DATA SECTIONS below). This
			    is the supported way to apply the data sections.
			  - modelConfig / viewModelConfig — the same configs in full-object form, for REFERENCE only.
			    viewModelConfig is already FILTERED (attributes used only by dropped components removed).
			  - adaptiveLayout — the responsive layout for each MULTI-column grid container (phone collapses to
			    1 column and stacks; tablet/desktop keep the web columns). BOTH sides are already baked into
			    mobileValues (the container's adaptive columns into its own values, each child's placement into
			    elementMap[].mobileValues.layoutConfig.adaptive) — nothing separate to apply. Present it at the
			    gate so the user can adjust or decline. Null when there is no multi-column grid container.
			  - resourceStrings — every localized string the converted body references (top-level captions AND
			    nested tokens like config.title / text.template), keyed by resource name and resolved to its
			    en-US text. Register this whole map via update-page `resources` so every #ResourceString token renders.
			  - constraints + nextSteps — the hard mobile rules and the ordered flow.

			─────────────────────────────────────────────────────────────
			GATES — MANDATORY HARD STOPS (analysis-first: nothing is written until the developer approves)
			─────────────────────────────────────────────────────────────
			This conversion is advisory-first. Running the guide and presenting the plan WRITE NOTHING.
			Persistence and section registration each require the developer's EXPLICIT approval, given as a
			separate response AFTER you show a plain-language plan:
			- Gate M (before ANY write): after running get-mobile-page-conversion-guide, present the
			  plain-language plan (what transfers / is adapted / is unsupported / needs a decision, plus the
			  section-registration intent) and STOP. Do NOT call create-page, update-page, validate-page, or
			  create-page-business-rule until the developer explicitly approves the plan.
			- Gate S (before ANY section/workplace registration): do NOT call odata-update / odata-create
			  (SysModule / SysModuleInWorkplace / SysWorkplace) or register-related-page until the developer
			  SEPARATELY approves the registration. Registering as a section is always the user's decision.
			- The user's initial request is NOT approval. "convert page X to mobile and register it as a
			  section" states the request, not approval of the plan. Present the plan, then wait for a
			  separate explicit go-ahead.
			- Headless / autonomous mode: never self-approve. Produce the plan, ask for confirmation, and END
			  THE TURN without writing or registering anything.
			These gate rules are authoritative on their own — the plan, the approval handshake, and the
			conversion report are all described in this guide; do not depend on any external document.

			─────────────────────────────────────────────────────────────
			FLOW
			─────────────────────────────────────────────────────────────
			1. Run get-mobile-page-conversion-guide with the source page schema-name.
			   - Check the returned sourceType. If it is not "freedom-web" (e.g. a Classic UI page) the
			     tool reports it as not yet supported: convert the page to a Freedom UI WEB page first
			     (classic-web -> freedom-web converter), then run this tool. Explain this to the user.
			2. Read the guide. Present its summary to the user: the recommended template, what maps
			   directly, what has a mobile alternative, what is UNSUPPORTED, and what REQUIRES A MANUAL
			   DECISION. Resolve the unsupported / requiresManualDecision items WITH THE USER.
			   — then STOP at Gate M (see GATES above): present the plain-language plan and do NOT proceed to
			   step 3 until the developer explicitly approves. The user's initial request is not approval.
			3. Create the target mobile page from recommendedMobileTemplate — ONLY after Gate M — (list-page-templates with
			   schema-type "mobile" to confirm; create-page). The template provides the Scaffold root —
			   do NOT add a second Scaffold.
			4. Build the mobile body (plain JSON: viewConfigDiff / viewModelConfigDiff / modelConfigDiff)
			   by iterating elementMap. For each entry act on its operation:
			   - merge — the element is provided by the mobile template (a "twin", e.g. Tabs→Tabs,
			     FeedTabContainer→FeedContainer). REUSE the existing mobileName; do NOT insert it. (Insert
			     vs merge is the #1 mistake — the template already contains these elements.) A merge entry MAY
			     also carry a prebuilt mobileValues (for component twins whose rule declares carryProperties,
			     e.g. FolderTree->FolderTreeActions carrying sourceSchemaName/rootSchemaName) — paste it onto
			     the merged element verbatim, deterministically, as part of this same step. This does NOT
			     require a separate confirmation beyond Gate M — it is a mechanical property fill-in, not a new
			     decision. If the mobile list template already provides the List / ListItem elements, configure
			     them by MERGE-BY-NAME (the row goes on the ListItem element: title + body) — do NOT insert a
			     second crt.List and do NOT put itemLayout inside a merge of the parent List (silent no-op;
			     ListItem is a separate named element).
			   - insert — add mobileType under parentName/propertyName (propertyName defaults to "items").
			     When elementMap[].index is present, add it to the insert op at that 0-based position (a
			     positional element mapped above/below an anchor, e.g. above the mobile Tabs); otherwise omit
			     index and append. On a tabbed record page EVERY web tab inserts as its OWN new mobile tab under
			     Tabs (no general-tab collapse); the web wrapper's non-tab content merges into the mobile general
			     tab's grid (e.g. CardContentWrapper→GeneralTabContainer). The mobile template's Feed and
			     Attachments tabs (FeedTab, AttachmentsTab) MUST stay last: insert each converted web tab BEFORE
			     them (index it after the general tab) so the order is general tab, converted web tabs, Feed,
			     Attachments.
			     START from elementMap[].mobileValues: paste it as the component's values VERBATIM. It already
			     carries the type and EVERY source property the mobile component supports — never drop any of
			     them. It also already carries the CONVERTED event-binding requests (a button's `clicked`, a
			     field's `valueChange`/`updated`): supported requests are kept (remapped when the mobile name
			     differs). A component whose request the mobile app does NOT support is not inserted at all — it
			     was already DROPPED (see the elementMap `drop` entry), so you never see it here. Do NOT re-add or
			     hand-edit these bindings — paste mobileValues as-is. Then add ONLY
			     what mobileValues deliberately leaves out:
			       • the value binding (control, or value for lookups) — type-specific, so it is not prebuilt;
			       • for a structural mapping (grid → crt.List + crt.ListItem), build the row: add a crt.ListItem
			         into the crt.List's itemLayout (title = first column, body = the rest); see the
			         componentSuggestions note and the mobileContracts example. If the template already provides
			         the List/ListItem, configure them by merge-by-name instead of inserting (see the merge branch).
			     The mobileValues carry every localized string verbatim as #ResourceString(key)# tokens — both a
			     top-level caption AND nested ones (e.g. config.title, text.template). Register them ALL: pass
			     guide.resourceStrings (a { key: en-US text } map covering the whole converted body) to update-page
			     `resources` in one call — do NOT register a #ResourceString(...)# token as the value, and do not
			     hand-pick individual keys. A token whose key is not registered renders blank. Consult
			     mobileContracts / get-component-info (schema-type "mobile") only
			     for those not-prebuilt parts. validate-page is the backstop — it
			     rejects an insert that drops a required property (e.g. a field caption, or a lookup-path
			     attribute's type) and update-page refuses to save.
			   - relocate-children — do NOT recreate this container; its children are placed in parentName
			     instead (each child has its own entry whose parentName already points there).
			   - drop — skip the element entirely (reason explains why: unsupported type or multi-data-source).
			     Tell the user what was dropped. (Empty containers are still inserted — the user can delete them.)
			   For many→one suggestions (primaryWebMerge set, e.g. crt.FolderTree + crt.FolderTreeActions
			   -> crt.FolderTreeActions), emit a SINGLE mobile component and merge in the secondary
			   component's properties; do not emit the secondary as a separate component.
			5. Apply the data sections — paste guide.modelConfigDiff and guide.viewModelConfigDiff VERBATIM as
			   the page's modelConfigDiff / viewModelConfigDiff (see DATA SECTIONS below). Do NOT rebuild them
			   by hand, and NEVER copy the data-source section from a pre-existing / reference body.
			5b. Adaptive layout (when guide.adaptiveLayout is present): for every MULTI-column crt.GridContainer the
			   guide has ALREADY baked both sides into mobileValues you pasted in step 4 — the container's per-breakpoint
			   columns (small = 1, medium/large = the web columns) and each child's layoutConfig.adaptive (phone stacks
			   in one column; tablet/desktop keep the web placement). A single-column grid gets no adaptive (the mobile
			   client renders the plain layout). Nothing extra to apply — do NOT emit a separate merge for the
			   container's adaptive (it is already inside the container's inserted mobileValues; a separate merge
			   would duplicate the operation). Just PRESENT it to the user in plain language ("fields in <container>
			   stack on the phone, keep <n> columns on a tablet — adjust?"); they may change it or decline.
			6. Validate the body with validate-page; resolve any findings (e.g. a binding whose attribute
			   is not declared) before treating the page as done.
			7. Persist with update-page. Recreate the page-level business rules: for each
			   guide.pageBusinessRules.convertedRules entry, pass its `rule` VERBATIM to
			   create-page-business-rule on the MOBILE page (after the user approves). Surface any
			   droppedRules to the user (they did not convert). Then tell the user to open the result in
			   Freedom UI Mobile Designer for final layout review.

			─────────────────────────────────────────────────────────────
			COMPONENT CLASSIFICATION (5 categories — in componentSuggestions.category)
			─────────────────────────────────────────────────────────────
			- directMapping          : same component type exists on mobile — carry it over as-is.
			- withAdaptation         : transferred, but layout/properties need adjusting.
			- alternativeAvailable   : maps to a different mobile type (e.g. crt.Checkbox → crt.Toggle).
			- unsupported            : NOT available on mobile; replace it or configure manually.
			- requiresManualDecision : unknown/custom or ambiguous UX; decide with the user.

			─────────────────────────────────────────────────────────────
			DATA SECTIONS — modelConfigDiff / viewModelConfigDiff (paste, don't rebuild)
			─────────────────────────────────────────────────────────────
			Both metadata sections have IDENTICAL structural support in the mobile runtime, and the guide
			already hands them to you as ready-to-paste diffs.

			HARD RULE — NEVER source data-source attributes (modelConfigDiff) from a pre-existing or reference
			mobile body. That is exactly how an attribute's "type" (e.g. ForwardReference on a related/lookup
			column) gets dropped, and the binding then resolves to nothing in Mobile Designer ("Item with the
			path … not found"). Always build modelConfigDiff from the guide. If a target page already exists,
			DISCARD its data-source section and rebuild it from guide.modelConfigDiff.

			- modelConfigDiff (guide.modelConfigDiff): paste it VERBATIM as the page's modelConfigDiff. It is a
			  single root merge that carries the full modelConfig (data sources + attributes) with every
			  attribute's "type" and "path" intact. Do not omit, rename, or reconstruct any fields. (Own columns
			  that are not declared in attributes resolve automatically; only related/lookup-path columns are
			  declared, and each MUST keep its "type".)
			- viewModelConfigDiff (guide.viewModelConfigDiff): paste it VERBATIM as the page's
			  viewModelConfigDiff. The guide ALREADY removed attributes referenced only by dropped/unsupported
			  components. Converters: reference only OOTB mobile converters; a definitive mobile converter list
			  is forthcoming — flag any custom converter for manual review.
			- guide.modelConfig / guide.viewModelConfig are the same data in full-object form, for reference.

			CHECKLIST before validate-page: confirm no insert dropped a property the mobile component supports
			(you pasted mobileValues verbatim). validate-page enforces the critical ones — a data-source
			attribute whose "path" contains a "." must keep its "type", and an inserted field must keep its
			caption ("label"); both are errors that block update-page.

			─────────────────────────────────────────────────────────────
			HARD MOBILE RULES (see also get-guidance `mobile-page-modification`)
			─────────────────────────────────────────────────────────────
			- Mobile body is plain JSON with only viewConfigDiff / viewModelConfigDiff / modelConfigDiff.
			- NO handlers, NO validators, NO custom converters in the mobile body.
			- viewConfigDiff INSERTS address the slot by parentName + propertyName ONLY — never use "path" in a
			  viewConfigDiff insert (e.g. NOT "path": ["tools"]; use "propertyName": "tools"). "path" is valid
			  only in viewModelConfigDiff / modelConfigDiff; a viewConfigDiff insert that uses "path" is silently
			  dropped by the differ.
			- LIST ROW (grid → crt.List + crt.ListItem): the row layout lives on a crt.ListItem placed in the
			  crt.List's itemLayout (title = first grid column, body = the rest). If the mobile list template
			  already provides the List/ListItem elements, configure them by MERGE-BY-NAME (the row goes on the
			  ListItem element) — NEVER insert a second crt.List and NEVER put itemLayout inside a merge of the
			  parent List (silent no-op; ListItem is a separate named element).
			- PAGE-level business rules ARE converted for you in guide.pageBusinessRules: each rule keeps
			  its condition and only the actions that survive on mobile. Page rules carry ONLY element
			  actions — hide / show / make-editable / read-only / required / optional — and an action
			  survives only for the referenced elements whose component converts (set-values / apply-filter /
			  apply-static-filter do not exist at page level). The condition ALWAYS converts verbatim — every
			  operand type is supported in a mobile page-rule condition (attribute, const, formula, system-value,
			  system-setting). Recreate each convertedRules[] entry by
			  passing its `rule` VERBATIM to create-page-business-rule on the MOBILE page (after approval).
			  droppedRules[] did not convert (every referenced element drops) — report them.
			  OBJECT-/entity-level business rules are shared across web and mobile — do NOT re-create or touch them.
			- REQUESTS (actions) on component event bindings (a button's `clicked`, a field's `valueChange`/`updated`)
			  ARE handled for you. ONLY a `crt.Button` whose request the Creatio Mobile app does NOT support (and
			  that does not remap to a supported one) is DROPPED (elementMap operation `drop`, reason names the
			  request) — a dead button is not shipped. Other component types are NOT dropped for an unsupported
			  request (some legitimately use a system request absent from the list): their binding is kept verbatim
			  and flagged. A supported request is kept in
			  elementMap[].mobileValues (remapped when the mobile name differs) — paste mobileValues verbatim.
			  guide.requestConversions is the advisory summary (convertedRequests / flaggedRequests); dropped
			  components appear in elementMap as `drop`. Tell the user which action components were removed.
			  Page `handlers` (the web-only AMD section) are NEVER transferred — re-implement that behavior as entity-level business rules.
			- ADAPTIVE LAYOUT (multi-column crt.GridContainer) is two-sided and the guide builds AND bakes both sides
			  into mobileValues for you: the container's per-breakpoint columns (small = 1, medium/large = the web
			  columns) and each child's layoutConfig.adaptive (small = single-column stack; medium/large = the web
			  placement). A single-column grid gets NO adaptive — the mobile client renders the plain config. Just
			  paste mobileValues verbatim; do not hand-build adaptive. The mobile runtime reflows children by
			  `row` / `column`. adaptiveLayout is a PROPOSAL — let the user adjust or decline it at the gate.
			- NEVER drop a property the mobile component supports. The guide already prebuilds each insert's
			  values (elementMap[].mobileValues) by carrying every source property valid on mobile (per the
			  registry) — paste it verbatim and add only the value binding. validate-page is the backstop and
			  rejects an insert that drops a required property (e.g. a field's caption, or a lookup-path
			  attribute's type), and update-page blocks the save.
			- Mobile layout is a simplified vertical flow; complex multi-column desktop layout will likely
			  need manual adaptation in the designer.

			LIMITATIONS (be transparent)
			This does not guarantee a pixel-perfect or behavior-perfect migration. It guarantees a
			deterministic guide: the recommended template, container correspondence, classified components,
			and mobile contracts. The result is a starting point that the user finishes in Freedom UI
			Mobile Designer.
			"""
	};
}
