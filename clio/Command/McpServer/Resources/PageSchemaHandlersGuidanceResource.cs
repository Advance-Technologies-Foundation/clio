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
	/// Canonical guidance article accessible by name through <c>get-guidance</c>.
	/// </summary>
	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP page-schema handlers guide

		       Scope
		       - Use this guide when the task changes the `handlers` section of a Freedom UI page body returned by `get-page`.
		       - Resolve exact MCP tool contracts through `get-tool-contract` before any write workflow.
		       - If the handler body adds or edits `@creatio-devkit/common`, or if the handler reads data, system settings, model rows, processes, or backend services, you MUST read `page-schema-creatio-devkit-common` before touching `SCHEMA_DEPS`, `SCHEMA_ARGS`, SDK services, or raw service calls.
		       - Keep runtime page-body handler work inside clio-owned guidance instead of depending on repository-local notes from another workspace.

		       Canonical runtime flow
		       - Prefer `list-pages -> get-page -> sync-pages -> get-page` for deployed page-schema handler edits.
		       - Use the `raw.body` field from the `get-page` response as the editable source of truth and preserve the outer AMD module structure.
		       - `SCHEMA_HANDLERS` must remain a JavaScript array section, not a JSON-only payload.
		       - Handler entries may contain async functions, closures, `await next?.handle(request)`, and request-specific branching.
		       - Mandatory routing rule: when the handler requirement includes any data access, system setting read/write, process execution, model query, or backend/external service call, stop and read `page-schema-creatio-devkit-common` before choosing between `sdk.HandlerChainService.instance.process(...)`, SDK services, `sdk.Model`, or `fetch`.

		       BUSINESS RULES FIRST — mandatory triage before authoring any handler
		       - Before writing a handler, check whether the task can be closed with a business rule. If it can, CLOSE IT WITH A BUSINESS RULE — do NOT write a handler. A handler is only justified when no business rule covers the requirement.
		       - Business rules are not limited to show/hide/enable/require. They can also WRITE values into columns and CLEAR columns (the `set-values` action; an empty value clears the column). This is the most common case for two interdependent fields — when changing field A should auto-fill or wipe field B, that is a business rule, not an attribute-change handler.
		       - Do NOT mentally narrow business rules to "visibility/editability/required" and fall back to a handler for value population/clearing. Resist reaching for `crt.HandleViewModelAttributeChangeRequest` + `$context.set(...)` when a `set-values` business rule does the same job declaratively.
		       - A business-rule condition is NOT limited to field values: it can also test the current user (CurrentUser / CurrentUserContact / CurrentUserAccount), the user's roles (CurrentUserRoles CONTAIN / NOT_CONTAIN a role), or the current date/time (CurrentDate / CurrentTime / CurrentDateTime). Such a check is a business-rule CONDITION, not handler "data access" — do NOT reach for `crt.HandleViewModelInitRequest` (or `sdk.RightsService` / `sdk.Model`) to gate a field by role, user, or date; close it with a business rule.

		       Decision tree
		       - If the requirement is conditional field/element visibility, editability, or required state based on another field's value (e.g. "when Status is Closed, hide field X" or "when Type is Internal, make Description required"), this is a BUSINESS RULE, not a handler. Use `create-page-business-rules` or `create-entity-business-rules`. Call `get-guidance` with name `business-rules` first.
		       - If the requirement is show/hide (or editable/required) based on whether another field is FILLED or EMPTY ("hidden until a value is entered"), this is a BUSINESS RULE, not a handler. Use `create-page-business-rules` with show-element/hide-element on `is-filled-in` / `is-not-filled-in`. Do NOT toggle a bound `visible` attribute from a handler. Call `get-guidance` with name `business-rules` first.
		       - If the requirement is conditional visibility, editability, or required state based on the current user's ROLE, the current user's identity, or the current DATE/TIME (e.g. "Resolved visible only for administrators", "Assignee group visible only for the Supervisor contact", "show this label only on 2026-06-09"), this is a BUSINESS RULE, not a handler. Put the system variable in the condition: CurrentUserRoles CONTAIN / NOT_CONTAIN a role id; CurrentUser / CurrentUserContact / CurrentUserAccount equal a target id; or a CurrentDate / CurrentDateTime comparison. Use `create-page-business-rules` or `create-entity-business-rules` and call `get-guidance` with name `business-rules` first. Do NOT write a `crt.HandleViewModelInitRequest` handler, and do NOT treat the role/user/date check as "data access".
		       - If the requirement is writing a value into a column or clearing a column when another field changes (e.g. "when Type=Personal, clear Company"; "when Country=USA, set Currency=USD"; two interdependent fields where one drives the other's value), this is a BUSINESS RULE with the `set-values` action, not a handler. Use `create-entity-business-rules` and call `get-guidance` with name `business-rules` first. Do NOT implement this as a `crt.HandleViewModelAttributeChangeRequest` handler.
		       - If the requirement is field-value validation, stop and read `page-schema-validators`.
		       - If the requirement is max/min/length/range/regex validation whose threshold comes from a system setting, SDK lookup, or other async read, it is still validator work. Do NOT default to an init handler that only sets `maxLength` or another UI-only property.
		       - Else if the requirement is a pure value transform for bound data, use `SCHEMA_CONVERTERS`, not `SCHEMA_HANDLERS`.
		       - Else if the requirement is a one-step built-in page action, prefer direct request wiring from the page config instead of a custom handler.
		       - Else if the handler must read or write data, syssettings, processes, or backend services, stop and read `page-schema-creatio-devkit-common` before authoring the handler body.
		       - Else use `SCHEMA_HANDLERS` for lifecycle events, attribute changes, cross-field orchestration, editor events, request guards, or domain-specific workflows.

		       Direct request decision table
		       - Prefer built-in request wiring for simple one-step actions. Do NOT create a custom handler when a direct request already matches the requirement.
		       - This table covers direct triggers from page config. You may still intercept the same built-in request in `SCHEMA_HANDLERS` when custom logic must run before or after the base behavior.
		         | Requirement pattern | Prefer | Section or API | Custom handler needed |
		         | --- | --- | --- | --- |
		         | open a related page or mini page | `crt.OpenPageRequest` | button/menu `clicked.request` | no |
		         | create a related record from current context | `crt.CreateRecordRequest` | button/menu `clicked.request` | no |
		         | save the current task or mini page from a button/menu trigger | `crt.SaveRecordRequest` | button/menu `clicked.request` | no |
		         | cancel unsaved edits on the current page | `crt.CancelRecordChangesRequest` | button/menu `clicked.request` | no |
		         | delete the current or selected record | `crt.DeleteRecordRequest` | button/menu `clicked.request` | no |
		         | launch a business process | `crt.RunBusinessProcessRequest` | button/menu `clicked.request` | no |
		         | compose an email from current context | `crt.CreateEmailRequest` | button/menu `clicked.request` | no |
		         | copy a prepared literal value to clipboard | `crt.CopyClipboardRequest` | button/menu `clicked.request` | no |
		         | copy a page attribute value to clipboard | `crt.CopyInputToClipboardRequest` | button/menu `clicked.request` | no |
		         | print the current or selected record(s) | `crt.PrintablesRequest` | button/menu `clicked.request` | no |
		         | open the printables management page | `crt.GoToPrintablesRequest` | button/menu `clicked.request` | no |
		         | export list data to Excel | `crt.ExportDataGridToExcelRequest` | button/menu `clicked.request` | no |
		         | import data for an entity | `crt.ImportDataRequest` | button/menu `clicked.request` | no |
		         | delete multiple selected records from a list | `crt.DeleteRecordsRequest` | button/menu `clicked.request` | no |
		         | duplicate a record | `crt.CopyRecordRequest` | button/menu `clicked.request` | no |
		         | page init, destroy, attribute-change orchestration, editor interaction, or domain-specific workflow | handler in `SCHEMA_HANDLERS` | handlers runtime | yes |

		       crt.CreateRecordRequest page-resolution note
		       - A button/menu wired to `crt.CreateRecordRequest` opens the target entity's navigation/edit page,
		         which the runtime resolves from the entity's REGISTERED page. For a standalone detail/child
		         entity with NO section (no registered edit page) the click throws this exact toast:
		         "There is no page for new or existing record. System administrator must check the button settings in the Freedom UI." (even
		         though `update-page` reported `success: true` — the break surfaces only on click in the browser).
		         Pass an explicit `entityPageName` in `params` naming an existing FormPage schema, or register a
		         section page (`create-app-section`). To simply ADD rows to a related list, prefer INLINE grid add
		         (`features.editable.itemsCreation`) — no page needed; see get-guidance `related-list`.

		       Request shape quick reference
		       - Do NOT mix the declarative page-config shape with the imperative runtime-dispatch shape.
		         | Use case | Shape | Canonical example |
		         | --- | --- | --- |
		         | declarative page config | `request` + `params` | `"clicked": { "request": "usr.AlertRequest", "params": { "message": "$AlertMessage" } }` |
		         | imperative dispatch from handler code | `type` + flat payload fields + `$context` + usually `scopes` | `await sdk.HandlerChainService.instance.process({ type: "usr.AlertRequest", message: "Handler Chain works!", $context: request.$context, scopes: [...request.scopes] });` |
		       - API choice rules:
		         | Context | Prefer | Why |
		         | --- | --- | --- |
		         | deployed page-body handler in `SCHEMA_HANDLERS` | `await sdk.HandlerChainService.instance.process({ type, $context, scopes })` | this is the documented `@creatio-devkit/common` dispatcher used uniformly by Creatio Academy SCHEMA_HANDLERS examples; `request.$context.executeRequest(...)` is reachable from handler code but not part of the public SDK surface |
		       - Do NOT default to `request.$context.executeRequest(...)` in deployed page-body handlers; use `sdk.HandlerChainService.instance.process({ type, $context, scopes })`. `executeRequest` is an internal handler-chain shortcut that may shift between minor versions; `HandlerChainService.instance.process(...)` is exported from `@creatio-devkit/common` and is the supported contract. Add `"@creatio-devkit/common"` to `SCHEMA_DEPS` and bind the import as `sdk` in `SCHEMA_ARGS`.

		       Server-side compilation
		       - Handlers live in the Freedom UI page body, which is an AMD module served at runtime. After `update-page` or `sync-pages` your changes are live.
		       - Do NOT call `compile-creatio` — it is for C# schema changes, not page-body JavaScript. Compilation forces a runtime reload, breaks the active session, and is never required as a follow-up to a page-body edit.

		       NON-NEGOTIABLES
		       - Use handler signatures like `async (request, next) => { ... }` only inside `SCHEMA_HANDLERS`.
		       - `handlers` is an ARRAY, not an object.
		       - Call `next` intentionally: place it `before`, `after`, or omit it only for an intentional chain break or full behavior replacement.
		       - Prefer built-in `crt.*Request` names before inventing custom `usr.*Request` names.
		       - Keep field validation in validators and simple value transforms in converters, not in handlers.
		       - Read and write page state through `request.$context`.
		       - Keep handler edits minimal and coupled: usually `SCHEMA_HANDLERS`, the triggering `viewConfigDiff` action, and `SCHEMA_DEPS` / `SCHEMA_ARGS` only when imports are actually required.

		       Rules enforced by the AST lint pass (run automatically on every `update-page` / `sync-pages` web body)
		       - Note: anti-shape detection that overlaps the existing regex content validators is handled by the regex layer with its own established wording, NOT by the lint pass. Handler-side examples already covered by the regex layer (and therefore NOT in the lint catalogue): `handlers` written as an object literal, and the forbidden handler-API patterns `request.viewModel` / `request.sender` / `request.$context.get(...)` / `request.$get(...)` / `request.$set(...)` (all five are rejected with their own Errors by `SchemaHandlerValidationService.ForbiddenHandlerApiRules`).
		       - WARNING (reported alongside the response, write still proceeds): `request.$context.executeRequest(...)` is flagged with `handler-uses-context-execute-request`. Use `await sdk.HandlerChainService.instance.process({ type, $context, scopes })` instead — `executeRequest` is reachable from handler code but is NOT part of the documented `@creatio-devkit/common` public surface, while `HandlerChainService.instance.process(...)` is the canonical SCHEMA_HANDLERS dispatcher per Creatio Academy.
		       - Fix the underlying body to clear any finding; do not strip the offending block to silence the diagnostic.

		       Page-body runtime shape
		       - The deployed page-body handler shape is an array of objects like:
		         handlers: /**SCHEMA_HANDLERS*/[
		           {
		             request: "crt.HandleViewModelInitRequest",
		             handler: async (request, next) => {
		               await next?.handle(request);
		               const { $context } = request;
		               await $context.set("UsrReady", true);
		             }
		           }
		         ]/**SCHEMA_HANDLERS*/
		       - Use `request` for the current request payload and `next` for the downstream handler chain.
		       - Do not guess the `next` placement. Choose it from the chain-control rules below.

		       Chain-control rules
		       - `next` placement is part of the behavior contract, not a formatting detail.
		         | Intent | `next` placement | Why |
		         | --- | --- | --- |
		         | extend a built-in lifecycle flow and depend on the platform's default work (`init`, `resume`, `destroy`) | call `await next?.handle(request)` first | preserve base loading/binding behavior before custom follow-up logic |
		         | inspect or validate a request before the downstream chain decides what to do | run custom logic first, then call `await next?.handle(request)` only when the request may continue | short-circuit invalid or unsupported cases early |
		         | add a side effect only after the base behavior succeeds | call `await next?.handle(request)` first, then run the side effect | avoid firing post-success logic before the platform finishes |
		         | fully replace or intentionally stop the built-in behavior | do NOT call `next` | break the chain only on purpose |
		       - `crt.HandleViewModelInitRequest` is the main example where omitting `next` can break built-in page loading.
		       - When the handler is pure pass-through for non-matching branches, prefer `return next?.handle(request);` so the downstream result is preserved explicitly.

		       Context read/write patterns
		       - If the task reached handler logic from validator reasoning about dynamic `required`, `visible`, or `readonly` state, stop and use the canonical handler API in this section before writing code.
		       - Use `request.value` / `request.oldValue` for the field that triggered `crt.HandleViewModelAttributeChangeRequest`.
		       - Use `await request.$context["<AttributeName>"]` to read any other page attribute in deployed page-body handlers.
		       - Use `await request.$context.set("<AttributeName>", <value>)` to write back into the live ViewModel.
		       - Direct property assignment on `request.$context` is allowed only for transient runtime references such as subscriptions or service handles; page attributes still use `await request.$context.set(...)`.
		       - Prefer `request.value` over re-reading the triggering attribute through the view-model context.
		       - Do NOT use `request.viewModel`, `request.sender`, `.$get(...)`, `.$set(...)`, or `request.$context.get(...)` in deployed page-body handlers.
		       - Canonical rule for `crt.HandleViewModelAttributeChangeRequest`: use `request.value` for the triggering attribute, not `request.viewModel.get(...)`.
		       - A field control MUST bind to the same declared view-model attribute that handlers read or write through `$context.set("<AttributeName>", ...)`.
		       - Default page bodies already bind field controls to attributes declared in `viewModelConfig` / `viewModelConfigDiff`. Keep using that declared attribute unless you intentionally create a different one.
		       - If you create a new attribute for handler-driven logic on the same field, rebind the control to that new attribute. Do NOT leave the control on the old attribute while handlers update the new one.
		       - Do NOT infer the correct binding from naming patterns such as `$PDS_*`. Data-source-backed attributes may use any declared attribute name.
		       - Rule: when handlers write attribute `UsrName` through `$context.set("UsrName", ...)`, the matching `viewConfigDiff` control MUST use `"control": "$UsrName"`.
		       - Correct: handler writes `UsrName` and the control uses `"control": "$UsrName"`.
		       - Wrong: handler writes `UsrNameForHandler`, but the control still uses `"control": "$UsrName"`.
		       - If the current schema already has a local `insert` or `merge` for that control, edit that existing local operation directly.
		       - If the control is inherited from a parent schema and is absent from the current schema's `viewConfigDiff`, add one local `merge` operation for that inherited control name.
		       - Do NOT add a second local patch merge when the current schema already has a local operation for the same control.
		       - Inherited-control example:
		         {
		           "operation": "merge",
		           "name": "RoleDescription",
		           "values": {
		             "control": "$RoleDescription"
		           }
		         }
		       - Minimal read pattern:
		         const currentStatus = await request.$context["UsrStatus"];
		         if (currentStatus === "Active") {
		           await request.$context.set("UsrCanProceed", true);
		         }

		       Minimal canonical templates
		       - Lifecycle init handler:
		         handlers: /**SCHEMA_HANDLERS*/[
		           {
		             request: "crt.HandleViewModelInitRequest",
		             handler: async (request, next) => {
		               await next?.handle(request);
		               const { $context } = request;
		               await $context.set("<AttributeName>", <value>);
		             }
		           }
		         ]/**SCHEMA_HANDLERS*/

		       - Attribute-change orchestration handler:
		         handlers: /**SCHEMA_HANDLERS*/[
		           {
		             request: "crt.HandleViewModelAttributeChangeRequest",
		             handler: async (request, next) => {
		               const { $context, attributeName, value } = request;
		               if (attributeName !== "<AttributeName>") {
		                 return next?.handle(request);
		               }

		               const currentMode = await $context["<ModeAttribute>"];
		               if (currentMode === "<BlockedMode>") {
		                 request.preventAttributeChangeRequest = true;
		                 request.preventRunBusinessRules = true;
		                 return; // intentional chain break: block downstream handlers and business rules
		               }

		               const result = await next?.handle(request);
		               await $context.set("<DependentAttribute>", value);
		               return result;
		             }
		           }
		         ]/**SCHEMA_HANDLERS*/

		       - CAVEAT for the two value-sync templates below: a plain "copy/clear field B based on field A's value" requirement is a BUSINESS RULE (`set-values` action), not a handler — close it with `create-entity-business-rules`. Use these handler templates only when the sync needs logic a business rule cannot express (e.g. transforming the value before writing, multi-source computation, or conditional branching beyond a simple condition→value mapping).
		       - Mirror one text field into another on attribute change:
		         handlers: /**SCHEMA_HANDLERS*/[
		           {
		             request: "crt.HandleViewModelAttributeChangeRequest",
		             handler: async (request, next) => {
		               if (request.attributeName !== "UsrSourceTextField") {
		                 return next?.handle(request);
		               }

		               const result = await next?.handle(request);
		               await request.$context.set("UsrCopyTextField", request.value);
		               return result;
		             }
		           }
		         ]/**SCHEMA_HANDLERS*/

		       - Sync one field into another only when the target value actually differs:
		         handlers: /**SCHEMA_HANDLERS*/[
		           {
		             request: "crt.HandleViewModelAttributeChangeRequest",
		             handler: async (request, next) => {
		               if (request.attributeName !== "<SourceAttribute>") {
		                 return next?.handle(request);
		               }

		               const value = request.value;
		               const targetValue = await request.$context["<TargetAttribute>"];
		               const result = await next?.handle(request);
		               if (value !== undefined && targetValue !== value) {
		                 await request.$context.set("<TargetAttribute>", value);
		               }

		               return result;
		             }
		           }
		         ]/**SCHEMA_HANDLERS*/

		       - NOTE: if the goal is to RUN A BUSINESS PROCESS when the record is saved, do NOT use this save
		         handler. A record-triggered process is started by a Signal start element INSIDE the process
		         (build it with `create-business-process`, start element type `signalStart`, `signal.entity` =
		         the page object, `signal.on` = added|modified|deleted) — not by a page handler. Use the
		         save-handler pattern below only for in-page logic (setting attributes, reloading data, etc.).
		       - Save handler that runs custom logic after the base save succeeds:
		         handlers: /**SCHEMA_HANDLERS*/[
		           {
		             request: "crt.SaveRecordRequest",
		             handler: async (request, next) => {
		               const saveResult = await next?.handle(request);
		               await sdk.HandlerChainService.instance.process({
		                 type: "usr.AfterSaveRequest",
		                 $context: request.$context,
		                 scopes: [...request.scopes]
		               });
		               return saveResult;
		             }
		           }
		         ]/**SCHEMA_HANDLERS*/

		       - Custom button action that orchestrates built-in requests.
		         Use this pattern only when the button starts a multi-step domain workflow that is not a single built-in `crt.*Request`.
		         handlers: /**SCHEMA_HANDLERS*/[
		           {
		             request: "usr.RunCustomActionRequest",
		             handler: async (request, next) => {
		               await sdk.HandlerChainService.instance.process({
		                 type: "crt.RunBusinessProcessRequest",
		                 processName: "<ProcessName>",
		                 $context: request.$context,
		                 scopes: [...request.scopes]
		               });
		               const result = await next?.handle(request);
		               return result;
		             }
		           }
		         ]/**SCHEMA_HANDLERS*/

		       - Subscription lifecycle across init/resume/pause/destroy.
		         This template requires `@creatio-devkit/common` and a live `sdk` alias from `SCHEMA_DEPS` / `SCHEMA_ARGS`; do NOT invent placeholder subscription services.
		         handlers: /**SCHEMA_HANDLERS*/[
		           {
		             request: "crt.HandleViewModelInitRequest",
		             handler: async (request, next) => {
		               await next?.handle(request);
		               const messageChannelService = new sdk.MessageChannelService();
		               request.$context.subscription = await messageChannelService.subscribe("<Channel>", async event => { // transient runtime handle, not a page attribute
		                 await request.$context.set("<TargetAttribute>", event.body);
		               });
		             }
		           },
		           {
		             request: "crt.HandleViewModelResumeRequest",
		             handler: async (request, next) => {
		               await next?.handle(request);
		               if (!request.$context.subscription) {
		                 const messageChannelService = new sdk.MessageChannelService();
		                 request.$context.subscription = await messageChannelService.subscribe("<Channel>", async event => { // transient runtime handle, not a page attribute
		                   await request.$context.set("<TargetAttribute>", event.body);
		                 });
		               }
		             }
		           },
		           {
		             request: "crt.HandleViewModelPauseRequest",
		             handler: async (request, next) => {
		               request.$context.subscription?.unsubscribe();
		               request.$context.subscription = null; // clear transient runtime handle
		               return next?.handle(request);
		             }
		           },
		           {
		             request: "crt.HandleViewModelDestroyRequest",
		             handler: async (request, next) => {
		               request.$context.subscription?.unsubscribe();
		               request.$context.subscription = null; // clear transient runtime handle
		               return next?.handle(request);
		             }
		           }
		         ]/**SCHEMA_HANDLERS*/

		       - Multiple handlers in one page-body array:
		         handlers: /**SCHEMA_HANDLERS*/[
		           {
		             request: "crt.HandleViewModelInitRequest",
		             handler: async (request, next) => {
		               await next?.handle(request);
		               await request.$context.set("UsrReady", true);
		             }
		           },
		           {
		             request: "crt.HandleViewModelAttributeChangeRequest",
		             handler: async (request, next) => {
		               if (request.attributeName !== "UsrStatus") {
		                 return next?.handle(request);
		               }
		               const result = await next?.handle(request);
		               await request.$context.set("UsrStatusChanged", true);
		               return result;
		             }
		           },
		           {
		             request: "usr.RunCustomActionRequest",
		             handler: async (request, next) => {
		               await sdk.HandlerChainService.instance.process({
		                 type: "crt.RunBusinessProcessRequest",
		                 processName: "<ProcessName>",
		                 $context: request.$context,
		                 scopes: [...request.scopes]
		               });
		               const result = await next?.handle(request);
		               return result;
		             }
		           }
		         ]/**SCHEMA_HANDLERS*/
		       - Keep each array item focused on one request contract. Add more entries instead of merging unrelated workflows into one handler body.
		       - Prefer a stable array order: lifecycle handlers first, attribute-change handlers next, and custom domain/action handlers after them.
		       - Compatibility note: existing product code may also use `request.$context.attributes[...]` or direct property assignment. Keep newly generated page-body handlers on the canonical pattern above unless the task explicitly requires matching local legacy style.

		       Orchestration patterns
		       - Use `await sdk.HandlerChainService.instance.process({ type, $context, scopes })` when a deployed page-body handler forwards into another page-scoped request. This is the canonical SCHEMA_HANDLERS dispatcher per Creatio Academy.
		       - Use SDK/domain services such as `sdk.ProcessEngineService` when the task is direct service orchestration rather than request forwarding.

		       Request selection hints
		       - Use `crt.HandleViewModelInitRequest` for initial state preparation.
		       - Use `crt.HandleViewModelResumeRequest` when the page must restore runtime subscriptions or reinitialize transient state after returning to the page.
		       - Use `crt.HandleViewModelPauseRequest` when the page must stop temporary runtime work before the page is resumed or destroyed.
		       - Use `crt.HandleViewModelDestroyRequest` for cleanup when the page closes.
		       - Use `crt.HandleViewModelAttributeChangeRequest` when one field should update dependent page state.
		       - Use `crt.LoadDataRequest` when the handler must prepare or refresh backing collections/data.
		       - Use `crt.CancelRecordChangesRequest` when the page must discard unsaved edits and return to the clean state.
		       - Use `crt.PrintablesRequest` when a button should generate a printable document for the current record or selected records from a data source.
		       - Use `crt.GoToPrintablesRequest` when a button should navigate to the printables management page.
		       - Use `crt.ExportDataGridToExcelRequest` when a button should export list data to an Excel file.
		       - Use `crt.ImportDataRequest` when a button should open the import wizard for a given entity.
		       - Use `crt.DeleteRecordsRequest` for bulk deletion of multiple selected records from a list (with optional filter or recordIds).
		       - Use `crt.CopyRecordRequest` to duplicate an existing record.
		       - When dispatching another request imperatively from a handler, pass both `$context: request.$context` and `scopes: [...request.scopes]` unless the target request intentionally changes scope.
		       - Use a custom `usr.*Request` only when no built-in request type matches the domain workflow.

		       Standard built-in handler catalog
		       - Built-in handlers and related user-available request names include:
		         - `crt.CreateRecordRequest`
		         - `crt.UpdateRecordRequest`
		         - `crt.OpenPageRequest`
		         - `crt.LoadDataRequest`
		         - `crt.SaveRecordRequest`
		         - `crt.DeleteRecordRequest`
		         - `crt.CancelRecordChangesRequest`
		         - `crt.RunBusinessProcessRequest`
		         - `crt.CreateEmailRequest`
		         - `crt.CopyClipboardRequest`
		         - `crt.CopyInputToClipboardRequest`
		         - `crt.ClosePageRequest`
		         - `crt.HandleViewModelInitRequest`
		         - `crt.HandleViewModelResumeRequest`
		         - `crt.HandleViewModelAttributeChangeRequest`
		         - `crt.HandleViewModelPauseRequest`
		         - `crt.HandleViewModelDestroyRequest`
		         - `crt.HandleSidebarOpenRequest`
		         - `crt.HandleSidebarCloseRequest`
		         - `crt.SidebarInitRequest`
		         - `crt.ShowSidebarNotificationMarkRequest`
		         - `crt.HideSidebarNotificationMarkRequest`
		         - `crt.OpenSidebarRequest`
		         - `crt.CloseSidebarRequest`
		         - `crt.GetSidebarStateRequest`
		         - `crt.PrintablesRequest`
		         - `crt.GoToPrintablesRequest`
		         - `crt.ExportDataGridToExcelRequest`
		         - `crt.ImportDataRequest`
		         - `crt.DeleteRecordsRequest`
		         - `crt.CopyRecordRequest`
		         - `crt.ShowDialogRequest`
		       - Treat this catalog as the first place to look before inventing custom `usr.*Request` names.
		       - Prefer the exact built-in request name from this catalog when the requirement matches it directly.

		       Standard handler parameter catalog
		       - AUTHORITATIVE contracts live in the request catalog: call `get-request-info <type>` first —
		         when a request is cataloged there (e.g. crt.PrintablesRequest, crt.RunBusinessProcessRequest,
		         crt.ClosePageRequest, crt.CancelRecordChangesRequest), its `parameters` (required flags, valid
		         values, valueSource probe annotations) and `documentation` override the rows below. This table
		         stays as the fallback index for requests not yet cataloged. See `when-to-use-requests` for the
		         selection and probe discipline.
		       - Read this catalog as the MCP-safe payload contract extracted from `creatio-ui` source.
		       - `config` means fields you author in direct request wiring or `sdk.HandlerChainService.instance.process(...)`.
		       - `runtime` means fields the platform injects before your handler receives the request.
		       - `none` means there are no meaningful custom fields beyond base request/context.
		       - Core page/action requests:
		         | Request | Kind | Params for AI authoring | Notes |
		         | --- | --- | --- | --- |
		         | `crt.CreateRecordRequest` | config | `entityName?`, `defaultValues?`, `itemsAttributeName?`, `entityPageName?`, `skipUnsavedData?` | create page/record flow |
		         | `crt.UpdateRecordRequest` | config | `entityName?`, `recordId?`, `itemsAttributeName?`, `replaceHistoryState?` | update existing record |
		         | `crt.OpenPageRequest` | config | `schemaName` required, `packageUId?`, `modelInitConfigs?`, `parameters?`, `skipUnsavedData?` | standard open-page request |
		         | `crt.LoadDataRequest` | config | `dataSourceName`, `config` (commonly `loadType`, `useLastLoadParameters?`), `showSuccessMessage?` | reload or refresh a page/list data source |
		         | `crt.SaveRecordRequest` | config | `preventCardClose?`, `preventCardStateChange?`, `showSuccessMessage?`, `messageTextAfterCompletion?`, `reloadSavedRecord?`, `showErrorMessage?` | save current page/task |
		         | `crt.DeleteRecordRequest` | config | `recordId`, `itemsAttributeName` | delete one record; source handler converts it into `crt.DeleteRecordsRequest` |
		         | `crt.CancelRecordChangesRequest` | config | `none` | cancel edits |
		         | `crt.RunBusinessProcessRequest` | config | `processName` + `processRunType` required — FULL parameter contract lives in the request catalog: get-request-info `crt.RunBusinessProcessRequest` (single source of truth) | Keys in `processParameters` / `parameterMappings` / `recordIdProcessParameterName` are process parameter CODES, NOT captions — a wrong code is silently dropped. Resolve with `get-process-signature` and get-request-info `crt.RunBusinessProcessRequest` before authoring this button |
		         | `crt.CreateEmailRequest` | config | `recordId?`, `bindingColumns?` | compose an email from current context |
		         | `crt.CopyClipboardRequest` | config | `value` required | copy a prepared literal value |
		         | `crt.CopyInputToClipboardRequest` | config | `attribute` required, `successMessageArea?` | copy the value of a page attribute |
		         | `crt.ClosePageRequest` | config | `none` | close current page |
		         | `crt.PrintablesRequest` | config | `dataSourceName` required, `templateId?`, `printableCaption?`, `convertInPDF?`, `filters?` | generate printable document for current or selected record(s) |
		         | `crt.GoToPrintablesRequest` | config | `none` | open printables management page in a new tab |
		         | `crt.ExportDataGridToExcelRequest` | config | `viewName` required, `filters?` | export list data to Excel |
		         | `crt.ImportDataRequest` | config | `entitySchemaName` required | open import wizard for an entity |
		         | `crt.DeleteRecordsRequest` | config | `dataSourceName` required, `filters?`, `recordIds?`, `skipConfirmation?` | delete multiple records; prefer over `crt.DeleteRecordRequest` for list-based bulk delete |
		         | `crt.CopyRecordRequest` | config | `recordId` required, `itemsAttributeName?`, `entityName?` | duplicate a record |
		       - Lifecycle and attribute-change requests:
		         | Request | Kind | Params visible in handler | Notes |
		         | --- | --- | --- | --- |
		         | `crt.HandleViewModelInitRequest` | runtime | `none` | use `request.$context` |
		         | `crt.HandleViewModelResumeRequest` | runtime | `none` | use `request.$context` |
		         | `crt.HandleViewModelAttributeChangeRequest` | runtime | `attributeName`, `value`, `oldValue`, `silent` (deprecated), `preventAttributeChangeRequest`, `preventStateChange`, `preventRunBusinessRules` | author handlers against these runtime fields |
		         | `crt.HandleViewModelPauseRequest` | runtime | `none` | use `request.$context` |
		         | `crt.HandleViewModelDestroyRequest` | runtime | `none` | use `request.$context`; keep cleanup synchronous when possible |
		       - Sidebar requests:
		         | Request | Kind | Params | Notes |
		         | --- | --- | --- | --- |
		         | `crt.HandleSidebarOpenRequest` | runtime | `sidebarCode` | event payload when sidebar opens |
		         | `crt.HandleSidebarCloseRequest` | runtime | `sidebarCode` | event payload when sidebar closes |
		         | `crt.SidebarInitRequest` | runtime | `none` | sidebar init lifecycle |
		         | `crt.ShowSidebarNotificationMarkRequest` | config | `sidebarCode` | direct request |
		         | `crt.HideSidebarNotificationMarkRequest` | config | `sidebarCode` | direct request |
		         | `crt.OpenSidebarRequest` | config | `sidebarCode` | direct request |
		         | `crt.CloseSidebarRequest` | config | `containerName?` | direct request |
		         | `crt.GetSidebarStateRequest` | config | `sidebarCode` | direct request |
		       - Dialog and special cases:
		       - To show ANY user-facing message from a handler - a short confirmation, info, success, or error popup such as "Approved." or "Saved." - dispatch `crt.ShowDialogRequest` (shape below). "just show a short confirmation message" is a `crt.ShowDialogRequest`, NOT a browser dialog. This needs `@creatio-devkit/common` in `SCHEMA_DEPS` and the `sdk` alias in `SCHEMA_ARGS`. NEVER use `alert(...)`, `window.alert(...)`, `confirm(...)`, or `prompt(...)`: they are raw browser primitives, not the Freedom UI dialog, and are not acceptable in deployed page-body handlers.
		         | User-visible name | Source reality | Params | Notes |
		         | --- | --- | --- | --- |
		         | `crt.ShowDialog` | source request is `crt.ShowDialogRequest`, handled by `crt.ShowDialogHandler` | `dialogConfig.data` with `message`, `actions`, optional `title` | in code author `type: "crt.ShowDialogRequest"`; `crt.ShowDialog` is the user-visible catalog label |
		       - Minimal `dialogConfig` shape - `message`, `actions`, and `title` go under `dialogConfig.data`, NOT directly on `dialogConfig` (it is a `MessageDialogConfig`; the platform renders `dialogConfig.data.message` / `dialogConfig.data.actions`). Placing them on `dialogConfig` directly opens an empty dialog with only the default OK button:
		         await sdk.HandlerChainService.instance.process({
		           type: "crt.ShowDialogRequest",
		           dialogConfig: {
		             data: {
		               title: "<OptionalTitle>",
		               message: "<MessageText>",
		               actions: [
		                 {
		                   key: "ok",
		                   config: {
		                     color: "primary",
		                     caption: "OK"
		                   }
		                 }
		               ]
		             }
		           },
		           $context: request.$context,
		           scopes: [...request.scopes]
		         });
		       Safe editing rules
		       - This guide is only for deployed page-body handlers inside the schema body returned by `get-page`.
		       - Prefer direct request wiring for simple open/create/save/delete/process/output actions.
		       - When the handler body needs `@creatio-devkit/common`, or when the task touches data access, system settings, process execution, or backend service calls, read `page-schema-creatio-devkit-common` first and then follow its AMD dependency, public-API, and fallback rules.
		       - Add `@creatio-devkit/common` to `SCHEMA_DEPS` and the matching alias to `SCHEMA_ARGS` only when the handler body actually needs SDK services or request execution helpers.
		       - Preserve the exact `/**SCHEMA_HANDLERS*/` comment markers around the handlers array; clio uses them to locate and validate the editable section.
		       - For page-body work, reuse the live SDK alias already present in the schema body when imports are required.
		       - Verify the edited body is syntactically valid JavaScript before calling `sync-pages`.
		       - Do not rewrite unrelated handlers or reorder the whole array unless the task specifically requires that refactor.

		       Anti-patterns
		       - Do NOT write `handlers: { ... }`; `handlers` must remain an array.
		       - Do NOT invent `usr.*Request` when an existing `crt.*Request` already matches the workflow.
		       - Do NOT choose raw `fetch(...)` to a platform endpoint before checking `page-schema-creatio-devkit-common` for a canonical `crt.*Request`, SDK service, or `sdk.Model` pattern.
		       - Do NOT invent placeholder SDK services such as `<Service>.subscribe(...)`; when SDK-based subscriptions are required, use a concrete service such as `sdk.MessageChannelService` and keep `SCHEMA_DEPS` / `SCHEMA_ARGS` aligned.
		       - Do NOT write `type: "crt.ShowDialog"` in imperative request code; use `type: "crt.ShowDialogRequest"`.
		       - Do NOT use `alert(...)`, `window.alert(...)`, `confirm(...)`, or `prompt(...)` to show a message from a handler; dispatch `crt.ShowDialogRequest` instead (message/actions under `dialogConfig.data`).
		       - Do NOT use `request.viewModel`, `request.sender`, `.$get(...)`, `.$set(...)`, or `request.$context.get(...)` in deployed page-body handlers.
		       - Do NOT omit `$context` or `scopes` when forwarding a request from one handler into another page-scoped handler chain.
		       - Do NOT mutate unrelated request fields; only use documented control flags such as `preventAttributeChangeRequest` when that request type explicitly supports them.
		       - Do NOT parallelize multiple `$context.set(...)` calls with `Promise.all(...)` unless order independence is proven.

		       BEFORE SAVE CHECKLIST
		       - Does the requirement truly need a handler instead of a BUSINESS RULE, direct built-in request, validator, or converter?
		       - If this handler touches data access, syssettings, processes, or backend services, did you read `page-schema-creatio-devkit-common` before choosing the implementation pattern?
		       - Are the exact `/**SCHEMA_HANDLERS*/` markers still present around the handlers array?
		       - Is `SCHEMA_HANDLERS` still a JavaScript array section?
		       - Is every handler entry still an object with string `request` and `handler` fields?
		       - Do attribute-name guards use strict equality / inequality (`===` / `!==`) unless coercion is intentional?
		       - Is the `next` placement intentional (`before`, `after`, or omitted) for this exact workflow?
		       - Were built-in request names preferred before introducing any custom `usr.*Request`?
		       - Are `$context` and `scopes` forwarded in every imperative follow-up request that stays on the live page scope?
		       - If `fetch(...)` is still used, is there an explicit reason why no canonical `crt.*Request`, SDK service, or `sdk.Model` pattern from `page-schema-creatio-devkit-common` covers the scenario?
		       - Does page-state writeback use `await request.$context.set(...)` unless the task explicitly matches a compatibility pattern already present in the schema?
		       - Is this edit still using the canonical page-body API (`request.value`, `await request.$context["Attr"]`, `await request.$context.set(...)`) rather than a compatibility form?
		       - Was any new SDK import added through `SCHEMA_DEPS` / `SCHEMA_ARGS` using the existing page aliasing style?
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for editing handler sections in Freedom UI page bodies.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "page-schema-handlers-guidance")]
	[Description("Returns canonical MCP guidance for creating and editing Freedom UI page handlers inside raw page schema bodies.")]
	public ResourceContents GetGuide() => Guide;
}
