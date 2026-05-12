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
		       - Use this guide whenever the requirement involves conditional field/element visibility, editability, required state, or auto-assignment based on another field's value.
		       - Business rules are SEPARATE first-class artifacts in Creatio Freedom UI. They are NOT implemented as page JavaScript code, handlers, validators, or converters.
		       - Do NOT write JavaScript handler or validator code to implement business-rule behavior. Use the dedicated MCP tools instead.

		       What is a business rule
		       - A business rule is a declarative condition → action definition that Creatio evaluates at runtime.
		       - It consists of a CONDITION GROUP (AND/OR of field-value comparisons) and one or more ACTIONS that fire when the condition is met.
		       - Business rules are created via dedicated MCP tools, not by editing page schema bodies or writing JavaScript.

		       Two levels of business rules

		       1. Entity-level business rules
		          - Scope: operate on entity schema attributes (columns).
		          - Tool: `create-entity-business-rule`
		          - Supported actions: make-editable, make-read-only, make-required, make-optional, set-values.
		          - Use when the rule should apply everywhere the entity is used, regardless of which page displays it.

		       2. Page-level business rules
		          - Scope: operate on page elements (UI controls from viewConfig) and page attributes (from viewModelConfig).
		          - Tool: `create-page-business-rule`
		          - Supported actions: hide-element, show-element, make-editable, make-read-only, make-required, make-optional.
		          - Use when the rule should apply only on a specific page.
		          - Element names come from `get-page` bundle.viewConfig (recursive). Attribute names come from bundle.viewModelConfig.attributes.

		       Decision tree — when to use business rules vs handlers/validators
		       - If the requirement is "when field X equals Y, then hide/show/enable/disable/require/unrequire field Z" → use a BUSINESS RULE.
		       - If the requirement is "when field X equals Y, set field Z to value W" → use an ENTITY BUSINESS RULE with set-values action.
		       - If the requirement is field-value format validation (length, regex, range) → use a VALIDATOR, not a business rule.
		       - If the requirement is cross-field orchestration with side effects (API calls, process launch, data loading) → use a HANDLER.
		       - If the requirement is display-only value transformation → use a CONVERTER.
		       - When in doubt, prefer business rules over handlers for simple conditional visibility/editability/required logic.

		       Condition group structure
		       - A condition group has a `logicalOperation` (AND or OR) and a list of condition items.
		       - Each condition item specifies: `attributeName` (entity column or page attribute), `comparisonType` (e.g., Equal, NotEqual, IsNotNull, IsNull, Greater, Less, GreaterOrEqual, LessOrEqual, Contain, NotContain, StartWith, NotStartWith, EndWith, NotEndWith), and `value` (the comparison value, can be null for IsNull/IsNotNull).
		       - For lookup columns, use the lookup record Id (Guid) as the comparison value, and set `type` to "Guid". For boolean columns, use `true` or `false` string. For other types, use string representation.

		       Workflow
		       1. Read entity schema columns with `get-entity-schema-column-properties` or page structure with `get-page`.
		       2. Determine whether the rule is entity-level or page-level.
		       3. Build the condition group and actions.
		       4. Call `create-entity-business-rule` or `create-page-business-rule`.
		       5. Verify by checking the entity or page on the environment.

		       Common mistakes to avoid
		       - Do NOT add visibility/editability/required toggling logic in SCHEMA_HANDLERS — use business rules.
		       - Do NOT confuse business rules with business logic. Business rules are declarative condition→action pairs, not imperative code.
		       - Do NOT write `$context.enableAttribute()`/`$context.disableAttribute()` handlers when a business rule suffices.
		       - Do NOT duplicate entity-level rules on every page. If the rule applies to the entity globally, create it at the entity level.
		       """
	};
}
