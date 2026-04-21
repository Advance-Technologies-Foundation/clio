using System.ComponentModel;
using System.IO.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for entity-level Freedom UI business rules through clio MCP.
/// </summary>
[McpServerResourceType]
public sealed class ObjectBusinessRulesGuidanceResource : BaseResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/object-business-rules";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	public ObjectBusinessRulesGuidanceResource() : this(new FileSystem()) {
	}

	public ObjectBusinessRulesGuidanceResource(IFileSystem fileSystem) : base(fileSystem) {
	}

	protected override string Description { get; init; } =
		"Returns canonical MCP guidance for entity-level Freedom UI business-rule authoring.";

	protected override string ResourceName { get; init; } = "object-business-rules-guidance";

	/// <summary>
	/// Returns the canonical guidance article for object-scoped business-rule authoring.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "object-business-rules-guidance")]
	[Description("Returns canonical MCP guidance for entity-level Freedom UI business-rule authoring.")]
	public ResourceContents GetGuide() =>
		new TextResourceContents {
			Uri = ResourceUri,
			MimeType = "text/plain",
			Text = """
			       clio MCP object business rules guide

			       Scope and lifecycle
			       - This guide covers entity-level Freedom UI business rules stored as BusinessRule add-on metadata on an entity schema.
			       - In product wording this is the object-scoped rule family that applies across Freedom UI pages and editable lists using the object.
			       - Current clio MCP supports create-only execution through `create-entity-business-rule`.
			       - Do not promise edit, remove, or dedicated verify flows until clio exposes separate MCP tools for them.
			       - Do not mutate raw add-on metadata outside MCP to emulate unsupported lifecycle operations.

			       Contract discovery first
			       - Read `get-tool-contract` before the first business-rule call in a session.
			       - Use discovered tool names exactly as advertised.
			       - For `create-entity-business-rule`, use the current top-level camelCase fields: `environmentName`, `packageName`, `entitySchemaName`, and `rule`.
			       - Do not wrap those fields inside a synthetic `args` object.
			       - Do not send `rule.name`; clio generates the internal business-rule name automatically.

			       Canonical create workflow
			       - Preferred flow: `get-tool-contract -> get-entity-schema-properties -> create-entity-business-rule`.
			       - Use `get-entity-schema-properties` before authoring the rule so attribute names come from the deployed schema instead of requirement wording.
			       - If the package or entity must be discovered from an installed app first, use the fallback flow `list-apps -> get-app-info -> create-entity-business-rule`.
			       - The create tool returns execution output, not a structured rule read model. Treat the result as create confirmation only.

			       Supported working subset
			       - `rule.condition.logicalOperation` supports `AND` and `OR`.
			       - `rule.condition.conditions[*].leftExpression.type` must be `AttributeValue`.
			       - `rule.condition.conditions[*].leftExpression.path` must resolve to an existing entity attribute.
			       - `rule.condition.conditions[*].comparisonType` currently supports `equal` and `not-equal`.
			       - `rule.condition.conditions[*].rightExpression.type` must be `AttributeValue` or `Const`.
			       - `rule.actions[*].type` currently supports `make-editable`, `make-read-only`, `make-required`, and `make-optional`.
			       - Every item in `rule.actions[*].items` must resolve to an existing entity attribute.
			       - Use attribute-to-constant and attribute-to-attribute comparisons only.
			       - For lookup constants, pass only a raw GUID string in `rightExpression.value`.
			       - Do not send older payload shapes such as `if`, `then`, operand `kind`, `targets`, `operator`, `comparison`, `ConstValue`, or `referenceSchemaName`.

			       DataForge-assisted planning
			       - DataForge is optional assistance for business-rule authoring. It is used for getting information about data in the environment - tables, columns, lookups, and relations - to help translate business requirements into the technical schema language required by the create tool.
			       - DataForge is not required to author rules, but it can make the process more efficient and less error-prone.
			       - If the workflow will need several DataForge reads, optionally preflight once with `dataforge-health` or `dataforge-status`. If DataForge is unavailable, continue with schema inspection and explicit user input rather than blocking the rule workflow.
			       - Use `dataforge-context` when the request is still phrased in business terms and you need semantic candidates before choosing the target schema, lookup, or related concept.
			       - Use `dataforge-find-tables(query)` when the entity name or related lookup schema is described semantically and the technical schema name is still uncertain.
			       - Use `dataforge-get-table-columns(table-name)` after the target table is known but the current runtime column list, data types, or `reference-schema-name` values are still unclear.
			       - Use `dataforge-find-lookups(schema-name, query)` when a rule compares a lookup attribute to a constant and the user provided a business label rather than the required GUID. Resolve the GUID first, then send that raw GUID string in `rightExpression.value`.
			       - If DataForge search returns nothing for a concept that should exist and `dataforge-status` says `Ready`, call `dataforge-update` once and retry the search before treating the empty result as authoritative.
			       
			       Failure policy
			       - Stop and ask for clarification when the request is for page-level business rules.
			       - Stop when the requested action type or comparison is outside the supported subset.
			       - Stop when the target entity or target attributes cannot be resolved confidently from schema inspection.
			       - Stop when the request is really an edit or delete of an existing rule rather than a new rule.
			       - Do not guess lookup GUIDs, entity attributes, or cross-entity paths just to make the create call succeed.

			      
			       Recommended authoring sequence when lookup constants are involved
			       - Find entity calling `dataforge-find-tables`.
			       - Identify the left-side lookup attribute and its reference schema.
			       - If the lookup schema is still uncertain, call `get-entity-schema-properties`.
			       - If the lookup schema is known but the constant GUID is not, call `dataforge-find-lookups`.
			       - Build the rule with `rightExpression.type = Const` and the resolved GUID string value.
			       - Execute `create-entity-business-rule`.

			       Practical reminders
			       - Attribute names come from deployed schema metadata, not from captions in the requirement.
			       - Lookup constants are stricter than page-level UX values: the create tool expects a GUID string, not `{ value, displayValue }`.
			       - If the requirement needs unsupported actions such as visibility, filtering, set-value, formula, or role-based behavior, surface that limitation explicitly instead of improvising.
			       """
		};

	public override ResourceContents GetHelpArticle() => GetGuide();
}
