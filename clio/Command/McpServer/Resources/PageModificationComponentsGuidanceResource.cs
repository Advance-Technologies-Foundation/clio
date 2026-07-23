using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Focused sub-guide of the <c>page-modification</c> family: composing viewConfigDiff inserts —
/// adding a button with a click handler, the rules for the handlers and viewConfigDiff sections, the
/// column-type-to-control mapping, and how to read a <c>get-component-info</c> response when building
/// an insert.
/// </summary>
[McpServerResourceType]
public sealed class PageModificationComponentsGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/page-modification-components";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP page modification components guide

		       This is a focused sub-guide of `page-modification`. Read `page-modification` FIRST and follow its
		       pre-edit GATE checklist (including the mandatory COMPONENT-TYPE VERIFICATION step); read
		       `page-modification-containers` for picking the `parentName`. This guide owns viewConfigDiff INSERT
		       composition — adding a button + handler, the operation/type/name rules, the column-type-to-control
		       mapping, and how to read a `get-component-info` response. For inserting a data-bound FIELD (the
		       binding/label contract) read `page-modification-field-contract`.

		       Adding a button with a click handler
		       Body structure for `update-page` (preserve all marker pairs — do not remove or reorder them):

		       ```
		       define("<PageName>", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
		           return {
		               viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
		                   {
		                       "operation": "insert",
		                       "name": "UsrMyButton",
		                       "values": {
		                           "type": "crt.Button",
		                           "visible": true,
		                           "caption": "$Resources.Strings.UsrMyButton_caption",
		                           "clicked": { "request": "usr.MyClickRequest" }
		                       },
		                       "parentName": "FilterGridContainer",
		                       "propertyName": "items",
		                       "index": 0
		                   }
		               ]/**SCHEMA_VIEW_CONFIG_DIFF*/,
		               viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
		               modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
		               handlers: /**SCHEMA_HANDLERS*/[
		                   {
		                       request: "usr.MyClickRequest",
		                       handler: async (request) => {
		                           // run custom logic here. To show a user-facing message (confirmation/info), dispatch crt.ShowDialogRequest - see page-schema-handlers. NEVER use alert()/window.alert()/confirm()/prompt().
		                           return request.next?.handle(request);
		                       }
		                   }
		               ]/**SCHEMA_HANDLERS*/,
		               converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
		               validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
		           };
		       });
		       ```

		       Rules for the handlers block
		       - Contents between `/**SCHEMA_HANDLERS*/` markers is raw JavaScript, NOT JSON. Use unquoted keys (`request`, `handler`) and arrow functions or `function` expressions.
		       - Every `viewConfigDiff` entry whose `values.clicked.request` is `usr.*` (or any custom namespace) MUST have a matching `handler` entry with the same `request` string.
		       - Always end custom handlers with `return request.next?.handle(request);` to propagate to the default pipeline. Omitting it breaks page lifecycle events.
		       - Built-in requests (`crt.*`) already have default handlers — don't duplicate them unless you intend to override.
		       - To show a user-facing message (confirmation/info/success/error) from a handler, dispatch `crt.ShowDialogRequest` (message/actions under `dialogConfig.data`); this needs `@creatio-devkit/common` in `SCHEMA_DEPS` and the `sdk` alias in `SCHEMA_ARGS` (see `page-schema-handlers`). NEVER use `alert(...)`, `window.alert(...)`, `confirm(...)`, or `prompt(...)`.
		       (For non-trivial handler logic — business orchestration, SDK service calls, attribute-change handling — read `page-schema-handlers`.)

		       Rules for viewConfigDiff
		       - `operation` must be one of: `insert`, `remove`, `merge`, `move`.
		       - `type` (the component type inside `values`) MUST be a type you confirmed exists via `get-component-info` for the target environment — see the mandatory COMPONENT-TYPE VERIFICATION STOP in `page-modification`. Never invent or guess a `crt.*` type; an unknown type saves successfully but renders as a broken placeholder. If no catalog component matches the requirement, stop and ask the user (use an existing component, or build a custom one).
		       - `name` is the unique component id inside the hierarchy. Prefix custom components with `Usr` or project-specific prefix to avoid collisions. For entity-bound FormPage fields, the `control` binding uses the view-model attribute key — commonly `$PDS_<Column>` for designer-generated attributes against the primary data source, but may be `$Usr<Column>`, `$PageParameters_<Name>`, or another prefix depending on how the attribute was defined. Copy the attribute key from the existing binding rather than constructing one from the column name; use `get-component-info` for ready-to-use examples.
		       - `parentName` must match an existing container name from `bundle.containers` (see `page-modification-containers`).
		       - `propertyName` is usually `items` for containers. For the exact slot a given component exposes and how to populate it, read that component's `get-component-info` contract (see "Finding a container for a new component" in `page-modification-containers`).
		       - `index` is the insertion position within `parentName.items[]`.
		       - `visible` is a view-engine property, not a component-specific one. It can appear in the `values` object of ANY view element alongside `type` and element-specific properties. Accepts `true`, `false`, or a binding expression (e.g. `"$SomeAttr | crt.InvertBooleanValue"`). Applies equally to web and mobile.
		       - User-visible string values inside `values` (`label`, `caption`, `title`, `tooltip`, `placeholder`, `description`, button captions, tab/group titles — examples, not an exhaustive list; the rule covers ANY string-like property the runtime renders to the user) MUST be authored as `$Resources.Strings.<Key>` bindings, not inline string literals. Read `page-schema-resources` first to decide whether the key requires explicit registration via the `resources` parameter (DS-bound attributes auto-provide the caption; custom keys must be registered). Sole carve-out (inverse): `crt.ImageInput.tooltip` ignores localizableStrings and MUST be a plain literal — a resource binding there is rejected and renders empty.
		       - For entity-bound FormPage data-entry fields, match the column DataValueType to the control: `ShortText`/`MediumText`/`LongText` → `crt.Input`; `Lookup` → `crt.ComboBox`; `Boolean` → `crt.Checkbox`; `DateTime`/`Date`/`Time` → `crt.DateTimePicker`; `Integer`/`Float`/`Money` → `crt.NumberInput`; `Email` → `crt.EmailInput`; `PhoneNumber` → `crt.PhoneInput`; `WebLink` → `crt.WebInput`. Use `get-component-info` for full insert examples. For display-only transformations (email as mailto link, phone as tel link) read `page-schema-converters` first — do not select a component type for display tasks. (The binding/label contract for inserting such a field is in `page-modification-field-contract`.)

		       Canonical flow to add a Test button to Accounts_ListPage
		       1. `list-pages filter=Accounts_List` → resolve schema name.
		       2. `get-page schema-name=Accounts_ListPage` → response contains `bundle.containers` (flat list of valid parentName values) and `raw.body` (empty replacing template if no replacement exists yet).
		       3. Pick a container from `bundle.containers`: filter by `type == "crt.FlexContainer"` and non-zero `childCount`; use its `name` as `parentName`.
		       4. Compose body: start from `raw.body` (or the template above), add button entry to `viewConfigDiff` with the chosen `parentName`, add matching handler to `handlers`.
		       5. `update-page schema-name=Accounts_ListPage body=<composed body> verify:true`.
		       6. Response includes `page.schemaUId` — the newly-materialized replacing schema in the design package.

		       Interpreting get-component-info response metadata
		       - Every response carries `resolvedTargetVersion` (the catalog version actually loaded) and `resolvedFrom` (the resolver tier that selected it).
		       - Resolve the platform version BEFORE you generate an implementation plan: pass `environment-name` so clio probes the stand and reports the real `resolvedFrom`. Do not plan a page change against an unverified component set.
		       - `resolvedFrom: "environment"` means clio resolved the platform version from the active environment AND the exact per-version catalog was loaded. Treat the catalog as authoritative for `update-page` and proceed — no confirmation needed.
		       - `resolvedFrom: "environment-superset"` means the platform version was known (probe-success or explicit `--version`) but the exact catalog was not published on the CDN, so `latest` was served as the closest available. The version is not a mystery, but the `latest` catalog may include components not yet present in an older GA target environment. Flag this to the user and verify critical component types against the actual environment before committing to an implementation plan.
		       - `resolvedFrom: "latest-fallback"` means no usable platform version was resolved (no active environment, cliogate < `2.0.0.32`, probe failed, or `CoreVersion` did not parse). The response sets `requiresVersionConfirmation: true` (a machine-readable flag — branch on it, do not rely on reading the prose caveat) and carries the most recent platform catalog clio knows of, which may be a superset of the target environment. STOP: do NOT silently assume this component set. Tell the user the target platform version could not be determined and request explicit confirmation before proceeding against `latest`. `resolvedFromReason` tells you whether the failure is transient (`probe-error` — a retry or a reachable environment may resolve it) or stable (`no-active-environment` / `core-version-missing` / `core-version-unparseable` — supply an explicit version). You may still use the catalog for discovery, but a component or property it lists may not exist on the target stand — `update-page` rejecting it is a legitimate signal the catalog was wider than the platform.
		       - Do not paper over a `latest-fallback` by pinning a target version yourself. Fix the upstream signal (active environment, cliogate version) so the next call resolves to `"environment"` or `"environment-superset"`.
		       - Discover proactively: at the start of page work call `get-component-info` in list mode (omit `component-type`) to enumerate the full component set for the resolved version. Non-obvious components (e.g. `crt.Gallery`) are in the catalog — consider and suggest them when relevant instead of waiting for the user to ask you to search.
		       - Use the selection-metadata to choose between similar components: a detail response may carry `whenToUse` / `whenNotToUse` (one-line "pick this when…" / "do NOT pick this when…" guidance) plus `synonyms` / `useCases`. When two types look interchangeable (e.g. `crt.Gallery` vs `crt.DataGrid` vs `crt.List`), read `whenToUse`/`whenNotToUse` and follow the steer instead of defaulting to the first familiar type. `appliesToCustomEntities: false` means the component cannot be built on a custom entity (see `entityCouplingNote`).

		       Detail-response payload shape (read once before composing `update-page` bodies; same on web and mobile flavors — pass `schema-type: "mobile"` to query the mobile catalog through the same pipeline)
		       - `inputs` — the curated input bindings for the component (e.g. `caption`, `disabled`, `color` on `crt.Button`). Each value carries `type` and may carry `default`, `description`, `values` (enum constraints), `items` (array element type), `keyType`/`valueType` (record shape). Map these directly onto the `values` object of a `viewConfigDiff` insert.
		       - `outputs` — the curated output bindings (events) for the component (e.g. `clicked`, `blurred`, `focused`). Output bindings are bound through `request` descriptors in the body — match each `outputs.<name>` to a `viewConfigDiff` entry's `values.<name>.request` and add a matching `handlers` entry with the same `request` string.
		       - `references.typeDefinitions` — the producer's named-type schemas referenced by `inputs`/`outputs` `type` strings (e.g. `"string | ButtonIcon | ButtonAnimatedIcon"`). When a `type` token is not a primitive (`string`/`number`/`boolean`/`array`/`object`/`Record`), look it up here to learn the allowed values (enum) or the nested `fields` shape. Without this, you cannot construct a valid `icon`, `columns`, `bulkActions`, etc.
		       - `properties` — present only for legacy catalog entries that did not migrate to the `inputs`/`outputs` split. Today the mobile catalog still ships in the legacy shape, so mobile detail responses carry `properties` and omit `inputs`/`outputs`/`references.typeDefinitions`; web responses carry the wrapped fields. Treat whichever is present as authoritative for that component — both describe the same surface, just different schema generations.
		       - `documentation` — opt-in long-form markdown for complex components (e.g. `crt.DataGrid`); concatenated from every file listed in the producer's `references.docs[]`. Use it as the source of truth for non-trivial composition rules (e.g. data-grid features matrix). Absent on simple components and on mobile entries today — do not interpret its absence as missing data.
		       - `resolvedTargetVersion` / `resolvedFrom` — present on every detail response regardless of `schema-type`. Mobile and web share the same async catalog pipeline, so both carry the resolver markers.
		       """
	};

	/// <summary>
	/// Returns the canonical viewConfigDiff-composition sub-guide of the page-modification family.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "page-modification-components-guidance")]
	[Description("Returns the viewConfigDiff-composition sub-guide of the page-modification family: adding a button with a click handler, the handlers/viewConfigDiff section rules, the column-type-to-control mapping, the canonical add-button flow, and how to read a get-component-info detail response.")]
	public ResourceContents GetGuide() => Guide;
}
