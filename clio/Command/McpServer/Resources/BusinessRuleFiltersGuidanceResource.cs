using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for the friendly-filter contract used by the
/// <c>apply-static-filter</c> business-rule action of <c>create-entity-business-rule</c>.
/// </summary>
[McpServerResourceType]
public sealed class BusinessRuleFiltersGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/business-rule-filters";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP business-rule filters guide

		       Scope
		       - Use this guide when authoring the `apply-static-filter` action of `create-entity-business-rule`.
		       - This action restricts a Lookup column on the target entity by a static filter against the lookup's reference schema.
		       - Resolve exact MCP tool contracts through `get-tool-contract` before any write workflow.
		       - Out of scope: page-level rules, multi-id `InFilter` lookup values, datetime macros (PREVIOUS_HOUR, etc.), column-to-column right operands.

		       When to choose apply-static-filter
		       - The user wants the lookup picker on a column to show only matching reference-schema rows.
		       - Example: "When the rule case matches, restrict the City lookup so only cities whose Country equals Ukraine are visible."
		       - Do NOT use this action for hiding/disabling fields (use make-read-only / make-required) or for assigning constants (use set-values).

		       Wire shape
		       - The friendly action object lives inside `rule.actions[]`:
		         {
		           "type": "apply-static-filter",
		           "targetAttribute": "<LookupColumnName>",
		           "filter": {
		             "logicalOperation": "AND",
		             "filters": [
		               {
		                 "columnPath": "<ColumnOnReferenceSchema>",
		                 "comparisonType": "EQUAL",
		                 "value": "<JsonPrimitiveOrGuidString>"
		               }
		             ],
		             "backwardReferenceFilters": []
		           }
		         }
		       - `type`, `targetAttribute`, and `filter` are required.
		       - `items` MUST NOT be sent for `type=apply-static-filter`. Sending `items` raises `filter.items-not-allowed`.

		       Inferred root schema rule
		       - Do NOT pass a `rootSchemaName`. clio infers it from `targetAttribute` by reading the entity schema and resolving the Lookup column's reference schema.
		       - Example: target entity is `City`; `targetAttribute` is `Country`; the inferred root schema for the friendly filter is the Country reference schema. The `columnPath` values inside `filter.filters[*]` resolve against that reference schema, not against `City`.

		       Logical operations
		       - `filter.logicalOperation` accepts only `"AND"` and `"OR"`. Anything else raises `filter.logical-operation-unknown`.
		       - The same vocabulary applies to nested groups inside `backwardReferenceFilters[*].filter`.

		       Comparison types
		       - The friendly tokens map to a fixed set of integer codes. Use the exact uppercase tokens.
		         | Token              | Allowed leaf datatypes                          |
		         | ------------------ | ----------------------------------------------- |
		         | EQUAL              | All except RichText / Image                     |
		         | NOT_EQUAL          | All except RichText / Image                     |
		         | GREATER            | Numeric and Date / DateTime / Time              |
		         | GREATER_OR_EQUAL   | Numeric and Date / DateTime / Time              |
		         | LESS               | Numeric and Date / DateTime / Time              |
		         | LESS_OR_EQUAL      | Numeric and Date / DateTime / Time              |
		         | IS_NULL            | All (omit `value`)                              |
		         | IS_NOT_NULL        | All (omit `value`)                              |
		         | START_WITH         | Text only                                       |
		         | NOT_START_WITH     | Text only                                       |
		         | CONTAIN            | Text only                                       |
		         | NOT_CONTAIN        | Text only                                       |
		         | END_WITH           | Text only                                       |
		         | NOT_END_WITH       | Text only                                       |
		       - Unknown tokens raise `filter.comparison-unknown`. Mismatched datatypes raise `filter.comparison-not-supported-for-datatype`.

		       Leaf value shape
		       - For Text columns, send a JSON string.
		       - For numeric columns (Integer, Float, Money, FloatN), send a JSON number.
		       - For Boolean columns, send a JSON boolean (`true` or `false`).
		       - For Date / DateTime / Time columns, send an ISO 8601 string. DateTime and Time require an explicit timezone suffix (`Z` or `+/-HH:mm`); Date uses `yyyy-MM-dd`.
		       - For Lookup columns, send a GUID string referencing an existing record in the column's reference schema. See "Lookup-record validation" below.
		       - Wrong JSON shape raises `filter.value-shape`.
		       - Missing operand for a binary comparison raises `filter.value-required`. Including `value` for IS_NULL / IS_NOT_NULL is not an error but is ignored.

		       Lookup-record validation
		       - Lookup leaves are validated for record existence at create time by the server-side `LlmEsqConverterService`, mirroring the creatio-ui lookup picker.
		       - The leaf value must be a GUID string referencing an existing record in the resolved reference schema. A non-existent or non-GUID value surfaces back to clio as `filter.server-rejected` with the server message; for the missing-record case, the underlying server condition is `filter.lookup-record-not-found`.
		       - The server reads `(Id, Name, DisplayValue)` from the reference schema and splices `{ Name, Id, value, displayValue }` directly into the emitted envelope for UI parity; clio embeds the envelope verbatim into `BusinessRuleValueExpression.value`.
		       - Post-save deletions are NOT tracked. If the referenced record is removed after the rule is saved, the runtime owns that scenario; clio does not retro-validate.

		       Backward-reference filters
		       - Use `backwardReferenceFilters[]` when the friendly filter has to traverse a 1:N relationship from the inferred root schema to a child schema.
		       - Each entry is `{ "referenceColumnPath": "[ChildSchema:ColumnOnChild]", "filter": <StaticFilterGroup>, "aggregationType"?: ..., ... }`. The bracketed segment is the only place backward references are accepted.
		       - The nested `filter` is validated against the child schema, not the root.
		       - A backward reference whose path is absent or does not point to a 1:N lookup of the root raises `filter.backward-reference-not-1n`.
		       - Putting a `[ChildSchema:Column]` segment inside a regular `filters[*].columnPath` raises `filter.path-resolves-to-collection`. Move it to `backwardReferenceFilters[]` instead.
		       - Optional aggregation fields on a backward reference:
		         - `aggregationType` -- one of `EXISTS` (default when omitted), `NOT_EXISTS`, `COUNT`, `SUM`, `AVG`, `MIN`, `MAX`.
		         - `EXISTS` / `NOT_EXISTS` -- no other aggregation fields allowed; semantics: at least one (or no) child record matches the nested `filter`.
		         - `COUNT` -- requires `comparisonType` + numeric `aggregationValue`; aggregates record count.
		         - `SUM` / `AVG` / `MIN` / `MAX` -- additionally require `aggregationColumnPath` (a numeric column on the child schema). Both `comparisonType` (any of the 14 binary tokens) and numeric `aggregationValue` are required.
		       - Aggregation errors: `filter.comparison-unknown` (bad `aggregationType` or missing `comparisonType` for non-EXISTS), `filter.value-required` (missing `aggregationValue`), `filter.path-unknown` (missing or non-existent `aggregationColumnPath`), `filter.comparison-not-supported-for-datatype` (aggregationColumnPath is not numeric for SUM/AVG/MIN/MAX), `filter.value-shape` (aggregationValue not a JSON number).

		       Column path rules
		       - `columnPath` is a dotted path through Lookup hops, rooted at the inferred reference schema (NOT the original entity schema).
		       - All segments before the last must be Lookup columns; the last segment is the leaf the comparison applies to.
		       - Unknown segments raise `filter.path-unknown`.
		       - Examples (assume target attribute resolves to `Country` reference schema):
		         - `Name` -- compare on the Country.Name column.
		         - `Region.Name` -- traverse Country.Region (Lookup) and compare on the related Region.Name column.
		         - `[Activity:Country].Subject` -- INVALID inside `filters[]`. Use `backwardReferenceFilters[]`.

		       Error code reference
		       - All filter-side errors surface as `ArgumentException` with the message `<errorCode>: <message> (path=<fieldPath>)`.
		       - Local (clio-side) codes raised before the server-side conversion is invoked:
		         - `filter.target-attribute-required` -- `targetAttribute` missing or blank.
		         - `filter.target-attribute-unknown` -- `targetAttribute` is not a column on the entity schema.
		         - `filter.target-attribute-not-lookup` -- `targetAttribute` is not a Lookup column with a reference schema.
		         - `filter.items-not-allowed` -- `items` provided alongside `targetAttribute` / `filter`.
		         - `filter.required` -- `filter` missing.
		         - `filter.logical-operation-unknown` -- unknown logical token (only AND / OR are accepted).
		         - `filter.comparison-unknown` -- unknown comparison token (use the table above).
		         - `filter.value-required` -- leaf value missing for a binary comparison.
		         - `filter.path-unknown` -- leaf with empty `columnPath`.
		         - `filter.backward-reference-not-1n` -- backward-reference filter without a `referenceColumnPath`.
		       - Server-side rejection (delegated to `LlmEsqConverterService`):
		         - `filter.server-rejected` -- the server rejected the friendly filter. The error message includes the server's underlying reason and may surface conditions like `filter.path-unknown`, `filter.path-resolves-to-collection`, `filter.comparison-not-supported-for-datatype`, `filter.value-shape`, `filter.lookup-value-not-guid`, `filter.lookup-record-not-found`, or `filter.backward-reference-not-1n` with the original server detail.

		       Minimal canonical payloads
		       - Single-leaf EQUAL on a Lookup target:
		         {
		           "type": "apply-static-filter",
		           "targetAttribute": "Country",
		           "filter": {
		             "logicalOperation": "AND",
		             "filters": [
		               { "columnPath": "Name", "comparisonType": "EQUAL", "value": "Ukraine" }
		             ],
		             "backwardReferenceFilters": []
		           }
		         }
		       - OR group with two text comparisons:
		         {
		           "type": "apply-static-filter",
		           "targetAttribute": "Country",
		           "filter": {
		             "logicalOperation": "OR",
		             "filters": [
		               { "columnPath": "Name", "comparisonType": "START_WITH", "value": "U" },
		               { "columnPath": "Name", "comparisonType": "START_WITH", "value": "K" }
		             ],
		             "backwardReferenceFilters": []
		           }
		         }
		       - Backward reference (City rule whose Country lookup must point to a country with at least one matching contact):
		         {
		           "type": "apply-static-filter",
		           "targetAttribute": "Country",
		           "filter": {
		             "logicalOperation": "AND",
		             "filters": [],
		             "backwardReferenceFilters": [
		               {
		                 "referenceColumnPath": "[Contact:Country]",
		                 "filter": {
		                   "logicalOperation": "AND",
		                   "filters": [
		                     { "columnPath": "Type.Name", "comparisonType": "EQUAL", "value": "Customer" }
		                   ],
		                   "backwardReferenceFilters": []
		                 }
		               }
		             ]
		           }
		         }

		       Discovery flow (primary: DataForge, fallback: MCP native)
		       - Preferred: use the DataForge MCP tools to discover schemas, columns, and 1:N relations before composing the filter -- they index Creatio metadata in one place and return fast, structured results.
		         - Tables / lookup root schemas: `dataforge-find-tables`, `dataforge-find-lookups`.
		         - Columns on a schema: `dataforge-get-table-columns`.
		         - 1:N relation paths (input for backward references): `dataforge-get-relations`.
		         - Aggregated context for a single AI request: `dataforge-context`.
		       - Fallback when DataForge is not available, not configured for the target environment, or has not finished indexing the requested schema (`dataforge-health` reports `data-structure-readiness=false` or `lookups-readiness=false`):
		         - Tables / schemas: `find-entity-schema`.
		         - Columns: `get-entity-schema-properties`.
		         - Reference schema names already live in `EntityDesignSchemaDto.ReferenceSchema` returned by `get-entity-schema-properties`, so forward paths can be assembled the same way as with DataForge output.
		         - 1:N child-to-parent relations: search columns on candidate child schemas via `get-entity-schema-properties` and pick the Lookup that points back to the parent.
		       - Either discovery path produces the same friendly-filter payload; clio's structural and schema-aware validators run identically. No runtime dependency on DataForge.

		       columnPath: Name or UId accepted
		       - Every segment in `filters[*].columnPath` may be either the column `Name` (e.g. `Country`) or the column `UId` (GUID string, e.g. `3b2f8b1d-1234-...`).
		       - clio auto-detects GUID-shape segments and looks them up by `Column.UId` on the schema; otherwise it falls back to a name lookup.
		       - The emitted BVE1 envelope is always rewritten to use canonical `Name` segments so the platform runtime resolves the path without UId mapping.
		       - Use UIds when the AI assistant wants explicit identity (no risk of name collisions across packages) and Names when readability matters.

		       Anti-patterns
		       - Do NOT mix `items` with `targetAttribute` / `filter` for `type=apply-static-filter`.
		       - Do NOT pass `rootSchemaName` -- it is inferred.
		       - Multi-id arrays ARE supported for Lookup leaves with `EQUAL` / `NOT_EQUAL`: pass `value: ["guid1", "guid2", "display-name", ...]`. Mixed GUID + display-name entries resolve per-element.
		       - Do NOT use backward-reference brackets `[Schema:Column]` inside `filters[*].columnPath`.
		       - Display-name lookup values are resolved automatically against the lookup's primary display column; ambiguous (multiple matches) and no-match cases surface as `filter.lookup-record-not-found`.
		       - Do NOT invent comparison or logical tokens; the vocabularies above are exhaustive for clio Phase 1.

		       Intent decomposition (natural-language prompt -> payload)
		       - Before composing a payload, decompose the user's sentence into three independent slots:
		         1. **Target lookup column** -- the lookup attribute on the *entity being designed* whose picker the rule will narrow. This is the noun that names the records the user wants to choose from in the form.
		         2. **Forward leaf filters** -- attributes ON the reference schema (or its forward lookups) that must hold. These go into `filter.filters[]`.
		         3. **Backward-reference filters** -- relationships FROM the reference schema TO child schemas. These go into `filter.backwardReferenceFilters[]`.
		       - Recurring sentence patterns and how to translate them:
		         | English fragment                              | Goes into                              | Aggregation                                |
		         | --------------------------------------------- | -------------------------------------- | ------------------------------------------ |
		         | "where X = Y", "with X = Y", "of type Y"      | `filters[]` leaf, EQUAL                | -                                          |
		         | "without any X", "with no X", "that have no X"| `backwardReferenceFilters[]`           | `NOT_EXISTS` *or* `COUNT` + `EQUAL` + 0    |
		         | "with at least one X", "who have an X"        | `backwardReferenceFilters[]`           | `EXISTS` (default; omit `aggregationType`) |
		         | "with more than N X"                          | `backwardReferenceFilters[]`           | `COUNT` + `GREATER` + N                    |
		         | "with at least N X"                           | `backwardReferenceFilters[]`           | `COUNT` + `GREATER_OR_EQUAL` + N           |
		         | "with total X > N", "whose sum of X > N"      | `backwardReferenceFilters[]`           | `SUM` + `GREATER` + N + `aggregationColumnPath` |
		         | "with most recent X in <period>"              | `backwardReferenceFilters[]`           | `MAX` over Date column + relational compare |
		       - The aggregated COUNT/SUM/AVG/MIN/MAX leaf path always navigates THROUGH the backward reference; clio prepends `[Child:Column].` automatically, so the caller only supplies the column name on the child (e.g. `aggregationColumnPath = "Amount"`, not `[Opportunity:Account].Amount`).

		       Creatio domain vocabulary (apply this BEFORE searching schemas)
		       - "Customer" / "Partner" / "Vendor" / "Supplier" -> these are *values* of `Account.Type` (a Lookup into `AccountType`). They are NOT the schema name. The target lookup is almost always `UsrAccount` (or any column whose reference schema is `Account`), and the filter leaf compares the Lookup column itself: `{ "columnPath": "Type", "comparisonType": "EQUAL", "value": "Customer" }`. clio resolves the display name to the AccountType record id and emits the full Freedom UI lookup parameter `{Name, Id, value, displayValue}` so the rule editor renders the value by name. Do NOT use `Type.Name` -- that forwards through the lookup into a text column and emits a Text CompareFilter, which the rule editor renders as a raw string instead of a lookup chip.
		       - "Contact" -> the `Contact` schema. People, not companies. Distinct from `Account.Type = Customer`.
		       - "Lead" -> the `Lead` schema with column `QualifiedAccount` (Lookup -> Account) and `QualifiedContact` (Lookup -> Contact). Backward reference from Account uses `[Lead:QualifiedAccount]`; from Contact, `[Lead:QualifiedContact]`.
		       - "Opportunity" -> the `Opportunity` schema with `Account` and `Contact` lookups. Backward references: `[Opportunity:Account]`, `[Opportunity:Contact]`.
		       - "Activity" -> the `Activity` schema. Common owning lookups: `Owner` (-> Contact), `Account`, `Contact`. Use `[Activity:Owner]` for the contact who owns the activity.
		       - "Order" / "Invoice" / "Product" / "Project" / "Case" -> dedicated schemas. Discover their owning lookup via `dataforge-get-relations` or `get-entity-schema-properties` on the candidate child before assuming the column name.
		       - "Industry", "Country", "Region", "City" -> Account / Contact forward lookups. The leaf filter pattern is `Industry.Name = "IT companies"` (forward through `Account.Industry`), not a backward reference.
		       - When in doubt about whether a noun is a schema or a lookup value: schemas have their own primary key (you can `find-entity-schema --name <Noun>`); lookup values do not appear as a schema name (e.g. `find-entity-schema --name Customer` returns nothing -- it is an `AccountType` row).

		       Lookup vs forward-text comparison (do NOT confuse)
		       - Exact equality on a Lookup column -> compare the Lookup column itself, pass the display name (or GUID) as `value`:
		         `{ "columnPath": "Type", "comparisonType": "EQUAL", "value": "Customer" }` -- clio resolves to a GUID + full lookup-parameter object, server emits `Terrasoft.InFilter` (`filterType=4`), `referenceSchemaName` is populated, and the rule UI shows a coloured chip. This is the canonical pattern.
		       - Inequality (NOT_EQUAL) on a Lookup column -> same forward shape, comparison flips: `{ "columnPath": "Type", "comparisonType": "NOT_EQUAL", "value": "Customer" }`.
		       - Multi-value IN on a Lookup column -> array value with EQUAL / NOT_EQUAL: `{ "columnPath": "Industry", "comparisonType": "EQUAL", "value": ["IT companies", "Banks"] }`.
		       - Partial text match (CONTAIN / START_WITH / END_WITH / NOT_CONTAIN / ...) on a lookup's display name -> *must* forward through the lookup into its text column because CONTAIN is text-only: `{ "columnPath": "Industry.Name", "comparisonType": "CONTAIN", "value": "IT" }`. Use this ONLY when the user explicitly wants a substring / prefix / suffix match; for exact equality always stay on the Lookup column itself.
		       - Rule of thumb: if the user said an exact value name -> compare the Lookup column. If the user said "starts with", "contains", "ends with", "like" -> forward into the lookup's text column with the matching string-match token.

		       Pre-payload checklist for backward references
		       1. Identify the parent (reference schema of the target lookup).
		       2. Identify the child schema -- the noun on the OTHER side of "with" / "without" / "who has".
		       3. On the child, find the Lookup column that points back to the parent. Confirm with `get-entity-schema-properties --schema-name <Child>` and look for `reference schema: <Parent>`. The bracket form `[Child:ThatColumn]` is what goes into `referenceColumnPath`.
		       4. If the user said "without" / "no" / "0", use `aggregationType: NOT_EXISTS` (no `comparisonType` / `aggregationValue`) OR `aggregationType: COUNT` with `comparisonType: EQUAL` and `aggregationValue: 0`. Prefer `NOT_EXISTS` -- it is cheaper at runtime.
		       5. If the user said a number, COUNT with a relational comparison.
		       6. If the user said "total" / "sum" / "average" / "max" / "min" on an attribute of the child, use SUM / AVG / MAX / MIN and pass that child column as `aggregationColumnPath` (bare column name, NOT bracketed).

		       Worked examples (natural language -> friendly payload)
		       - "Filter UsrAccount to show only customers without leads."
		         - target lookup: `UsrAccount` -> reference schema `Account`
		         - "customers" -> leaf on Account's Lookup column `Type` (NOT `Type.Name`): `{ columnPath: "Type", comparisonType: "EQUAL", value: "Customer" }`. clio resolves "Customer" through `AccountType` and emits the full lookup-parameter shape so the rule editor renders a Customer chip.
		         - "without leads" -> backward `[Lead:QualifiedAccount]` with `aggregationType: NOT_EXISTS` (or `COUNT` + `EQUAL` + 0)
		         {
		           "type": "apply-static-filter",
		           "targetAttribute": "UsrAccount",
		           "filter": {
		             "logicalOperation": "AND",
		             "filters": [
		               { "columnPath": "Type", "comparisonType": "EQUAL", "value": "Customer" }
		             ],
		             "backwardReferenceFilters": [
		               {
		                 "referenceColumnPath": "[Lead:QualifiedAccount]",
		                 "filter": { "logicalOperation": "AND" },
		                 "aggregationType": "NOT_EXISTS"
		               }
		             ]
		           }
		         }
		       - "Filter UsrContact to show contacts with more than 10 activities."
		         - target lookup: `UsrContact` -> reference schema `Contact`
		         - "more than 10 activities" -> backward `[Activity:Owner]` with `COUNT > 10`
		         {
		           "type": "apply-static-filter",
		           "targetAttribute": "UsrContact",
		           "filter": {
		             "logicalOperation": "AND",
		             "filters": [],
		             "backwardReferenceFilters": [
		               {
		                 "referenceColumnPath": "[Activity:Owner]",
		                 "filter": { "logicalOperation": "AND" },
		                 "aggregationType": "COUNT",
		                 "comparisonType": "GREATER",
		                 "aggregationValue": 10
		               }
		             ]
		           }
		         }
		       - "Filter UsrAccount to show accounts whose total opportunity amount exceeds 100000."
		         - target lookup: `UsrAccount` -> reference schema `Account`
		         - "total opportunity amount > 100000" -> backward `[Opportunity:Account]` with `SUM(Amount) > 100000`
		         {
		           "type": "apply-static-filter",
		           "targetAttribute": "UsrAccount",
		           "filter": {
		             "logicalOperation": "AND",
		             "filters": [],
		             "backwardReferenceFilters": [
		               {
		                 "referenceColumnPath": "[Opportunity:Account]",
		                 "filter": { "logicalOperation": "AND" },
		                 "aggregationType": "SUM",
		                 "aggregationColumnPath": "Amount",
		                 "comparisonType": "GREATER",
		                 "aggregationValue": 100000
		               }
		             ]
		           }
		         }
		       - "Filter UsrProduct to show products that contain feature 'video memory'."
		         - target lookup: `UsrProduct` -> reference schema `Product`
		         - "contain feature 'video memory'" -> backward `[ProductFeature:Product]` with EXISTS + leaf `Feature.Name CONTAIN "video memory"`
		         {
		           "type": "apply-static-filter",
		           "targetAttribute": "UsrProduct",
		           "filter": {
		             "logicalOperation": "AND",
		             "filters": [],
		             "backwardReferenceFilters": [
		               {
		                 "referenceColumnPath": "[ProductFeature:Product]",
		                 "filter": {
		                   "logicalOperation": "AND",
		                   "filters": [
		                     { "columnPath": "Feature.Name", "comparisonType": "CONTAIN", "value": "video memory" }
		                   ]
		                 }
		               }
		             ]
		           }
		         }
		       - Discover-then-write rule: when uncertain about the child schema's lookup column name or the parent's `Type.Name` enum value, run `get-entity-schema-properties` on the candidate child schema BEFORE finalizing `referenceColumnPath`, and validate the lookup-value display string against the lookup's primary display column.

		       BEFORE SAVE CHECKLIST
		       - Did you decompose the prompt into target lookup + forward leaves + backward references using the "Intent decomposition" rules above, and verify that domain nouns ("Customer", "Lead", etc.) map to the right schema/lookup-value via the "Creatio domain vocabulary" section?
		       - For every `backwardReferenceFilters[*].referenceColumnPath`, did you actually verify the child column name with `get-entity-schema-properties --schema-name <Child>` (looking for `reference schema: <Parent>`), and confirm the user's "without" / "with" / "more than N" maps to the right `aggregationType`?
		       - Is the action type `apply-static-filter` and is `targetAttribute` a real Lookup column on the target entity?
		       - Is the friendly `filter` non-empty and routed through `filters[]` (compare leaves) and / or `backwardReferenceFilters[]` (1:N traversals)?
		       - Are all `columnPath` segments resolvable from the inferred reference schema, with non-leaf segments being Lookup columns?
		       - Do all comparison tokens come from the table above and match the leaf datatype?
		       - Are leaf values shaped correctly for their datatype, with Lookup leaves carrying a GUID for an existing record?
		       - Are `items` absent from this action (never sent alongside `targetAttribute` / `filter`)?
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for the friendly filter contract behind the
	/// <c>apply-static-filter</c> business-rule action.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "business-rule-filters-guidance")]
	[Description("Returns canonical MCP guidance for the friendly-filter contract used by the apply-static-filter business-rule action.")]
	public ResourceContents GetGuide() => Guide;
}
