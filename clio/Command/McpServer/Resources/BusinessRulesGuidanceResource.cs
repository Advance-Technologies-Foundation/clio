using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for creating Freedom UI business rules through clio MCP.
/// </summary>
[McpServerResourceType]
public sealed class BusinessRulesGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/business-rules";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Returns the canonical guidance article for Freedom UI business rules.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "business-rules-guidance")]
	[Description("Returns canonical MCP guidance for Freedom UI business rules: entity-level and page-level conditional logic for field/element visibility, editability, required state, and value assignment.")]
	public ResourceContents GetGuide() => Guide;

	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP business rules guide

		       Scope
		       - Use this guide whenever the requirement involves conditional field/element visibility, editability, required state, auto-assignment of a field value, CLEARING a field value, or lookup filtering.
		       - Writing into a column or clearing a column when another field changes IS a business rule. This is the most common case for two interdependent fields (changing field A auto-fills or wipes field B). Do NOT narrow business rules to only show/hide/enable/require — value population and clearing are first-class business-rule actions (`set-values`), no handler required.
		       - Business rules are SEPARATE first-class artifacts in Creatio Freedom UI. They are NOT implemented as page JavaScript code, handlers, validators, or converters.
		       - Do NOT write JavaScript handler or validator code to implement business-rule behavior. Use the dedicated MCP tools instead.

		       What is a business rule
		       - A business rule is a declarative condition → action definition that Creatio evaluates at runtime.
		       - It consists of a CONDITION GROUP (AND/OR of field-value comparisons) and one or more ACTIONS that fire when the condition is met.
		       - Business rules are created via dedicated MCP tools, not by editing page schema bodies or writing JavaScript.

		       Required MCP contract check
		       - Before creating or modifying business rules (calling `create-entity-business-rule`, `create-page-business-rule`, etc) call `get-tool-contract` to get more information about contract and see examples.

		       State-changing actions are one-way
		       - Actions that change field or element state (visibility, editability, or required state) apply only when their condition is met.
		       - A business rule does not automatically roll back state or apply the inverse action when its condition stops matching.
		       - When a requirement describes state that must switch in both directions, create an explicit inverse business rule for the opposite condition and corresponding opposite action.

		       Two levels of business rules

		       1. Entity-level business rules
		          - Scope: operate on entity schema attributes (columns).
		          - Tool: `create-entity-business-rule`
		          - Supported actions: make-editable, make-read-only, make-required, make-optional, set-values, apply-filter, apply-static-filter.
		          - Use when the rule should apply everywhere the entity is used, regardless of which page displays it.
		          - `apply-filter` targets one lookup field, compares it to one source lookup field on the current record, and may auto-generate child clear/populate rules.
		          - `apply-static-filter` narrows a target lookup by a static ESQ filter expressed in a friendly contract (constants, lookup values by GUID or display name, AND/OR groups, EXISTS/NOT_EXISTS backward references). `rootSchemaName` is inferred from the target lookup's reference schema — never sent by the caller.
		          - When the requirement sounds like a standard dependent lookup UX, prefer `populateValue=true` by default unless the user explicitly asks for one-way filtering only or the selected source/target path shape makes populate unsupported.

		       apply-static-filter friendly filter contract
		       - The FULL filter contract lives in a dedicated guide — call `get-guidance` with name `business-rule-filters` before building any apply-static-filter `filter`.
		       - It covers: action shape, leaf comparisons, lookup values (GUID/display-name), forward paths, nested AND/OR groups, backward EXISTS/NOT_EXISTS and COUNT/SUM/AVG/MIN/MAX aggregations, relative-date and current-user macros, age/birthday translation, multilingual handling, and the no-assumptions discovery flow.
		       - Key reminder: `rootSchemaName` is inferred from the target lookup's reference schema — never sent by the caller; `columnPath` is rooted on that reference schema, not the rule entity.

		       2. Page-level business rules
		          - Scope: operate on page elements (UI controls from viewConfig) and page attributes (from viewModelConfig).
		          - Tool: `create-page-business-rule`
		          - Supported actions: hide-element, show-element, make-editable, make-read-only, make-required, make-optional.
		          - Use when the rule should apply only on a specific page.
		          - Element names come from `get-page` bundle.viewConfig (recursive). Attribute names come from bundle.viewModelConfig.attributes.

		       Decision tree — when to use business rules vs handlers/validators
		       - If the requirement is "when field X equals Y, then hide/show/enable/disable/require/unrequire field Z" (the field itself appears/disappears or becomes editable/required) → use a BUSINESS RULE with hide-element/show-element/make-* actions.
		       - If the requirement is "when field X equals Y, set field Z to value W" → use an ENTITY BUSINESS RULE with set-values action. Do NOT write a handler for this.
		       - If the requirement is "when field X equals Y, clear field Z" → use an ENTITY BUSINESS RULE with set-values action and an empty value for Z. Clearing a field is the same action as setting one — NO handler is needed.
		       - If the requirement is "filter lookup A by the current value of another lookup B on the same record (dependent lookups)" → use an ENTITY BUSINESS RULE with apply-filter action.
		       - If the requirement is "limit / restrict a lookup field to records matching a fixed condition" (e.g. "let users select only records whose <field> equals <value>", "only records where a status flag is set", "show only <records> in the <lookup> field that <condition>", "show the <field> only for <records> where <condition>", "show only <records> that have at least one / more than N <children>") → use an ENTITY BUSINESS RULE with apply-static-filter action. The condition can be unconditional (always filter) or gated: "WHEN field X is Y, limit lookup Z to ..." → put X=Y in the rule's condition group and the apply-static-filter action on Z.
		         - This holds for ANY constraint mechanism, not a fixed list of phrases — classify the requirement into one mechanism and map it to a filter field: attribute value -> a value leaf; now-relative period -> a date macro (valueMacros); fixed calendar/clock part such as a time of day -> datePart; existence/count of related child records -> backwardReferenceFilters; dependent on another field's value -> the gate (X = Y) in the rule's condition group. Every one of these is apply-static-filter, never a handler/crt.InitRequest. Map the mechanism, not the wording; see `business-rule-filters` for the field-by-field contract.
		         - DISAMBIGUATION: this is NOT the same as the page DataSource `staticFilters` / `filterConfig` array inside a Freedom UI `body.js`. To restrict what a lookup/field shows, do NOT hand-edit `body.js`, `filterConfig`, `dataSourceFilters`, or `modelConfig` — use `create-entity-business-rule` with apply-static-filter. The entity rule applies everywhere the lookup is used and is the supported no-code surface; manual `body.js` filter editing is page-scoped, brittle, and bypasses validation.
		         - DISAMBIGUATION (restriction vs visibility): a phrase like "show the <LookupField> only for <records> where <condition>" / "show the Assignee field only for contacts where Age = 30" means RESTRICT which records the lookup OFFERS (apply-static-filter on that lookup, rooted on its reference schema), NOT toggle the field's visibility. Do NOT set the field `visible:false`, do NOT use hide-element/show-element, and do NOT add a page attribute for this. hide-element/show-element are only for requirements about the field itself appearing or disappearing (e.g. "hide the Discount field when Type = Internal"), never about which records a lookup lists. For "...Assignee only for contacts where Age = 30": targetAttribute = the Assignee lookup, filter = { logicalOperation: AND, filters: [ { columnPath: "Age", comparisonType: "EQUAL", value: 30 } ] } (root schema Contact, inferred).
		       - If the requirement is field-value format validation (length, regex, range) → use a VALIDATOR, not a business rule.
		       - If the requirement is cross-field orchestration with side effects (API calls, process launch, data loading) → use a HANDLER.
		       - If the requirement is display-only value transformation → use a CONVERTER.
		       - When in doubt, prefer business rules over handlers for simple conditional visibility/editability/required logic.

		       Workflow
		       1. Call `get-tool-contract` for `create-entity-business-rule` or `create-page-business-rule`.
		       2. Read entity schema columns with `get-entity-schema-column-properties` or page structure with `get-page`.
		       3. Determine whether the rule is entity-level or page-level.
		       4. For state-changing requirements, decide whether the state must also return through an explicit inverse rule.
		       5. Resolve every lookup condition value and lookup set-values constant with `odata-read` or `execute-esq`. Example lookup read by display value with `odata-read`: entity `Contact`, filters `{ "all": [{ "field": "Name", "op": "contains", "value": "Andrew" }] }`, select `["Id","Name"]`, top `5`.
		       6. Build the condition group and actions.
		          - For `apply-filter`, use an empty condition group and put the lookup-filter configuration into the action payload.
		          - If the user did not specify otherwise and the scenario is a normal dependent lookup, default `populateValue` to `true` so the reverse helper child rule is generated too.
		       7. Call `create-entity-business-rule` or `create-page-business-rule`.
		       8. Verify by checking the entity or page on the environment.

		       Common mistakes to avoid
		       - Do NOT add visibility/editability/required toggling logic in SCHEMA_HANDLERS — use business rules.
		       - Do NOT confuse business rules with business logic. Business rules are declarative condition→action pairs, not imperative code.
		       - Do NOT write `$context.enableAttribute()`/`$context.disableAttribute()` handlers when a business rule suffices.
		       - Do NOT duplicate entity-level rules on every page. If the rule applies to the entity globally, create it at the entity level.
		       - Do NOT assume a state-changing business rule automatically reverses itself. Use an explicit inverse rule when both directions are required.
		       - Do NOT call a business-rule creation tool before reading its `get-tool-contract` entry.
		       - Do NOT use random GUIDs for lookup constants. Resolve or verify them with `odata-read` (or `execute-esq`).
		       - Do NOT author `...Id` paths by reflex when the target filter contract expects object-path semantics such as `CreatedBy` or `[Contact:Account]`.
		       - Do NOT serialize date-relative wording like "this year" or "next 7 days" as plain text constants.
		       - Do NOT use `apply-filter` on non-lookup targets or non-lookup sources.
		       - Do NOT use `targetFilterPath` or `sourceFilterPath` that resolve to scalar `Guid` columns such as `Lookup.Id`; those paths must resolve to Lookup attributes.
		       - Do NOT set `populateValue=true` together with `sourceFilterPath`; current platform behavior does not support that combination cleanly.
		       - Do NOT use kebab-case comparison tokens (`equal`, `is-filled-in`) inside an apply-static-filter leaf `comparisonType`; that field uses UPPER_SNAKE_CASE (`EQUAL`, `IS_NOT_NULL`).
		       - Do NOT root apply-static-filter `columnPath` on the rule's entity; it is rooted on the target lookup's reference schema.
		       - Do NOT use a backward EXISTS to express a simple "field is filled" check; use IS_NOT_NULL on the column.
		       """
	};
}
