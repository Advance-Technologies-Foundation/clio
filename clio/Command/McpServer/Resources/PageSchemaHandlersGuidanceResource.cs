using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for editing Freedom UI page handlers through clio MCP.
/// </summary>
[McpServerResourceType]
public sealed class PageSchemaHandlersGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/page-schema-handlers";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Returns the canonical guidance article for editing handler sections in Freedom UI page bodies.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "page-schema-handlers-guidance")]
	[Description("Returns canonical MCP guidance for creating and editing Freedom UI page handlers inside raw page schema bodies.")]
	public ResourceContents GetGuide() => Guide;

	internal static readonly TextResourceContents Guide = new() {
			Uri = ResourceUri,
			MimeType = "text/plain",
			Text = """
			       clio MCP page-schema handlers guide

			       Scope
			       - Use this guide when the task changes the `handlers` section of a Freedom UI page body returned by `get-page`.
			       - Resolve exact tool names, request arguments, defaults, validators, aliases, and response shapes through `get-tool-contract`.
			       - Treat this guide as clio-owned workflow and page-body policy, not as an independent MCP API contract.

			       Canonical runtime flow
			       - Prefer `list-pages -> get-page -> sync-pages -> get-page` for handler work on deployed page schemas.
			       - Use `update-page` only as a fallback for single-page dry-run or legacy save workflows.
			       - Use `raw.body` from `get-page` as the editable source of truth.
			       - Preserve the outer AMD `define(...)` wrapper and all marker pairs.

			       Handler section shape
			       - Page bodies store handlers inside `handlers: /**SCHEMA_HANDLERS*/[...]/**SCHEMA_HANDLERS*/`.
			       - Each handler entry contains a request type string and an async `handler(request, next)` function.
			       - Keep handler logic in the page-body marker section when the task targets deployed runtime schema bodies. Do not switch to standalone TypeScript `@CrtRequestHandler` classes unless the task explicitly targets frontend source modules.

			       Minimal page-body example
			       - Use this shape when the task edits a deployed page schema body:
			         handlers: /**SCHEMA_HANDLERS*/[
			         	{
			         		request: "crt.HandleViewModelInitRequest",
			         		handler: async (request, next) => {
			         			return next?.handle(request);
			         		}
			         	}
			         ]/**SCHEMA_HANDLERS*/

			       Frontend-source pattern
			       - For frontend-source modules rather than runtime page-body edits, the public SDK registration pattern is `@CrtRequestHandler({ type, requestType, scopes? })` from `@creatio-devkit/common`.
			       - The frontend-source runtime base class is `BaseRequestHandler`, but page-body work should stay in `SCHEMA_HANDLERS` unless the task explicitly targets source modules.

			       Runtime execution model
			       - A user action or lifecycle event dispatches a request into the Freedom UI request chain.
			       - Use `request.$context.executeRequest(...)` when handler logic must trigger a secondary standard request.
			       - Call `next?.handle(request)` when the handler should continue the chain.
			       - Returning without `next?.handle(request)` stops further processing for that request.
			       - Match request types to business intent. Common examples include `crt.HandleViewModelInitRequest`, `crt.HandleViewModelDestroyRequest`, `crt.HandleViewModelAttributeChangeRequest`, `crt.SaveRecordRequest`, `crt.DeleteRecordRequest`, `crt.OpenPageRequest`, and `crt.RunBusinessProcessRequest`.
			       - Typical intent mapping: page load -> `crt.HandleViewModelInitRequest`, field change -> `crt.HandleViewModelAttributeChangeRequest`, save interception -> `crt.SaveRecordRequest`, navigation -> `crt.OpenPageRequest`.

			       Page-context rules
			       - Read attribute values through `await request.$context.AttributeName`.
			       - Write attribute values through `request.$context.AttributeName = value` for simple cases.
			       - Use `setValue(...)` when the update must suppress attribute-change requests, state changes, or business rules.
			       - Use `setAttributePropertyValue(...)` for dynamic `required`, `visible`, or `readonly` state.
			       - Prefer handlers, business rules, or attribute-property updates for dynamic UI behavior instead of overloading `validators` or `converters` with side effects.
			       - Lookup-like values commonly expose `.value` and `.displayValue` when read from `$context`.

			       Import rules
			       - If handler code needs SDK services, update `SCHEMA_DEPS` and `SCHEMA_ARGS` together with the handler body.
			       - Prefer `@creatio-devkit/common` and reuse the live alias already present in the page body, such as `sdk` or `devkit`.
			       - Extend existing imports conservatively instead of rewriting the module header.
			       - High-value `sdk.*` services for page-body handlers include `HttpClientService`, `Model`, `ProcessEngineService`, `SysValuesService`, `SysSettingsService`, `RightsService`, and `DialogService`.

			       Academy and API references
			       - Freedom UI client schema overview: https://academy.creatio.com/docs/8.x/dev/development-on-creatio-platform/front-end-development/freedom-ui/client-schema-freedomui/overview
			       - Use the public `@creatio-devkit/common` surface for decorators, services, models, and handler infrastructure.
			       - For page-body handler work, prefer the runtime page schema pattern in this guide over standalone frontend-source examples.

			       Safe editing rules
			       - Modify only the marker content that belongs to the handler section.
			       - Re-parse the edited handler section before save.
			       - Keep handler logic focused on lifecycle, request-chain, navigation, or controlled runtime state changes.
			       - Do not use a handler for pure display transformation tasks such as "add a label that shows Name in uppercase"; use a converter and UI binding instead.
			       - If the task is really value transformation, call `get-guidance` with `name` set to `page-schema-converters`.
			       - If the task is really field-value validation, call `get-guidance` with `name` set to `page-schema-validators`.
			       - Do not use an external repository as the source of truth for handler authoring when clio MCP is available.
			       """
		};
}

