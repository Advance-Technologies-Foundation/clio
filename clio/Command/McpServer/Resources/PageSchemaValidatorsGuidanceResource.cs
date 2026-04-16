using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for editing Freedom UI page validators through clio MCP.
/// </summary>
[McpServerResourceType]
public sealed class PageSchemaValidatorsGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/page-schema-validators";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Returns the canonical guidance article for editing validator sections in Freedom UI page bodies.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "page-schema-validators-guidance")]
	[Description("Returns canonical MCP guidance for creating and editing Freedom UI page validators inside raw page schema bodies.")]
	public ResourceContents GetGuide() =>
		new TextResourceContents {
			Uri = ResourceUri,
			MimeType = "text/plain",
			Text = """
			       clio MCP page-schema validators guide

			       Scope
			       - Use this guide when the task changes the `validators` section of a Freedom UI page body returned by `get-page`.
			       - Resolve exact MCP tool contracts through `get-tool-contract` before any write workflow.
			       - Keep validator work inside clio-owned page-body guidance instead of depending on an external repository.

			       Canonical runtime flow
			       - Prefer `list-pages -> get-page -> sync-pages -> get-page` for deployed page-schema validator edits.
			       - Use `raw.body` as the editable source of truth and preserve the outer AMD module structure.
			       - Preserve `validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/` as an object section.
			       - Treat `SCHEMA_VALIDATORS` as a JavaScript object section, not a strict JSON-only payload. Function-based validator entries are valid.

			       Validator intent
			       - Keep validators focused on field-value validation.
			       - Use dedicated validators for reusable validation logic, especially when the rule belongs to one field value rather than a page lifecycle request.
			       - When the requirement is dynamic `required`, `visible`, or `readonly` state, prefer handlers, business rules, or `setAttributePropertyValue(...)` from `docs://mcp/guides/page-schema-handlers`.
			       - When the requirement is value transformation rather than validation, use `docs://mcp/guides/page-schema-converters`.

			       Minimal page-body example
			       - Custom validator object shape in a deployed page body:
			         validators: /**SCHEMA_VALIDATORS*/{
			           /* Implement a custom validator type. */
			             "usr.ValidateFieldValue": {
			                 /* Business logic of the validator. */
			                 "validator": function (config) {
			                     return function (control) {
			                         return control.value !== config.invalidName ? null: {
			                             "usr.ValidateFieldValue": { message: config.message }
			                         };
			                     };
			                 },
			                 /* Validator parameters. */
			                 "params": [
			                     {
			                         "name": "invalidName"
			                     },
			                     {
			                         "name": "message"
			                     }
			                 ],
			                 "async": false
			             }
			         }/**SCHEMA_VALIDATORS*/
			       - Preserve validator entries as an object section and keep field-oriented rules local to validation.

			       Frontend-source pattern
			       - For frontend-source modules, the public SDK registration pattern is `@CrtValidator({ type: 'usr.MyValidator' })` from `@creatio-devkit/common`.
			       - The public validator helpers include `BaseValidator`, `CrtControlState`, `CrtValidationError`, `CrtValidationErrors`, `CrtValidatorFn`, `ValidatorConfig`, and `ValidatorParametersValues`.
			       - For runtime page-body editing, keep the implementation in `SCHEMA_VALIDATORS` unless the task explicitly targets frontend source modules.

			       Safe editing rules
			       - Edit validators conservatively and preserve all marker pairs.
			       - Replace only the validator marker content, not the whole page body.
			       - Re-parse the edited validator section before save.
			       - Keep validators free of navigation, data loading, save orchestration, or HTTP side effects.

			       SDK and source guidance
			       - For page-body work, reuse the live SDK alias already present in the schema body when imports are required.
			       - For frontend-source work, keep validator imports on the public `@creatio-devkit/common` surface.
			       - Do not switch to internal SDK imports just because a broader package barrel exposes them.

			       Academy and API references
			       - Freedom UI client schema overview: https://academy.creatio.com/docs/8.x/dev/development-on-creatio-platform/front-end-development/freedom-ui/client-schema-freedomui/overview
			       - Validator reference: https://academy.creatio.com/docs/8.x/dev/development-on-creatio-platform/front-end-development/freedom-ui/client-schema-freedomui/references/validators
			       - Field value validation example: https://academy.creatio.com/docs/8.x/dev/development-on-creatio-platform/platform-customization/freedom-ui/customize-page-fields/examples/implement-the-field-value-validation
			       - Use the public `@creatio-devkit/common` surface for `CrtValidator`, `BaseValidator`, `ValidatorConfig`, and validator-related models.

			       Decision guardrails
			       - Use validators only when explicit validation logic is required by the page or by the requirement.
			       - For request-chain logic or dynamic UI state, use handlers instead of validators.
			       - Do not use an external repository as the source of truth for validator authoring when clio MCP is available.
			       """
		};
}