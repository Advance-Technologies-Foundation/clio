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
			  - modelConfigDiff / viewModelConfigDiff — READY-TO-PASTE diffs (each a single root merge of the
			    full config). Paste them VERBATIM as the page's modelConfigDiff / viewModelConfigDiff
			    (see DATA SECTIONS below). This is the supported way to apply the data sections.
			  - modelConfig / viewModelConfig — the same configs in full-object form, for REFERENCE only.
			    viewModelConfig is already FILTERED (attributes used only by dropped components removed).
			  - constraints + nextSteps — the hard mobile rules and the ordered flow.

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
			3. Create the target mobile page from recommendedMobileTemplate (list-page-templates with
			   schema-type "mobile" to confirm; create-page). The template provides the Scaffold root —
			   do NOT add a second Scaffold.
			4. Build the mobile body (plain JSON: viewConfigDiff / viewModelConfigDiff / modelConfigDiff)
			   by iterating elementMap. For each entry act on its operation:
			   - merge — the element is provided by the mobile template (a "twin", e.g. Tabs→Tabs,
			     FeedTabContainer→FeedContainer). REUSE the existing mobileName; do NOT insert it. (Insert
			     vs merge is the #1 mistake — the template already contains these elements.)
			   - insert — add mobileType under parentName/propertyName (propertyName defaults to "items").
			     If captionResource is present, register key = sourceValue with update-page `resources`
			     (the key follows "<MobileElementName>_caption"). Build values from the matching
			     mobileContracts entry (allowedProperties / designerDefaults / example); call
			     get-component-info (schema-type "mobile") only when you need more than the inline contract.
			     For structural mappings (grid -> crt.List), build itemLayout.body from the contract example.
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
			6. Validate the body with validate-page; resolve any findings (e.g. a binding whose attribute
			   is not declared) before treating the page as done.
			7. Persist with update-page. Then tell the user to open the result in Freedom UI Mobile
			   Designer for final layout review.

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

			CHECKLIST before validate-page: verify every data-source attribute whose "path" contains a "."
			still has its "type". (validate-page now also flags this as an error and update-page blocks the save.)

			─────────────────────────────────────────────────────────────
			HARD MOBILE RULES (see also get-guidance `mobile-page-modification`)
			─────────────────────────────────────────────────────────────
			- Mobile body is plain JSON with only viewConfigDiff / viewModelConfigDiff / modelConfigDiff.
			- NO handlers, NO validators, NO custom converters. Re-implement conditional visibility /
			  required / read-only / set-value logic as ENTITY-LEVEL business rules
			  (create-entity-business-rule). Page-level business rules are NOT transferred automatically —
			  review and recreate the supported ones manually.
			- One data source per page. If the web page used several (see guide.dataSources), keep only
			  the primary one.
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
