using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[AllureNUnit]
[AllureFeature("mcp-guidance-resources")]
[NonParallelizable]
public sealed class McpGuidanceResourceE2ETests {
	private const string DocsScheme = "docs";
	private const string GuidesPath = "mcp/guides";
	private static readonly string AppModelingUri = BuildGuideUri("app-modeling");
	private static readonly string ExistingAppMaintenanceUri = BuildGuideUri("existing-app-maintenance");
	private static readonly string PageSchemaHandlersUri = BuildGuideUri("page-schema-handlers");
	private static readonly string PageSchemaSdkCommonUri = BuildGuideUri("page-schema-sdk-common");
	private static readonly string PageSchemaValidatorsUri = BuildGuideUri("page-schema-validators");

	[Test]
	[AllureTag("mcp-guidance-resources")]
	[AllureName("MCP server advertises modeling and existing-app guidance resources")]
	public async Task McpServer_Should_Advertise_Guidance_Resources() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientResource> resources = await context.Session.ListResourcesAsync(context.CancellationTokenSource.Token);

		// Assert
		resources.Select(resource => resource.Uri).Should().Contain([
				AppModelingUri,
				ExistingAppMaintenanceUri,
				PageSchemaHandlersUri,
				PageSchemaSdkCommonUri,
				PageSchemaValidatorsUri
			],
			because: "the MCP server should advertise creation existing-app handler validator and sdk-common guidance resources");
	}

	[Test]
	[AllureTag("mcp-guidance-resources")]
	[AllureName("MCP server returns the existing-app maintenance guidance article")]
	public async Task McpServer_Should_Return_Existing_App_Maintenance_Guidance() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ReadResourceResult result = await context.Session.ReadResourceAsync(ExistingAppMaintenanceUri, context.CancellationTokenSource.Token);

		// Assert
		result.Contents.Should().ContainSingle(
			because: "the guidance resource should resolve to a single plain-text article");
		TextResourceContents article = result.Contents.Single().Should().BeOfType<TextResourceContents>(
			because: "the maintenance guide should be returned as plain text").Subject;
		article.Uri.Should().Be(ExistingAppMaintenanceUri,
			because: "the returned article should preserve the stable maintenance guidance URI");
		article.Text.Should().Contain("list-apps",
			because: "the article should explain how to discover the target installed application");
		article.Text.Should().Contain("update-page",
			because: "the article should describe the minimal page mutation path");
		article.Text.Should().Contain("do not wrap MCP arguments inside `args`",
			because: "the article should explicitly reject the request wrapper that caused the analyzed session failure");
		article.Text.Should().Contain("do not send `bundle` or `bundle.viewConfig` as the body payload",
			because: "the article should explain the concrete writable page payload shape");
		article.Text.Should().Contain("JSON object string",
			because: "the article should explain the concrete page resources payload shape");
		article.Text.Should().Contain("create-data-binding-db",
			because: "the article should steer standalone lookup seeding to MCP-native data-binding tools");
		article.Text.Should().Contain("modify-entity-schema-column",
			because: "the article should describe the minimal single-column schema mutation path");
		article.Text.Should().Contain("Read before write",
			because: "the article should encode the canonical maintenance verification discipline");
	}

	[Test]
	[AllureTag("mcp-guidance-resources")]
	[AllureName("MCP server returns the page-schema handlers guidance article")]
	public async Task McpServer_Should_Return_Page_Schema_Handlers_Guidance() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ReadResourceResult result = await context.Session.ReadResourceAsync(PageSchemaHandlersUri, context.CancellationTokenSource.Token);

		// Assert
		TextResourceContents article = result.Contents.Single().Should().BeOfType<TextResourceContents>(
			because: "the handlers guide should resolve to a single plain-text article").Subject;
		article.Uri.Should().Be(PageSchemaHandlersUri,
			because: "the returned article should preserve the stable handler guidance URI");
		article.Text.Should().Contain("SCHEMA_HANDLERS",
			because: "the handler guide should anchor editing to the correct page-body marker section");
		article.Text.Should().Contain("you MUST read `page-schema-sdk-common` before touching `SCHEMA_DEPS`, `SCHEMA_ARGS`, SDK services, or raw service calls",
			because: "the handler guide should point sdk-backed and service-backed handler edits to the dedicated sdk common guide");
		article.Text.Should().Contain("you MUST read `page-schema-sdk-common` before touching `SCHEMA_DEPS`, `SCHEMA_ARGS`, SDK services, or raw service calls",
			because: "the handler guide should make sdk and service routing to the sdk-common guide mandatory");
		article.Text.Should().Contain("Mandatory routing rule: when the handler requirement includes any data access, system setting read/write, process execution, model query, or backend/external service call",
			because: "the handler guide should force ai callers through the sdk-common guide before they choose a data or service implementation pattern");
		article.Text.Should().Contain("JavaScript array section",
			because: "the handler guide should state the runtime section shape directly");
		article.Text.Should().Contain("Request shape quick reference",
			because: "the handler guide should explicitly separate declarative request config from imperative runtime dispatch");
		article.Text.Should().Contain("This table covers direct triggers from page config.",
			because: "the handler guide should separate direct request triggers from handler interception scenarios");
		article.Text.Should().Contain("| imperative dispatch from handler code | `type` + flat payload fields + `$context` + usually `scopes` |",
			because: "the handler guide should include the canonical imperative dispatch shape");
		article.Text.Should().Contain("API choice rules",
			because: "the handler guide should explain the page-body request-dispatch choice explicitly");
		article.Text.Should().Contain("| deployed page-body handler in `SCHEMA_HANDLERS` | `await request.$context.executeRequest(...)` |",
			because: "the handler guide should prefer executeRequest for deployed page-body handlers");
		article.Text.Should().Contain("Do NOT default to `sdk.HandlerChainService.instance.process(...)` in deployed page-body handlers; use `request.$context.executeRequest(...)` unless the task explicitly matches an advanced SDK pattern from `page-schema-sdk-common`.",
			because: "the handler guide should keep HandlerChainService out of the default page-body authoring path");
		article.Text.Should().Contain("Chain-control rules",
			because: "the handler guide should explain next-placement semantics from the handler chain contract");
		article.Text.Should().Contain("Call `next` intentionally: place it `before`, `after`, or omit it only for an intentional chain break or full behavior replacement.",
			because: "the handler guide should not contradict its own intentional chain-break guidance with an exactly-once rule");
		article.Text.Should().Contain("Use `await request.$context[\"<AttributeName>\"]` to read any other page attribute in deployed page-body handlers.",
			because: "the handler guide should standardize read access on bracket syntax for deployed page-body handlers");
		article.Text.Should().Contain("Direct property assignment on `request.$context` is allowed only for transient runtime references such as subscriptions or service handles; page attributes still use `await request.$context.set(...)`.",
			because: "the handler guide should classify direct $context property writes as transient-runtime exceptions instead of a competing page-attribute write style");
		article.Text.Should().Contain("Prefer `request.value` over re-reading the triggering attribute through the view-model context.",
			because: "the handler guide should keep simple attribute mirroring on request.value instead of redundant reads");
		article.Text.Should().Contain("Do NOT use `request.sender`, `.$get(...)`, `.$set(...)`, or `request.$context.get(...)` in deployed page-body handlers.",
			because: "the handler guide should explicitly reject legacy sender/get/set access patterns");
		article.Text.Should().Contain("Do NOT choose raw `fetch(...)` to a platform endpoint before checking `page-schema-sdk-common` for a canonical `crt.*Request`, SDK service, or `sdk.Model` pattern.",
			because: "the handler guide should stop callers from defaulting to raw platform fetches");
		article.Text.Should().Contain("same declared view-model attribute that handlers read or write through `$context.set(\"<AttributeName>\", ...)`",
			because: "the handler guide should make handler-driven control binding explicit without relying on datasource naming conventions");
		article.Text.Should().Contain("Do NOT infer the correct binding from naming patterns such as `$PDS_*`.",
			because: "the handler guide should explicitly reject naming-based assumptions for datasource-backed attributes");
		article.Text.Should().Contain("Rule: when handlers write attribute `UsrName` through `$context.set(\"UsrName\", ...)`, the matching `viewConfigDiff` control MUST use `\"control\": \"$UsrName\"`.",
			because: "the handler guide should provide one concrete rule for the exact failure mode where a handler writes UsrName on init");
		article.Text.Should().Contain("Wrong: handler writes `UsrNameForHandler`, but the control still uses `\"control\": \"$UsrName\"`.",
			because: "the handler guide should call out the real failure mode where handler logic and control binding drift onto different declared attributes");
		article.Text.Should().Contain("If the control is inherited from a parent schema and is absent from the current schema's `viewConfigDiff`, add one local `merge` operation for that inherited control name.",
			because: "the handler guide should explain how to rebind inherited controls that are only defined in the parent schema");
		article.Text.Should().Contain("Do NOT add a second local patch merge when the current schema already has a local operation for the same control.",
			because: "the handler guide should forbid only duplicate local merges, not the first local merge for an inherited control");
		article.Text.Should().Contain("Compatibility note: existing product code may also use `request.$context.attributes[...]` or direct property assignment.",
			because: "the handler guide should acknowledge compatibility forms from product code without promoting them to the canonical AI-first pattern");
		article.Text.Should().Contain("const currentStatus = await request.$context[\"UsrStatus\"];",
			because: "the handler guide should include a concrete bracket-based context-read example for AI authoring");
		article.Text.Should().NotContain("Use `await request.$context.get(\"<AttributeName>\")`",
			because: "the handler guide should no longer teach the unsupported request.$context.get API as a supported read pattern");
		article.Text.Should().Contain("prefer `return next?.handle(request);` so the downstream result is preserved explicitly",
			because: "the handler guide should document the canonical pass-through branch pattern");
		article.Text.Should().Contain("crt.HandleViewModelInitRequest",
			because: "the handler guide should include the lifecycle init handler example");
		article.Text.Should().Contain("const currentMode = await $context[\"<ModeAttribute>\"];",
			because: "the handler guide should use bracket-based reads in the attribute-change template");
		article.Text.Should().Contain("if (attributeName !== \"<AttributeName>\") {",
			because: "the attribute-change template should use strict inequality in its guard");
		article.Text.Should().Contain("Mirror one text field into another on attribute change",
			because: "the handler guide should include the canonical copy-text scenario that motivated the API clarification");
		article.Text.Should().Contain("await request.$context.set(\"UsrCopyTextField\", request.value);",
			because: "the handler guide should teach the simplest supported mirror-on-change pattern");
		article.Text.Should().Contain("Sync one field into another only when the target value actually differs",
			because: "the handler guide should include a guarded sync template that avoids unnecessary writes");
		article.Text.Should().Contain("const value = request.value;",
			because: "the guarded sync template should read the changed value directly from request.value");
		article.Text.Should().Contain("const targetValue = await request.$context[\"<TargetAttribute>\"];",
			because: "the guarded sync template should read the dependent attribute through the canonical bracket-based pattern");
		article.Text.Should().Contain("if (value !== undefined && targetValue !== value) {",
			because: "the guarded sync template should show the equality guard explicitly");
		article.Text.Should().Contain("await request.$context.set(\"<TargetAttribute>\", value);",
			because: "the guarded sync template should write through the canonical setter API");
		article.Text.Should().Contain("request: \"crt.SaveRecordRequest\"",
			because: "the handler guide should include a canonical save handler example that captures and returns the downstream result");
		article.Text.Should().Contain("const saveResult = await next?.handle(request);",
			because: "the handler guide should show the call-next-in-the-middle pattern from handler chain examples");
		article.Text.Should().Contain("return saveResult;",
			because: "the handler guide should preserve the downstream save result when custom logic runs after save");
		article.Text.Should().Contain("Subscription lifecycle across init/resume/pause/destroy",
			because: "the handler guide should include a concrete lifecycle template for subscriptions and cleanup");
		article.Text.Should().Contain("This template requires `@creatio-devkit/common` and a live `sdk` alias from `SCHEMA_DEPS` / `SCHEMA_ARGS`; do NOT invent placeholder subscription services.",
			because: "the subscription template should tell AI to use a concrete SDK-backed service instead of inventing one");
		article.Text.Should().Contain("const messageChannelService = new sdk.MessageChannelService();",
			because: "the subscription template should show a concrete SDK service for page-schema subscriptions");
		article.Text.Should().Contain("request.$context.subscription = await messageChannelService.subscribe(\"<Channel>\", async event => {",
			because: "the subscription template should make async callbacks explicit so setter-based writeback stays canonical");
		article.Text.Should().Contain("// transient runtime handle, not a page attribute",
			because: "the subscription template should mark direct $context property writes as transient-state-only");
		article.Text.Should().Contain("await request.$context.set(\"<TargetAttribute>\", event.body);",
			because: "the subscription template should keep page attribute writes on the canonical setter API");
		article.Text.Should().Contain("request.$context.subscription?.unsubscribe();",
			because: "the lifecycle template should show explicit cleanup on pause and destroy");
		article.Text.Should().Contain("request.$context.subscription = null;",
			because: "the lifecycle template should reset transient subscription state after cleanup");
		article.Text.Should().Contain("crt.HandleViewModelResumeRequest",
			because: "the handler guide should include the built-in lifecycle handler catalog");
		article.Text.Should().Contain("Use `crt.HandleViewModelResumeRequest` when the page must restore runtime subscriptions or reinitialize transient state after returning to the page.",
			because: "the handler guide should give AI a concrete resume-use selection rule");
		article.Text.Should().Contain("Use `crt.HandleViewModelPauseRequest` when the page must stop temporary runtime work before the page is resumed or destroyed.",
			because: "the handler guide should give AI a concrete pause-use selection rule");
		article.Text.Should().Contain("Use `crt.CancelRecordChangesRequest` when the page must discard unsaved edits and return to the clean state.",
			because: "the handler guide should include the built-in cancel-edits request in the selection hints");
		article.Text.Should().Contain("crt.DeleteRecordRequest",
			because: "the handler guide should include the built-in delete request catalog and direct-request guidance");
		article.Text.Should().Contain("crt.LoadDataRequest",
			because: "the handler guide should include the built-in load-data request catalog and guidance");
		article.Text.Should().Contain("crt.CreateEmailRequest",
			because: "the handler guide should include the built-in create-email request catalog and guidance");
		article.Text.Should().Contain("crt.CopyClipboardRequest",
			because: "the handler guide should include the built-in clipboard request catalog and guidance");
		article.Text.Should().Contain("crt.CopyInputToClipboardRequest",
			because: "the handler guide should include the built-in copy-input request catalog and guidance");
		article.Text.Should().Contain("Standard handler parameter catalog",
			because: "the handler guide should expose concrete payload contracts backed by source implementation");
		article.Text.Should().Contain("| Request | Kind | Params for AI authoring | Notes |",
			because: "the handler guide should render request contracts in a compact table for AI consumption");
		article.Text.Should().Contain("| `crt.OpenPageRequest` | config | `schemaName` required, `packageUId?`, `modelInitConfigs?`, `parameters?`, `skipUnsavedData?` | standard open-page request |",
			because: "the handler guide should expose the source-backed open-page request fields");
		article.Text.Should().Contain("| `crt.LoadDataRequest` | config | `dataSourceName`, `config` (commonly `loadType`, `useLastLoadParameters?`), `showSuccessMessage?` | reload or refresh a page/list data source |",
			because: "the handler guide should expose the load-data request contract instead of only naming the request");
		article.Text.Should().Contain("| `crt.DeleteRecordRequest` | config | `recordId`, `itemsAttributeName` | delete one record; source handler converts it into `crt.DeleteRecordsRequest` |",
			because: "the handler guide should expose the source-backed delete-record request fields");
		article.Text.Should().Contain("| `crt.CancelRecordChangesRequest` | config | `none` | cancel edits |",
			because: "the handler guide should expose the cancel-edits request contract");
		article.Text.Should().Contain("| `crt.CreateEmailRequest` | config | `recordId?`, `bindingColumns?` | compose an email from current context |",
			because: "the handler guide should expose the create-email request contract");
		article.Text.Should().Contain("| `crt.CopyClipboardRequest` | config | `value` required | copy a prepared literal value |",
			because: "the handler guide should expose the literal clipboard request contract");
		article.Text.Should().Contain("| `crt.CopyInputToClipboardRequest` | config | `attribute` required, `successMessageArea?` | copy the value of a page attribute |",
			because: "the handler guide should expose the page-attribute clipboard request contract");
		article.Text.Should().Contain("| `crt.HandleViewModelAttributeChangeRequest` | runtime | `attributeName`, `value`, `oldValue`, `silent` (deprecated), `preventAttributeChangeRequest`, `preventStateChange`, `preventRunBusinessRules` | author handlers against these runtime fields |",
			because: "the handler guide should expose the runtime payload for attribute-change handlers");
		article.Text.Should().Contain("| User-visible name | Source reality | Params | Notes |",
			because: "the handler guide should isolate user-visible and source-runtime mismatches in a dedicated AI-readable table");
		article.Text.Should().Contain("| `crt.ShowDialog` | source request is `crt.ShowDialogRequest`, handled by `crt.ShowDialogHandler` | `dialogConfig` with `message`, `actions`, optional `title` | in code author `type: \"crt.ShowDialogRequest\"`; `crt.ShowDialog` is the user-visible catalog label |",
			because: "the handler guide should disambiguate the user-visible dialog label from the actual source request type with an explicit authoring rule");
		article.Text.Should().Contain("Minimal `dialogConfig` shape:",
			because: "the handler guide should show a concrete minimal dialog payload instead of only naming the config field");
		article.Text.Should().Contain("Do NOT create a custom handler when a direct request already matches the requirement",
			because: "the handler guide should explicitly prevent unnecessary custom handlers for simple actions");
		article.Text.Should().Contain("Multiple handlers in one page-body array",
			because: "the handler guide should show how several handler entries coexist in one schema array");
		article.Text.Should().MatchRegex(@"if \(request\.attributeName !== ""UsrStatus""\)\s*\{\s*return next\?\.handle\(request\);\s*\}\s*const result = await next\?\.handle\(request\);\s*await request\.\$context\.set\(""UsrStatusChanged"", true\);\s*return result;",
			because: "the multiple-handlers attribute-change example should preserve and return the downstream result consistently with the canonical post-next templates");
		article.Text.Should().Contain("Use this pattern only when the button starts a multi-step domain workflow that is not a single built-in `crt.*Request`.",
			because: "the custom action example should state when a usr.* request is actually justified");
		article.Text.Should().MatchRegex(@"(?s)request: ""usr\.RunCustomActionRequest"".*?type: ""crt\.RunBusinessProcessRequest"".*?const result = await next\?\.handle\(request\);\s*return result;",
			because: "custom action templates should preserve and return the downstream result instead of introducing a second non-returning next pattern");
		article.Text.Should().Contain("Prefer a stable array order: lifecycle handlers first, attribute-change handlers next, and custom domain/action handlers after them.",
			because: "the handler guide should give AI a predictable ordering heuristic for multi-handler arrays");
		article.Text.Should().Contain("Orchestration patterns",
			because: "the handler guide should distinguish page-body dispatch, handler-chain dispatch, and direct SDK service orchestration");
		article.Text.Should().Contain("Use `await request.$context.executeRequest(...)` when a deployed page-body handler forwards into another page-scoped request.",
			because: "the handler guide should keep executeRequest as the default page-body orchestration path");
		article.Text.Should().Contain("Use SDK/domain services such as `sdk.ProcessEngineService` when the task is direct service orchestration rather than request forwarding.",
			because: "the handler guide should mention the direct service-orchestration path seen in product code");
		article.Text.Should().MatchRegex(@"type: ""crt\.RunBusinessProcessRequest"",\s+processName: ""<ProcessName>"",\s+\$context(: request\.\$context|),\s+scopes: \[\.\.\.request\.scopes\]",
			because: "the handler guide should keep scopes in the canonical RunBusinessProcessRequest forwarding examples");
		article.Text.Should().Contain("return; // intentional chain break: block downstream handlers and business rules",
			because: "the handler guide should explain why the blocking attribute-change branch intentionally omits next");
		article.Text.Should().Contain("Anti-patterns",
			because: "the handler guide should make invalid shapes and request names explicit for AI consumers");
		article.Text.Should().Contain("Do NOT invent placeholder SDK services such as `<Service>.subscribe(...)`; when SDK-based subscriptions are required, use a concrete service such as `sdk.MessageChannelService` and keep `SCHEMA_DEPS` / `SCHEMA_ARGS` aligned.",
			because: "the handler guide should stop AI from inventing placeholder subscription services in schema code");
		article.Text.Should().Contain("Preserve the exact `/**SCHEMA_HANDLERS*/` comment markers around the handlers array;",
			because: "safe editing rules should explain that clio depends on the marker comments to find the editable section");
		article.Text.Should().Contain("This guide is only for deployed page-body handlers inside the schema body returned by `get-page`.",
			because: "safe editing rules should explicitly scope the guide to page-body handler authoring only");
		article.Text.Should().Contain("Are the exact `/**SCHEMA_HANDLERS*/` markers still present around the handlers array?",
			because: "the handler guide should verify the section markers explicitly in the checklist");
		article.Text.Should().Contain("Is `SCHEMA_HANDLERS` still a JavaScript array section?",
			because: "the handler guide should end with binary checklist questions for self-validation");
		article.Text.Should().Contain("Is this edit still using the canonical page-body API (`request.value`, `await request.$context[\"Attr\"]`, `await request.$context.set(...)`) rather than a compatibility form?",
			because: "the checklist should force AI to confirm that it stayed on the canonical page-body API instead of drifting to compatibility patterns");
	}

	[Test]
	[AllureTag("mcp-guidance-resources")]
	[AllureName("MCP server returns the page-schema sdk common guidance article")]
	public async Task McpServer_Should_Return_Page_Schema_Sdk_Common_Guidance() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ReadResourceResult result = await context.Session.ReadResourceAsync(PageSchemaSdkCommonUri, context.CancellationTokenSource.Token);

		// Assert
		TextResourceContents article = result.Contents.Single().Should().BeOfType<TextResourceContents>(
			because: "the sdk common guide should resolve to a single plain-text article").Subject;
		article.Uri.Should().Be(PageSchemaSdkCommonUri,
			because: "the returned article should preserve the stable sdk common guidance URI");
		article.Text.Should().Contain("clio MCP page-schema sdk common guide",
			because: "the guide should identify itself as the dedicated sdk common article");
		article.Text.Should().Contain("Use this guide only for deployed page schema code",
			because: "the sdk common guide should explicitly scope itself to schema-body work");
		article.Text.Should().Contain("Do NOT use this guide for remote modules or frontend-source classes.",
			because: "the sdk common guide should explicitly reject non-schema contexts");
		article.Text.Should().Contain("Prefer built-in `crt.*` requests first.",
			because: "the sdk common guide should stop AI from importing sdk common when built-in requests are enough");
		article.Text.Should().Contain("Canonical dependency pattern",
			because: "the guide should start from the AMD dependency contract that page schemas must preserve");
		article.Text.Should().Contain("Reuse the live alias already present in the schema body",
			because: "the guide should preserve existing page alias style");
		article.Text.Should().Contain("new sdk.SysSettingsService()",
			because: "the guide should include the public syssettings service pattern seen in product code");
		article.Text.Should().Contain("new sdk.HttpClientService()",
			because: "the guide should include the public http-client service pattern seen in product code");
		article.Text.Should().Contain("new sdk.FeatureService()",
			because: "the guide should include the public feature-service pattern seen in product code");
		article.Text.Should().Contain("new sdk.RightsService()",
			because: "the guide should include the public rights-service pattern seen in product code");
		article.Text.Should().Contain("Pattern selection order for handler-side data/service work is mandatory",
			because: "the sdk-common guide should make the request/sdk/fetch routing order explicit");
		article.Text.Should().Contain("| read or update syssettings | `new sdk.SysSettingsService()` | raw `fetch(...)` only when the target is not covered by the public syssettings API |",
			because: "the sdk-common guide should define syssettings fetch as a narrow fallback rather than the default");
		article.Text.Should().Contain("| platform endpoint with known page or sdk pattern | canonical `crt.*Request`, SDK service, or `sdk.Model` | do NOT jump to raw `fetch(...)` first |",
			because: "the sdk-common guide should explicitly block premature fetch usage for known platform scenarios");
		article.Text.Should().Contain("const featureEnabled = await new sdk.FeatureService().getFeatureState(\"UsrFeatureCode\");",
			because: "the guide should include a minimal feature-service example instead of only naming the service");
		article.Text.Should().Contain("const response = await new sdk.HttpClientService().get(\"<Url>\");",
			because: "the guide should include a minimal http-client example instead of only naming the service");
		article.Text.Should().Contain("const canExecute = await new sdk.RightsService().getCanExecuteOperation(\"CanManageUsers\");",
			because: "the guide should include a minimal rights-service example instead of only naming the service");
		article.Text.Should().Contain("new sdk.MessageChannelService()",
			because: "the guide should include the public message-channel service pattern seen in product code");
		article.Text.Should().Contain("new sdk.ProcessEngineService()",
			because: "the guide should include the public process-engine service pattern seen in product code");
		article.Text.Should().Contain("await sdk.Model.create(\"SysProcessElementLog\")",
			because: "the guide should include the public model-query pattern seen in product code");
		article.Text.Should().Contain("`SysSettingsService.getByCode(...)` commonly returns an object with fields such as `value` and `displayValue`.",
			because: "the guide should explain the syssettings return shape directly");
		article.Text.Should().Contain("Use `.value` for raw or numeric comparisons and validator thresholds.",
			because: "the guide should standardize numeric syssettings usage");
		article.Text.Should().Contain("Use `.displayValue` only when the page attribute should receive the display text shown to the user.",
			because: "the guide should standardize display syssettings usage");
		article.Text.Should().Contain("| `sdk.MessageChannelType` | choose channel kind for `MessageChannelService.sendMessage(...)` |",
			because: "the guide should expose the base-derived message-channel enum through the sdk contract");
		article.Text.Should().Contain("| `sdk.FilterGroup` | build model-query filters for `sdk.Model.load(...)` |",
			because: "the guide should expose the base-derived filter helper used in schema model queries");
		article.Text.Should().Contain("| `sdk.ComparisonType` | specify comparison operators inside `FilterGroup` |",
			because: "the guide should expose the base-derived comparison enum used by filter-building helpers");
		article.Text.Should().Contain("| `sdk.ModelParameterType` | declare parameter kinds such as `Filter` in `model.load(...)` |",
			because: "the guide should expose the base-derived model-parameter enum used by schema model loads");
		article.Text.Should().Contain("| work with collection attributes | `const collection = await request.$context[\"Items\"]` | `createItem(...)`, `registerOnCollectionChangeCallback(...)`, `registerOnItemAttributesChangesCallback(...)`, `sdk.ViewModelCollectionActionType` |",
			because: "the guide should expose the collection-oriented sdk surface used in schema body code");
		article.Text.Should().Contain("| `sdk.ViewModelCollectionActionType` | filter collection change callbacks by action such as `Add` or `Remove` |",
			because: "the guide should expose the collection action enum through the sdk contract");
		article.Text.Should().Contain("define(\"UsrSome_FormPage\", /**SCHEMA_DEPS*/[\"@creatio-devkit/common\"]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/(sdk)/**SCHEMA_ARGS*/ {",
			because: "the message-channel example should stay inside a full schema-body wrapper");
		article.Text.Should().Contain("const result = await next?.handle(request);",
			because: "handler-shaped sdk examples should align with the canonical pattern of capturing and returning the downstream result");
		article.Text.Should().Contain("return result;",
			because: "handler-shaped sdk examples should return the captured downstream result after sdk side effects");
		article.Text.Should().Contain("request.$context.subscription = await messageChannelService.subscribe(\"TestPTP\", async event => {",
			because: "the guide should make the message-channel callback async so setter failures are not dropped");
		article.Text.Should().Contain("// transient runtime handle, not a page attribute",
			because: "the guide should mark direct $context property assignment as a transient-state exception inside sdk lifecycle templates");
		article.Text.Should().Contain("await request.$context.set(\"UsrIncomingMessage\", event.body);",
			because: "the guide should keep schema attribute writes on the canonical awaited setter API inside sdk-backed callbacks");
		article.Text.Should().Contain("await messageChannelService.sendMessage(\"TestPTP\", \"Hello\", sdk.MessageChannelType.PTP);",
			because: "the guide should not teach fire-and-forget channel publishing");
		article.Text.Should().Contain("`PTP` is an example; keep the local channel type pattern already used by the schema.",
			because: "the guide should explain that the sample channel type must match the local schema pattern");
		article.Text.Should().Contain("Model API in schema body:",
			because: "the guide should have a dedicated schema-body model section instead of only a process-specific query snippet");
		article.Text.Should().Contain("Prefer built-in `crt.*` requests for standard page actions such as open/save/create/delete when one request already solves the task.",
			because: "the guide should tell AI when not to reach for sdk.Model");
		article.Text.Should().Contain("Use `await sdk.Model.create(\"<EntitySchema>\")` only when the schema needs custom data access or query logic that is not a single built-in `crt.*` request.",
			because: "the guide should define when model API is justified in schema code");
		article.Text.Should().Contain("| load records | `const model = await sdk.Model.create(\"Contact\"); await model.load({ attributes, parameters });` |",
			because: "the guide should provide a compact canonical load-records pattern");
		article.Text.Should().Contain("| insert record | `await model.insert({ Name: \"John Smith\" });` |",
			because: "the guide should provide a compact canonical insert pattern");
		article.Text.Should().Contain("| update record | `await model.update({ Name: \"John Smith\" }, [{ type: \"primaryColumnValue\", value: recordId }]);` |",
			because: "the guide should provide a compact canonical update pattern");
		article.Text.Should().Contain("| delete record | `await model.delete([{ type: \"primaryColumnValue\", value: recordId }]);` |",
			because: "the guide should provide a compact canonical delete pattern");
		article.Text.Should().Contain("const filters = new sdk.FilterGroup();",
			because: "the guide should provide a compact canonical filter-group pattern for model queries");
		article.Text.Should().Contain("await filters.addSchemaColumnFilterWithParameter(sdk.ComparisonType.Equal, \"Address\", request.value);",
			because: "the guide should show how comparison type is actually used in schema-body model filters");
		article.Text.Should().Contain("const rows = await model.load({ attributes: [\"Id\", \"Name\"], parameters: [{ type: sdk.ModelParameterType.Filter, value: filters }] });",
			because: "the guide should show how model-parameter type is actually used in schema-body model loads");
		article.Text.Should().Contain("Collection API in schema body:",
			because: "the guide should have a dedicated collection section instead of leaving collection operations implicit");
		article.Text.Should().Contain("Use collection API only when the page attribute already stores a collection and the schema must add records or react to collection mutations.",
			because: "the guide should define when collection API is justified in schema code");
		article.Text.Should().Contain("Read the collection from page context with `const collection = await request.$context[\"Items\"];`.",
			because: "the guide should standardize how schema code reads collection attributes from page context");
		article.Text.Should().Contain("`\"Items\"` is an example; use the real collection attribute name already present in the page schema.",
			because: "the guide should avoid teaching AI to copy a literal collection attribute name");
		article.Text.Should().Contain("| add a record to the collection | `await collection.createItem({ initialModelValues: { Name: \"Brule\" }, businessRulesActive: true });` |",
			because: "the guide should include the real schema-body pattern for adding collection items");
		article.Text.Should().Contain("| react only to added items | `collection.registerOnCollectionChangeCallback(onAdd, sdk.ViewModelCollectionActionType.Add);` |",
			because: "the guide should show how collection action filters are used with change callbacks");
		article.Text.Should().Contain("| react to item attribute updates | `collection.registerOnItemAttributesChangesCallback(onItemChanged);` |",
			because: "the guide should show how to observe collection item attribute changes");
		article.Text.Should().Contain("| stop listening | `collection.unregisterOnCollectionChangeCallback(onAdd, sdk.ViewModelCollectionActionType.Add);` |",
			because: "the guide should show how to detach collection listeners without inventing cleanup APIs");
		article.Text.Should().Contain("Collection change callbacks receive a change object with fields such as `collection`, `affectedElements`, `action`, and optional `index`.",
			because: "the guide should summarize the public collection-change payload shape for AI callers");
		article.Text.Should().Contain("Keep the same callback reference when unregistering collection listeners.",
			because: "the guide should warn that unregistering requires the original callback reference");
		article.Text.Should().Contain("Collection listener lifecycle in a handler:",
			because: "the guide should include a canonical lifecycle example for collection callback registration and cleanup");
		article.Text.Should().Contain("request.$context.onAdd = async changes => { // transient runtime callback reference, not a page attribute",
			because: "the collection lifecycle example should keep callback references on $context only as transient runtime state");
		article.Text.Should().Contain("collection.registerOnCollectionChangeCallback(request.$context.onAdd, sdk.ViewModelCollectionActionType.Add);",
			because: "the collection lifecycle example should show registration with a stable callback reference");
		article.Text.Should().Contain("collection.unregisterOnCollectionChangeCallback(request.$context.onAdd, sdk.ViewModelCollectionActionType.Add);",
			because: "the collection lifecycle example should show cleanup with the same callback reference");
		article.Text.Should().Contain("Collection item-attribute watcher in a handler:",
			because: "the guide should include a full handler example for collection item attribute watchers");
		article.Text.Should().Contain("request.$context.onCollectionItemChanged = async item => { // transient runtime callback reference, not a page attribute",
			because: "the item-attribute watcher should keep its callback reference on $context as transient runtime state");
		article.Text.Should().Contain("const name = await item[\"Name\"];",
			because: "the guide should show how collection item attributes are read inside callback handlers");
		article.Text.Should().Contain("await request.$context.set(\"UsrLastChangedItemName\", name);",
			because: "the item-attribute watcher should write back through the canonical awaited setter API");
		article.Text.Should().Contain("collection.registerOnItemAttributesChangesCallback(request.$context.onCollectionItemChanged);",
			because: "the guide should show registration of item-attribute change callbacks");
		article.Text.Should().Contain("collection.unregisterOnItemAttributesChangesCallback(request.$context.onCollectionItemChanged);",
			because: "the guide should show cleanup of item-attribute change callbacks");
		article.Text.Should().Contain("const dialogService = new sdk.DialogService();",
			because: "the guide should include the sdk dialog-service pattern seen in product code");
		article.Text.Should().Contain("Rule: if a handler is already dispatching requests, do NOT use `DialogService`; use `crt.ShowDialogRequest`.",
			because: "the guide should make the dialog stop-rule explicit for request-dispatching handlers");
		article.Text.Should().Contain("sdk.HandlerChainService.instance.process",
			because: "the guide should include the SDK-oriented handler-chain dispatch pattern");
		article.Text.Should().Contain("Inner handler/body snippet only: ProcessEngineService with model query helpers:",
			because: "fragment-only sdk snippets should say they are not standalone schema modules");
		article.Text.Should().Contain("Inner handler/body snippet only: DialogService from SDK code:",
			because: "fragment-only dialog snippets should say they are not standalone schema modules");
		article.Text.Should().Contain("Inner handler/body snippet only: HandlerChainService from advanced SDK-oriented schema code:",
			because: "fragment-only handler-chain snippets should say they are not standalone schema modules");
		article.Text.Should().Contain("Rule: in deployed page-body handlers, prefer `await request.$context.executeRequest(...)`.",
			because: "the guide should keep executeRequest as the default dispatch path for schema handlers");
		article.Text.Should().Contain("Do NOT use `import { ... } from \"@creatio-devkit/common\"` inside deployed page schema body code.",
			because: "the guide should explicitly block ES-module imports in schema-body code");
		article.Text.Should().NotContain("BaseRequest",
			because: "the schema-only sdk common guide should no longer advertise frontend-source request classes");
		article.Text.Should().Contain("Do NOT use internal `ɵ*` exports from the package.",
			because: "the guide should block internal API usage for AI consumers");
		article.Text.Should().Contain("Do NOT call a standard platform endpoint with raw `fetch(...)` before checking whether a built-in `crt.*Request`, public SDK service, or `sdk.Model` pattern already covers the scenario.",
			because: "the sdk-common anti-pattern list should stop callers from defaulting to raw platform fetches");
		article.Text.Should().Contain("Do NOT fire-and-forget SDK promises with `void somePromise`, `.then()` without error handling, or any other pattern that drops failures silently in schema-body code.",
			because: "the guide should explicitly reject silently dropped SDK promises");
		article.Text.Should().Contain("Do NOT invent collection helper APIs beyond `createItem(...)`, `registerOnCollectionChangeCallback(...)`, `registerOnItemAttributesChangesCallback(...)`, and their matching unregister methods shown here.",
			because: "the guide should keep AI from inventing unsupported collection helper APIs");
		article.Text.Should().Contain("Do `SCHEMA_DEPS` and `SCHEMA_ARGS` still have the same number of entries in the same order?",
			because: "the checklist should define aligned AMD dependencies and aliases precisely");
		article.Text.Should().Contain("Is every async SDK call awaited or explicitly handled with error flow instead of `void` / bare `.then()`?",
			because: "the checklist should make dropped SDK promises a concrete verification item");
		article.Text.Should().Contain("If `SysSettingsService.getByCode(...)` is used, is `.value` vs `.displayValue` chosen intentionally for this attribute or validation rule?",
			because: "the checklist should force an intentional choice for the syssettings return shape");
		article.Text.Should().Contain("If the task touches data access, system settings, processes, or backend services, was the implementation choice made in the required order: `crt.*Request` -> SDK service / `sdk.Model` -> raw `fetch(...)` only as justified fallback?",
			because: "the checklist should force an explicit routing decision for data and service scenarios");
		article.Text.Should().Contain("Are all shown snippets still valid inside deployed schema body code, not standalone TypeScript/module code?",
			because: "the checklist should guard against copying inner snippets as standalone modules");
	}

	[Test]
	[AllureTag("mcp-guidance-resources")]
	[AllureName("MCP server returns the page-schema validators guidance article")]
	public async Task McpServer_Should_Return_Page_Schema_Validators_Guidance() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ReadResourceResult result = await context.Session.ReadResourceAsync(PageSchemaValidatorsUri, context.CancellationTokenSource.Token);

		// Assert
		TextResourceContents article = result.Contents.Single().Should().BeOfType<TextResourceContents>(
			because: "the validators guide should resolve to a single plain-text article").Subject;
		article.Uri.Should().Be(PageSchemaValidatorsUri,
			because: "the returned article should preserve the stable validator guidance URI");
		article.Text.Should().Contain("SCHEMA_VALIDATORS",
			because: "the validator guide should anchor editing to the correct page-body marker section");
		article.Text.Should().Contain("also read `page-schema-sdk-common` before touching `SCHEMA_DEPS`, `SCHEMA_ARGS`, or SDK service calls",
			because: "the validator guide should point SDK-backed validator edits to the dedicated sdk common guide");
		article.Text.Should().Contain("field-value validation",
			because: "the validator guide should state the intended responsibility of validators");
		article.Text.Should().Contain("@CrtValidator",
			because: "the validator guide should mention the public frontend-source registration pattern");
		article.Text.Should().Contain("crt.MaxLength",
			because: "the validator guide should publish the built-in max-length validator in the standard decision table");
		article.Text.Should().Contain("Do NOT create a custom validator when a standard validator is sufficient",
			because: "the validator guide should explicitly prevent unnecessary custom validators for standard cases");
		article.Text.Should().Contain("const maxLength = await sysSettingsService.getByCode(config.settingCode);",
			because: "the validator guide should fetch the syssetting once before applying the canonical numeric field");
		article.Text.Should().Contain("if (maxLength?.value != null && value.length > Number(maxLength.value)) {",
			because: "the validator guide should use the canonical numeric `.value` field in the async syssettings example");
		article.Text.Should().Contain("setAttributePropertyValue(...)",
			because: "the validator guide should redirect dynamic UI-state logic away from validators without pointing to removed handler or converter guides");
	}

	private static async Task<ArrangeContext> ArrangeAsync(McpE2ESettings settings, TimeSpan timeout) {
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource);
	}

	private static string BuildGuideUri(string guideName) => $"{DocsScheme}://{GuidesPath}/{guideName}";

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
