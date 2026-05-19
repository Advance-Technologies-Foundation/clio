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

		       BEFORE SAVE CHECKLIST
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
