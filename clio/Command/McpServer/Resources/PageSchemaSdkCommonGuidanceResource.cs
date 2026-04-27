using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for using <c>@creatio-devkit/common</c> in Freedom UI page schemas.
/// </summary>
[McpServerResourceType]
public sealed class PageSchemaSdkCommonGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/page-schema-sdk-common";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	internal static readonly TextResourceContents Guide = new() {
		Uri = ResourceUri,
		MimeType = "text/plain",
		Text = """
		       clio MCP page-schema sdk common guide

		       Scope
		       - Use this guide only for deployed page schema code: `SCHEMA_DEPS`, `SCHEMA_ARGS`, `SCHEMA_HANDLERS`, and `SCHEMA_VALIDATORS`.
		       - Read this guide together with `page-schema-handlers` when the task adds SDK services, model helpers, channel subscriptions, dialog helpers, data access, process execution, system settings access, or backend service calls inside `SCHEMA_HANDLERS`.
		       - Read this guide together with `page-schema-validators` when the task adds SDK-backed async validation inside `SCHEMA_VALIDATORS`.
		       - Do NOT use this guide for remote modules or frontend-source classes.
		       - Keep page-body authoring on public `@creatio-devkit/common` API only. Do NOT invent services or rely on internal `ɵ*` exports.
		       - Prefer built-in `crt.*` requests first. Add `@creatio-devkit/common` only when the schema task really needs SDK services, model helpers, or SDK-style dialog/orchestration code.
		       - Pattern selection order for handler-side data/service work is mandatory: 1. built-in `crt.*Request`, 2. public SDK service or `sdk.Model`, 3. raw `fetch(...)` only when the scenario is custom/external or no canonical request/SDK pattern covers it.

		       Canonical dependency pattern
		       - Add the AMD dependency only when the body actually uses SDK API.
		       - Reuse the live alias already present in the schema body. If the schema has no existing alias and you add one, prefer `sdk`.
		       - Keep `SCHEMA_DEPS` and `SCHEMA_ARGS` aligned in the same order.
		       - Minimal shape:
		         define("UsrSome_FormPage", /**SCHEMA_DEPS*/["@creatio-devkit/common"]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/(sdk)/**SCHEMA_ARGS*/ {
		           return {
		             // page body
		           };
		         });
		       - If the page already uses `function/**SCHEMA_ARGS*/(devkit)/**SCHEMA_ARGS*/`, keep `devkit`; do not rename the alias just for style.

		       Public API quick catalog
		       - Schema-body public API that is commonly useful in deployed page schemas:
		         | Need | Prefer | Typical members |
		         | --- | --- | --- |
		         | read or update syssettings | `new sdk.SysSettingsService()` | `getByCode(...)`, `getByCodes(...)`, `update(...)`, `updateMany(...)` |
		         | read a feature flag | `new sdk.FeatureService()` | `getFeatureState(...)` |
		         | call an external endpoint | `new sdk.HttpClientService()` | `get(...)`, `post(...)`, `put(...)`, `delete(...)` |
		         | check operation rights | `new sdk.RightsService()` | `getCanExecuteOperation(...)` |
		         | subscribe or send channel events | `new sdk.MessageChannelService()` | `subscribe(...)`, `sendMessage(...)`, `unsubscribe()` |
		         | run or continue a process directly | `new sdk.ProcessEngineService()` | `executeProcessByName(...)`, `completeExecuting(...)` |
		         | open a dialog from SDK code | `new sdk.DialogService()` | `open(...)` |
		         | query Creatio data | `await sdk.Model.create(...)`, `new sdk.FilterGroup()` | `load(...)`, `ComparisonType`, `ModelParameterType` |
		         | work with collection attributes | `const collection = await request.$context["Items"]` | `createItem(...)`, `registerOnCollectionChangeCallback(...)`, `registerOnItemAttributesChangesCallback(...)`, `sdk.ViewModelCollectionActionType` |
		       - Quick inline examples:
		         - `const featureEnabled = await new sdk.FeatureService().getFeatureState("UsrFeatureCode");`
		         - `const response = await new sdk.HttpClientService().get("<Url>");`
		         - `const canExecute = await new sdk.RightsService().getCanExecuteOperation("CanManageUsers");`
		       - Canonical choice guide for common handler-side scenarios:
		         | Scenario | Prefer first | Fallback only when needed |
		         | --- | --- | --- |
		         | open/save/create/delete/run-process page action | built-in `crt.*Request` | custom handler only when extra orchestration is required |
		         | read or update syssettings | `new sdk.SysSettingsService()` | raw `fetch(...)` only when the target is not covered by the public syssettings API |
		         | custom data query or mutation | `await sdk.Model.create(...)` | raw `fetch(...)` only when the data source is not accessible through page requests or `sdk.Model` |
		         | backend or external HTTP call | `new sdk.HttpClientService()` when the schema already uses sdk HTTP helpers | raw `fetch(...)` when the target service is custom/external and no canonical request or SDK helper fits |
		         | platform endpoint with known page or sdk pattern | canonical `crt.*Request`, SDK service, or `sdk.Model` | do NOT jump to raw `fetch(...)` first |
		       - `SysSettingsService.getByCode(...)` commonly returns an object with fields such as `value` and `displayValue`.
		         - Use `.value` for raw or numeric comparisons and validator thresholds.
		         - Use `.displayValue` only when the page attribute should receive the display text shown to the user.
		       - Base-derived helpers exposed through `sdk` and safe to use in schema body:
		         | Helper | Use |
		         | --- | --- |
		         | `sdk.MessageChannelType` | choose channel kind for `MessageChannelService.sendMessage(...)` |
		         | `sdk.FilterGroup` | build model-query filters for `sdk.Model.load(...)` |
		         | `sdk.ComparisonType` | specify comparison operators inside `FilterGroup` |
		         | `sdk.ModelParameterType` | declare parameter kinds such as `Filter` in `model.load(...)` |
		         | `sdk.ViewModelCollectionActionType` | filter collection change callbacks by action such as `Add` or `Remove` |
		       - Advanced schema-body pattern:
		         | Need | Prefer | Typical members |
		         | --- | --- | --- |
		         | low-level request-chain dispatch in SDK-oriented schema code | `sdk.HandlerChainService.instance.process(...)` | `process({ type, $context, scopes })` |

		       Page-body templates
		       - SysSettingsService inside a handler:
		         define("UsrSome_FormPage", /**SCHEMA_DEPS*/["@creatio-devkit/common"]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/(sdk)/**SCHEMA_ARGS*/ {
		           return {
		             handlers: /**SCHEMA_HANDLERS*/[
		               {
		                 request: "crt.HandleViewModelInitRequest",
		                 handler: async (request, next) => {
		                   const result = await next?.handle(request);
		                   const sysSettingsService = new sdk.SysSettingsService();
		                   const setting = await sysSettingsService.getByCode("UsrDefaultCity");
		                   await request.$context.set("UsrDefaultCity", setting.displayValue);
		                   return result;
		                 }
		               }
		             ]/**SCHEMA_HANDLERS*/
		           };
		         });

		       - Async validator with SysSettingsService:
		         define("UsrSome_FormPage", /**SCHEMA_DEPS*/["@creatio-devkit/common"]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/(sdk)/**SCHEMA_ARGS*/ {
		           return {
		             validators: /**SCHEMA_VALIDATORS*/{
		               "usr.MaxLengthFromSysSettingValidator": {
		                 "validator": function(config) {
		                   return async function(control) {
		                     const value = control.value;
		                     if (!value) {
		                       return null;
		                     }
		                     const sysSettingsService = new sdk.SysSettingsService();
		                     const maxLength = await sysSettingsService.getByCode(config.settingCode);
		                     return value.length > Number(maxLength.value)
		                       ? { "usr.MaxLengthFromSysSettingValidator": { message: config.message } }
		                       : null;
		                   };
		                 },
		                 "params": [{ "name": "settingCode" }, { "name": "message" }],
		                 "async": true
		               }
		             }/**SCHEMA_VALIDATORS*/
		           };
		         });

		       - MessageChannelService lifecycle:
		         define("UsrSome_FormPage", /**SCHEMA_DEPS*/["@creatio-devkit/common"]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/(sdk)/**SCHEMA_ARGS*/ {
		           return {
		             handlers: /**SCHEMA_HANDLERS*/[
		               {
		                 request: "crt.HandleViewModelInitRequest",
		                 handler: async (request, next) => {
		                   const result = await next?.handle(request);
		                   const messageChannelService = new sdk.MessageChannelService();
		                   // transient runtime handles may live on $context
		                   request.$context.subscription = await messageChannelService.subscribe("TestPTP", async event => { // transient runtime handle, not a page attribute
		                     await request.$context.set("UsrIncomingMessage", event.body);
		                   });
		                   await messageChannelService.sendMessage("TestPTP", "Hello", sdk.MessageChannelType.PTP); // `PTP` is an example; keep the local channel type pattern already used by the schema.
		                   return result;
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
		           };
		         });

		       - Model API in schema body:
		         - Prefer built-in `crt.*` requests for standard page actions such as open/save/create/delete when one request already solves the task.
		         - Use `await sdk.Model.create("<EntitySchema>")` only when the schema needs custom data access or query logic that is not a single built-in `crt.*` request.
		         - Common operations:
		           | Need | Prefer |
		           | --- | --- |
		           | load records | `const model = await sdk.Model.create("Contact"); await model.load({ attributes, parameters });` |
		           | insert record | `await model.insert({ Name: "John Smith" });` |
		           | update record | `await model.update({ Name: "John Smith" }, [{ type: "primaryColumnValue", value: recordId }]);` |
		           | delete record | `await model.delete([{ type: "primaryColumnValue", value: recordId }]);` |
		         - Query helpers:
		           `const filters = new sdk.FilterGroup();`
		           `await filters.addSchemaColumnFilterWithParameter(sdk.ComparisonType.Equal, "Address", request.value);`
		           `const rows = await model.load({ attributes: ["Id", "Name"], parameters: [{ type: sdk.ModelParameterType.Filter, value: filters }] });`

		       - Collection API in schema body:
		         - Use collection API only when the page attribute already stores a collection and the schema must add records or react to collection mutations.
		         - Read the collection from page context with `const collection = await request.$context["Items"];`. `"Items"` is an example; use the real collection attribute name already present in the page schema.
		         - Common operations:
		           | Need | Prefer |
		           | --- | --- |
		           | add a record to the collection | `await collection.createItem({ initialModelValues: { Name: "Brule" }, businessRulesActive: true });` |
		           | react only to added items | `collection.registerOnCollectionChangeCallback(onAdd, sdk.ViewModelCollectionActionType.Add);` |
		           | react to item attribute updates | `collection.registerOnItemAttributesChangesCallback(onItemChanged);` |
		           | stop listening | `collection.unregisterOnCollectionChangeCallback(onAdd, sdk.ViewModelCollectionActionType.Add);` |
		         - Collection change callbacks receive a change object with fields such as `collection`, `affectedElements`, `action`, and optional `index`.
		         - Keep the same callback reference when unregistering collection listeners.
		         - Collection listener lifecycle in a handler:
		           define("UsrSome_FormPage", /**SCHEMA_DEPS*/["@creatio-devkit/common"]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/(sdk)/**SCHEMA_ARGS*/ {
		             return {
		               handlers: /**SCHEMA_HANDLERS*/[
		                 {
		                   request: "crt.HandleViewModelInitRequest",
		                   handler: async (request, next) => {
		                     const result = await next?.handle(request);
		                     const collection = await request.$context["Items"];
		                     request.$context.onAdd = async changes => { // transient runtime callback reference, not a page attribute
		                       await request.$context.set("UsrLastAddedCount", changes.affectedElements.length);
		                     };
		                     collection.registerOnCollectionChangeCallback(request.$context.onAdd, sdk.ViewModelCollectionActionType.Add);
		                     return result;
		                   }
		                 },
		                 {
		                   request: "crt.HandleViewModelDestroyRequest",
		                   handler: async (request, next) => {
		                     const collection = await request.$context["Items"];
		                     if (request.$context.onAdd) {
		                       collection.unregisterOnCollectionChangeCallback(request.$context.onAdd, sdk.ViewModelCollectionActionType.Add);
		                       request.$context.onAdd = null; // clear transient runtime callback reference
		                     }
		                     return next?.handle(request);
		                   }
		                 }
		               ]/**SCHEMA_HANDLERS*/
		             };
		           });
		         - Collection item-attribute watcher in a handler:
		           define("UsrSome_FormPage", /**SCHEMA_DEPS*/["@creatio-devkit/common"]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/(sdk)/**SCHEMA_ARGS*/ {
		             return {
		               handlers: /**SCHEMA_HANDLERS*/[
		                 {
		                   request: "crt.HandleViewModelInitRequest",
		                   handler: async (request, next) => {
		                     const result = await next?.handle(request);
		                     const collection = await request.$context["Items"];
		                     request.$context.onCollectionItemChanged = async item => { // transient runtime callback reference, not a page attribute
		                       const name = await item["Name"];
		                       await request.$context.set("UsrLastChangedItemName", name);
		                     };
		                     collection.registerOnItemAttributesChangesCallback(request.$context.onCollectionItemChanged);
		                     return result;
		                   }
		                 },
		                 {
		                   request: "crt.HandleViewModelDestroyRequest",
		                   handler: async (request, next) => {
		                     const collection = await request.$context["Items"];
		                     if (request.$context.onCollectionItemChanged) {
		                       collection.unregisterOnItemAttributesChangesCallback(request.$context.onCollectionItemChanged);
		                       request.$context.onCollectionItemChanged = null; // clear transient runtime callback reference
		                     }
		                     return next?.handle(request);
		                   }
		                 }
		               ]/**SCHEMA_HANDLERS*/
		             };
		           });

		       - Inner handler/body snippet only: ProcessEngineService with model query helpers:
		         const processService = new sdk.ProcessEngineService();
		         const processResult = await processService.executeProcessByName("UsrDemoProcess", { RunMode: 1 }); // result metadata may include fields such as `processId`
		         const processLogModel = await sdk.Model.create("SysProcessElementLog");
		         const filters = new sdk.FilterGroup();
		         await filters.addSchemaColumnFilterWithParameter(sdk.ComparisonType.Equal, "SysProcess", processResult.processId);
		         const rows = await processLogModel.load({
		           attributes: ["Id"], // project only the model attributes you actually need from the query result
		           parameters: [{
		             type: sdk.ModelParameterType.Filter,
		             value: filters
		           }]
		         });

		       - Inner handler/body snippet only: DialogService from SDK code:
		         const dialogService = new sdk.DialogService();
		         await dialogService.open({
		           message: "This is message from DialogService",
		           actions: [
		             {
		               key: "ok",
		               config: {
		                 color: "primary",
		                 caption: "OK"
		               }
		             }
		           ]
		         });
		       - Rule: if a handler is already dispatching requests, do NOT use `DialogService`; use `crt.ShowDialogRequest`. Use `DialogService` only when the task explicitly matches existing SDK-style schema code.

		       - Inner handler/body snippet only: HandlerChainService from advanced SDK-oriented schema code:
		         return await sdk.HandlerChainService.instance.process({
		           type: "usr.OpenCustomPageRequest",
		           $context: request.$context,
		           scopes: [...request.scopes]
		         });
		       - Rule: in deployed page-body handlers, prefer `await request.$context.executeRequest(...)`. Use `sdk.HandlerChainService.instance.process(...)` only when the schema already follows an SDK-oriented orchestration pattern or the task explicitly requires low-level chain dispatch.

		       Anti-patterns
		       - Do NOT add `@creatio-devkit/common` when no SDK symbol is used after the edit.
		       - Do NOT add `@creatio-devkit/common` when a built-in `crt.*` request already solves the schema task.
		       - Do NOT call a standard platform endpoint with raw `fetch(...)` before checking whether a built-in `crt.*Request`, public SDK service, or `sdk.Model` pattern already covers the scenario.
		       - Do NOT treat raw `fetch(...)` as the default for `SysSettingsService`, model-style data access, or standard process/page actions; those scenarios must start from the canonical request/SDK patterns in this guide.
		       - Do NOT add `SCHEMA_DEPS` without the matching `SCHEMA_ARGS` alias, or vice versa.
		       - Do NOT rename an existing SDK alias only to switch between `sdk` and `devkit`.
		       - Do NOT use `import { ... } from "@creatio-devkit/common"` inside deployed page schema body code.
		       - Do NOT use decorator/class-based frontend-source APIs inside deployed page schema body code.
		       - Do NOT invent services such as `sdk.SettingsService` or `sdk.ProcessService`; use the public names from this guide.
		       - Do NOT invent collection helper APIs beyond `createItem(...)`, `registerOnCollectionChangeCallback(...)`, `registerOnItemAttributesChangesCallback(...)`, and their matching unregister methods shown here.
		       - Do NOT fire-and-forget SDK promises with `void somePromise`, `.then()` without error handling, or any other pattern that drops failures silently in schema-body code.
		       - Do NOT use internal `ɵ*` exports from the package.
		       - Do NOT mix `sdk.HandlerChainService.instance.process(...)` into deployed page-body code unless the task explicitly matches an existing SDK-oriented pattern.
		       - Existing product code may still use `request.$context.attributes[...]` or direct property assignment. Do not prefer those forms in newly generated schema-body code unless the local schema already follows that pattern.

		       BEFORE SAVE CHECKLIST
		       - Does the edited page body really need `@creatio-devkit/common`?
		       - Is this edit still inside deployed page schema body code, not a remote module?
		       - Do `SCHEMA_DEPS` and `SCHEMA_ARGS` still have the same number of entries in the same order?
		       - Was the existing SDK alias reused when the page already had one?
		       - Is every async SDK call awaited or explicitly handled with error flow instead of `void` / bare `.then()`?
		       - If `SysSettingsService.getByCode(...)` is used, is `.value` vs `.displayValue` chosen intentionally for this attribute or validation rule?
		       - Does every referenced SDK symbol come from the public API in this guide?
		       - Are built-in `crt.*` requests insufficient for this task?
		       - If the task touches data access, system settings, processes, or backend services, was the implementation choice made in the required order: `crt.*Request` -> SDK service / `sdk.Model` -> raw `fetch(...)` only as justified fallback?
		       - If raw `fetch(...)` is used, is the reason explicit and limited to a custom/external endpoint or a confirmed gap in the canonical request/SDK patterns?
		       - Does the chosen SDK pattern match the task: service call, model query, dialog helper, or advanced schema-body orchestration?
		       - Are all shown snippets still valid inside deployed schema body code, not standalone TypeScript/module code?
		       """
	};

	/// <summary>
	/// Returns the canonical guidance article for using <c>@creatio-devkit/common</c> in Freedom UI page work.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "page-schema-sdk-common-guidance")]
	[Description("Returns canonical MCP guidance for using @creatio-devkit/common in deployed Freedom UI page schema handlers, validators, and related schema-body SDK patterns.")]
	public ResourceContents GetGuide() => Guide;
}
