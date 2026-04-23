using Clio.Command.McpServer.Resources;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using System.Collections.Generic;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit tests for MCP guidance resources that expose cross-tool modeling rules.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpGuidanceResourceTests {
	[Test]
	[Category("Unit")]
	[Description("Returns a canonical MCP guidance article for Creatio app modeling so consumer agents can rely on MCP-owned guardrails.")]
	public void AppModelingGuidanceResource_Should_Return_Canonical_Modeling_Guide() {
		// Arrange
		AppModelingGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = result.Should().BeOfType<TextResourceContents>(
			because: "the modeling guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/app-modeling",
			because: "the resource should expose a stable MCP URI for cross-tool modeling guidance");
		article.MimeType.Should().Be("text/plain",
			because: "the modeling guide should be discoverable as plain text");
		article.Text.Should().Contain("sync-schemas",
			because: "the guide should steer callers toward the batch schema workflow");
		article.Text.Should().Contain("sync-pages",
			because: "the guide should steer callers toward the batch page workflow");
		article.Text.Should().Contain("create-app",
			because: "the guide should explain the canonical app-creation entry point");
		article.Text.Should().Contain("create-app-section",
			because: "the guide should explain the canonical existing-app section creation entry point");
		article.Text.Should().Contain("update-app-section",
			because: "the guide should explain the canonical existing-section metadata update entry point");
		article.Text.Should().Contain("Canonical new-app entity flow",
			because: "the guide should publish the preferred new-app entity sequence as MCP-owned guidance");
		article.Text.Should().Contain("already performs internal Data Forge enrichment",
			because: "the guide should teach callers that create-app owns the required Data Forge logic inside clio");
		article.Text.Should().Contain("still creates the app shell",
			because: "the guide should document the soft-fallback semantics when Data Forge is degraded or unavailable");
		article.Text.Should().Contain("Canonical page flow after planning a page change",
			because: "the guide should publish the preferred page inspection and write sequence as MCP-owned guidance");
		article.Text.Should().Contain("get-guidance",
			because: "the creation-oriented guide should point callers to the dedicated existing-app maintenance guide through the guidance tool");
		article.Text.Should().Contain("scalar-only for app shell fields",
			because: "the guide should state that create-app keeps shell fields as plain strings");
		article.Text.Should().Contain("Do not send localization-map fields",
			because: "the guide should prevent callers from mixing create-app with entity-schema localization maps");
		article.Text.Should().Contain("create-app-section` is scalar-only",
			because: "the guide should state that section-create also keeps shell fields as plain scalars");
		article.Text.Should().Contain("update-app-section` is scalar-only",
			because: "the guide should state that section-update also keeps metadata fields as plain scalars");
		article.Text.Should().Contain("create the app first and then apply those captions through `sync-schemas`",
			because: "the guide should steer callers toward follow-up schema tools when localized captions are needed");
		article.Text.Should().Contain("compatibility fallbacks",
			because: "the guide should explain that single-tool mutations are fallback paths rather than the primary modeling workflow");
		article.Text.Should().Contain("Apply the same anti-duplication rule to supporting entities",
			because: "the guide should extend the duplicate-prevention guardrail beyond only the canonical main entity");
		article.Text.Should().Contain("Business captions are not naming authority",
			because: "the guide should reject minting new technical schema codes from requirement wording when runtime context already exposes an existing code");
		article.Text.Should().Contain("blocker-level planning error",
			because: "the guide should treat duplicate supporting-schema creation as a hard planning failure");
		article.Text.Should().Contain("resolve the backing schema from refreshed app context",
			because: "the guide should require page/detail workflows to inspect the existing object model before planning schema creation");
		article.Text.Should().Contain("Do not create `UsrSupportCaseKnowledgeBase`",
			because: "the guide should include a concrete negative example for the Support Case reuse scenario");
		article.Text.Should().Contain("BaseLookup",
			because: "the guide should explain lookup inheritance and display-field behavior");
		article.Text.Should().Contain("schema default",
			because: "the guide should explain that seed rows alone do not satisfy default requirements");
		article.Text.Should().Contain("operations[*].type",
			because: "the guide should explicitly document the canonical sync-schemas request field");
		article.Text.Should().Contain("do not invent or send `operations[*].operation`",
			because: "the guide should explicitly reject the legacy request field name");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a canonical MCP guidance article for existing-app discovery inspection and minimal mutation workflows.")]
	public void ExistingAppMaintenanceGuidanceResource_Should_Return_Canonical_Maintenance_Guide() {
		// Arrange
		ExistingAppMaintenanceGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = result.Should().BeOfType<TextResourceContents>(
			because: "the maintenance guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/existing-app-maintenance",
			because: "the resource should expose a stable MCP URI for existing-app maintenance guidance");
		article.MimeType.Should().Be("text/plain",
			because: "the maintenance guide should be discoverable as plain text");
		article.Text.Should().Contain("list-apps",
			because: "the guide should start existing-app discovery from installed application lookup");
		article.Text.Should().Contain("get-app-info",
			because: "the guide should explain the follow-up app inspection step");
		article.Text.Should().Contain("create-app-section",
			because: "the guide should include the dedicated section-create mutation path for existing apps");
		article.Text.Should().Contain("update-app-section",
			because: "the guide should include the dedicated section-update mutation path for existing apps");
		article.Text.Should().Contain("`code`",
			because: "the guide should steer existing-app flows toward the canonical code selector");
		article.Text.Should().Contain("Resolve the backing schema from runtime app context before planning new schema work.",
			because: "existing-app page/detail requests should inspect runtime package context before deciding whether new schema work is needed");
		article.Text.Should().Contain("reuse that schema",
			because: "the maintenance guide should require reuse of an existing supporting or link schema when it already models the needed relation");
		article.Text.Should().Contain("page-only/object-model reuse tasks by default",
			because: "detail/grid requests over existing data should default to page mutation rather than schema creation");
		article.Text.Should().Contain("list-pages",
			because: "the guide should describe page discovery before inspection");
		article.Text.Should().Contain("get-page",
			because: "the guide should explain how callers inspect raw page bodies before editing");
		article.Text.Should().Contain("sync-pages",
			because: "the guide should advertise the canonical page write path");
		article.Text.Should().Contain("list-pages -> get-page -> sync-pages -> get-page",
			because: "the guide should state the canonical page workflow explicitly");
		article.Text.Should().Contain("get-component-info",
			because: "the guide should explain how callers inspect unfamiliar Freedom UI components");
		article.Text.Should().Contain("Usr*_label",
			because: "the guide should reserve custom Usr label resources for standalone UI only");
		article.Text.Should().Contain("get-entity-schema-properties",
			because: "the guide should include schema-level inspection before mutation");
		article.Text.Should().Contain("get-entity-schema-column-properties",
			because: "the guide should include column-level inspection before single-column mutation");
		article.Text.Should().Contain("modify-entity-schema-column",
			because: "the guide should identify the minimal single-column mutation path");
		article.Text.Should().Contain("sync-schemas",
			because: "the guide should explain when a larger ordered schema workflow is required");
		article.Text.Should().Contain("single-page dry-run or legacy save workflows",
			because: "the guide should keep update-page as a fallback-only path");
		article.Text.Should().Contain("client-side validation enabled by default",
			because: "the guide should explain the canonical validate semantics for sync-pages");
		article.Text.Should().Contain("default `false`",
			because: "the guide should explain that sync-pages verify stays optional by default");
		article.Text.Should().Contain("fallback-oriented tools",
			because: "the guide should explain which single-surface tools are compatibility paths when the preferred batched workflow is not appropriate");
		article.Text.Should().Contain("application-code",
			because: "the guide should spell out the canonical section-create selector");
		article.Text.Should().Contain("section-code",
			because: "the guide should spell out the canonical existing-section selector for updates");
		article.Text.Should().Contain("with-mobile-pages",
			because: "the guide should explain the explicit top-level mobile-page toggle for section creation");
		article.Text.Should().Contain("do not wrap MCP arguments inside `args`",
			because: "the guide should explicitly reject the synthetic args wrapper that caused real session failures");
		article.Text.Should().Contain("do not send `bundle` or `bundle.viewConfig` as the body payload",
			because: "the guide should explain the concrete page payload shape expected by the page tools");
		article.Text.Should().Contain("JSON object string",
			because: "the guide should explain the concrete resources payload shape expected by page tools");
		article.Text.Should().Contain("create-data-binding-db",
			because: "the guide should steer standalone lookup seeding back to MCP-native data-binding tools");
		article.Text.Should().Contain("Read before write, and read back after mutations",
			because: "the guide should set the canonical verification discipline for existing-app edits");
		article.Text.Should().Contain("Absence of a tab, detail, or grid on the page does not prove the backing entity is missing",
			because: "the guide should decouple missing UI from missing object model");
		article.Text.Should().Contain("Do not create `UsrSupportCaseKnowledgeBase`",
			because: "the guide should include the concrete negative example for the Support Case page-detail reuse path");
		article.Text.Should().Contain("operations[*].type",
			because: "the maintenance guide should explicitly document the canonical sync-schemas request field");
		article.Text.Should().Contain("operations[*].operation",
			because: "the maintenance guide should explicitly warn callers away from the legacy request field");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns handler guidance that keeps handler logic separate from validators and converters in clio MCP page editing.")]
	public void PageSchemaHandlersGuidanceResource_Should_Return_Canonical_Handler_Guide() {
		// Arrange
		PageSchemaHandlersGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = result.Should().BeOfType<TextResourceContents>(
			because: "the handler guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/page-schema-handlers",
			because: "the handler guide should expose a stable MCP URI for handler authoring guidance");
		article.Text.Should().Contain("SCHEMA_HANDLERS",
			because: "handler guidance should point callers to the marker-delimited handler section");
		article.Text.Should().Contain("you MUST read `page-schema-sdk-common` before touching `SCHEMA_DEPS`, `SCHEMA_ARGS`, SDK services, or raw service calls",
			because: "handler guidance should route sdk-backed and service-backed handler edits to the dedicated sdk common guide");
		article.Text.Should().Contain("you MUST read `page-schema-sdk-common` before touching `SCHEMA_DEPS`, `SCHEMA_ARGS`, SDK services, or raw service calls",
			because: "handler guidance should make sdk and service routing to the sdk common guide mandatory");
		article.Text.Should().Contain("Mandatory routing rule: when the handler requirement includes any data access, system setting read/write, process execution, model query, or backend/external service call",
			because: "handler guidance should force ai callers through the sdk common guide before choosing a data or service implementation pattern");
		article.Text.Should().Contain("JavaScript array section",
			because: "handler guidance should state the SCHEMA_HANDLERS container shape directly");
		article.Text.Should().Contain("Decision tree",
			because: "handler guidance should front-load the main branching decisions for AI consumers");
		article.Text.Should().Contain("Direct request decision table",
			because: "handler guidance should teach AI callers when direct request wiring is enough");
		article.Text.Should().Contain("This table covers direct triggers from page config.",
			because: "the guide should explicitly separate direct request triggers from handler interception scenarios");
		article.Text.Should().Contain("Request shape quick reference",
			because: "handler guidance should explicitly separate declarative page-config request shape from imperative runtime dispatch");
		article.Text.Should().Contain("| declarative page config | `request` + `params` |",
			because: "handler guidance should show the canonical declarative request shape");
		article.Text.Should().Contain("| imperative dispatch from handler code | `type` + flat payload fields + `$context` + usually `scopes` |",
			because: "handler guidance should show the canonical imperative request shape");
		article.Text.Should().Contain("API choice rules",
			because: "handler guidance should explain the page-body request-dispatch choice explicitly");
		article.Text.Should().Contain("| deployed page-body handler in `SCHEMA_HANDLERS` | `await request.$context.executeRequest(...)` |",
			because: "handler guidance should prefer executeRequest for deployed page-body handlers");
		article.Text.Should().Contain("Do NOT default to `sdk.HandlerChainService.instance.process(...)` in deployed page-body handlers; use `request.$context.executeRequest(...)` unless the task explicitly matches an advanced SDK pattern from `page-schema-sdk-common`.",
			because: "handler guidance should keep HandlerChainService out of the default page-body authoring path");
		article.Text.Should().Contain("Else if the handler must read or write data, syssettings, processes, or backend services, stop and read `page-schema-sdk-common` before authoring the handler body.",
			because: "the handler decision tree should have an explicit sdk common branch for data and service work");
		article.Text.Should().Contain("| open a related page or mini page | `crt.OpenPageRequest` | button/menu `clicked.request` | no |",
			because: "handler guidance should keep simple navigation on direct request wiring instead of custom handlers");
		article.Text.Should().Contain("| create a related record from current context | `crt.CreateRecordRequest` | button/menu `clicked.request` | no |",
			because: "handler guidance should keep simple create flows on direct request wiring instead of custom handlers");
		article.Text.Should().Contain("| cancel unsaved edits on the current page | `crt.CancelRecordChangesRequest` | button/menu `clicked.request` | no |",
			because: "handler guidance should cover the built-in cancel-edits trigger directly in the decision table");
		article.Text.Should().Contain("| delete the current or selected record | `crt.DeleteRecordRequest` | button/menu `clicked.request` | no |",
			because: "handler guidance should keep simple delete flows on direct request wiring instead of custom handlers");
		article.Text.Should().Contain("| launch a business process | `crt.RunBusinessProcessRequest` | button/menu `clicked.request` | no |",
			because: "handler guidance should keep simple process launches on direct request wiring instead of custom handlers");
		article.Text.Should().Contain("| compose an email from current context | `crt.CreateEmailRequest` | button/menu `clicked.request` | no |",
			because: "handler guidance should cover the built-in email-composition trigger directly in the decision table");
		article.Text.Should().Contain("| copy a prepared literal value to clipboard | `crt.CopyClipboardRequest` | button/menu `clicked.request` | no |",
			because: "handler guidance should cover the built-in literal clipboard trigger directly in the decision table");
		article.Text.Should().Contain("| copy a page attribute value to clipboard | `crt.CopyInputToClipboardRequest` | button/menu `clicked.request` | no |",
			because: "handler guidance should cover the built-in attribute clipboard trigger directly in the decision table");
		article.Text.Should().Contain("stop and read `page-schema-validators`",
			because: "handler guidance should redirect field validation requests to the dedicated validator guide");
		article.Text.Should().Contain("If the requirement is max/min/length/range/regex validation whose threshold comes from a system setting, SDK lookup, or other async read, it is still validator work.",
			because: "handler guidance should explicitly redirect syssetting-backed length validation away from handler-based UI property updates");
		article.Text.Should().Contain("use `SCHEMA_CONVERTERS`, not `SCHEMA_HANDLERS`",
			because: "handler guidance should redirect pure transform work to converters");
		article.Text.Should().Contain("await next?.handle(request)",
			because: "handler guidance should preserve the runtime handler chain explicitly");
		article.Text.Should().Contain("Call `next` intentionally: place it `before`, `after`, or omit it only for an intentional chain break or full behavior replacement.",
			because: "handler guidance should not contradict its own documented chain-break pattern with an exactly-once rule");
		article.Text.Should().Contain("request.$context",
			because: "handler guidance should explain the canonical runtime state access path");
		article.Text.Should().Contain("Chain-control rules",
			because: "handler guidance should explain how to place next in the chain instead of leaving AI callers to guess");
		article.Text.Should().Contain("| extend a built-in lifecycle flow and depend on the platform's default work (`init`, `resume`, `destroy`) | call `await next?.handle(request)` first | preserve base loading/binding behavior before custom follow-up logic |",
			because: "handler guidance should explicitly teach the safe next-first lifecycle pattern");
		article.Text.Should().Contain("| fully replace or intentionally stop the built-in behavior | do NOT call `next` | break the chain only on purpose |",
			because: "handler guidance should explicitly teach the intentional chain-break pattern");
		article.Text.Should().Contain("prefer `return next?.handle(request);` so the downstream result is preserved explicitly",
			because: "handler guidance should document the canonical pass-through branch pattern for non-matching handlers");
		article.Text.Should().Contain("Context read/write patterns",
			because: "handler guidance should teach AI callers how to read current view-model state, not only write it");
		article.Text.Should().Contain("Use `await request.$context[\"<AttributeName>\"]` to read any other page attribute in deployed page-body handlers.",
			because: "handler guidance should standardize read access on bracket syntax for deployed page-body handlers");
		article.Text.Should().Contain("Direct property assignment on `request.$context` is allowed only for transient runtime references such as subscriptions or service handles; page attributes still use `await request.$context.set(...)`.",
			because: "handler guidance should classify direct $context property writes as transient-runtime exceptions instead of a competing page-attribute write style");
		article.Text.Should().Contain("Prefer `request.value` over re-reading the triggering attribute through the view-model context.",
			because: "handler guidance should keep simple attribute mirroring on request.value instead of redundant reads");
		article.Text.Should().Contain("Do NOT use `request.sender`, `.$get(...)`, `.$set(...)`, or `request.$context.get(...)` in deployed page-body handlers.",
			because: "handler guidance should explicitly reject legacy sender/get/set access patterns");
		article.Text.Should().Contain("Do NOT choose raw `fetch(...)` to a platform endpoint before checking `page-schema-sdk-common` for a canonical `crt.*Request`, SDK service, or `sdk.Model` pattern.",
			because: "the handler anti-pattern list should stop callers from defaulting to raw platform fetches");
		article.Text.Should().Contain("A field control MUST bind to the same declared view-model attribute that handlers read or write through `$context.set(\"<AttributeName>\", ...)`.",
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
		article.Text.Should().Contain("\"name\": \"RoleDescription\"",
			because: "the handler guide should include a concrete inherited-control merge example");
		article.Text.Should().Contain("Compatibility note: existing product code may also use `request.$context.attributes[...]` or direct property assignment.",
			because: "handler guidance should acknowledge compatibility forms from product code without promoting them to the canonical AI-first pattern");
		article.Text.Should().Contain("const currentStatus = await request.$context[\"UsrStatus\"];",
			because: "handler guidance should include a concrete bracket-based attribute-read example");
		article.Text.Should().NotContain("Use `await request.$context.get(\"<AttributeName>\")`",
			because: "handler guidance should no longer teach the unsupported request.$context.get API as a supported read pattern");
		article.Text.Should().Contain("Minimal canonical templates",
			because: "handler guidance should provide reusable page-body templates");
		article.Text.Should().Contain("if (attributeName !== \"<AttributeName>\") {",
			because: "the attribute-change template should use strict inequality in its guard");
		article.Text.Should().Contain("request: \"crt.HandleViewModelInitRequest\"",
			because: "handler guidance should include the lifecycle init request example");
		article.Text.Should().Contain("request: \"crt.HandleViewModelAttributeChangeRequest\"",
			because: "handler guidance should include the attribute-change request example");
		article.Text.Should().Contain("request.preventAttributeChangeRequest = true;",
			because: "handler guidance should show the documented attribute-change short-circuit flag in context");
		article.Text.Should().Contain("return; // intentional chain break: block downstream handlers and business rules",
			because: "handler guidance should explain why the blocking attribute-change branch intentionally omits next");
		article.Text.Should().Contain("const currentMode = await $context[\"<ModeAttribute>\"];",
			because: "handler guidance should use bracket-based reads in the attribute-change template");
		article.Text.Should().Contain("const result = await next?.handle(request);",
			because: "templates that add post-success side effects should capture the downstream result explicitly");
		article.Text.Should().Contain("return result;",
			because: "templates that capture the downstream result should also return it explicitly");
		article.Text.Should().Contain("Mirror one text field into another on attribute change",
			because: "handler guidance should include the canonical copy-text scenario that motivated the API clarification");
		article.Text.Should().Contain("await request.$context.set(\"UsrCopyTextField\", request.value);",
			because: "handler guidance should teach the simplest supported mirror-on-change pattern");
		article.Text.Should().Contain("Sync one field into another only when the target value actually differs",
			because: "handler guidance should include a guarded sync template that avoids unnecessary writes");
		article.Text.Should().Contain("const value = request.value;",
			because: "the guarded sync template should read the changed value directly from request.value");
		article.Text.Should().Contain("const targetValue = await request.$context[\"<TargetAttribute>\"];",
			because: "the guarded sync template should read the dependent attribute through the canonical bracket-based pattern");
		article.Text.Should().Contain("if (value !== undefined && targetValue !== value) {",
			because: "the guarded sync template should show the equality guard explicitly");
		article.Text.Should().Contain("await request.$context.set(\"<TargetAttribute>\", value);",
			because: "the guarded sync template should write through the canonical setter API");
		article.Text.Should().Contain("request: \"crt.SaveRecordRequest\"",
			because: "handler guidance should include a canonical save handler example that captures and returns the downstream result");
		article.Text.Should().Contain("const saveResult = await next?.handle(request);",
			because: "handler guidance should show the call-next-in-the-middle pattern from handler chain examples");
		article.Text.Should().Contain("return saveResult;",
			because: "handler guidance should preserve the downstream save result when custom logic runs after save");
		article.Text.Should().Contain("Subscription lifecycle across init/resume/pause/destroy",
			because: "handler guidance should include a concrete lifecycle template for subscriptions and cleanup");
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
		article.Text.Should().Contain("Multiple handlers in one page-body array",
			because: "handler guidance should show how to combine several handler entries in the same array");
		article.Text.Should().MatchRegex(@"if \(request\.attributeName !== ""UsrStatus""\)\s*\{\s*return next\?\.handle\(request\);\s*\}\s*const result = await next\?\.handle\(request\);\s*await request\.\$context\.set\(""UsrStatusChanged"", true\);\s*return result;",
			because: "the multiple-handlers attribute-change example should preserve and return the downstream result consistently with the canonical post-next templates");
		article.Text.Should().Contain("Prefer a stable array order: lifecycle handlers first, attribute-change handlers next, and custom domain/action handlers after them.",
			because: "handler guidance should give AI a predictable ordering heuristic for multi-handler arrays");
		article.Text.Should().Contain("request: \"usr.RunCustomActionRequest\"",
			because: "handler guidance should keep the custom action example inside the multi-handler array example");
		article.Text.Should().Contain("Use this pattern only when the button starts a multi-step domain workflow that is not a single built-in `crt.*Request`.",
			because: "the custom action example should state when a usr.* request is actually justified");
		article.Text.Should().MatchRegex(@"(?s)request: ""usr\.RunCustomActionRequest"".*?type: ""crt\.RunBusinessProcessRequest"".*?const result = await next\?\.handle\(request\);\s*return result;",
			because: "custom action templates should preserve and return the downstream result instead of introducing a second non-returning next pattern");
		article.Text.Should().Contain("Orchestration patterns",
			because: "handler guidance should distinguish page-body dispatch from direct SDK service orchestration");
		article.Text.Should().Contain("Use `await request.$context.executeRequest(...)` when a deployed page-body handler forwards into another page-scoped request.",
			because: "the handler guide should keep executeRequest as the default page-body orchestration path");
		article.Text.Should().Contain("Use SDK/domain services such as `sdk.ProcessEngineService` when the task is direct service orchestration rather than request forwarding.",
			because: "the handler guide should mention the direct service-orchestration path seen in product code");
		article.Text.Should().MatchRegex(@"type: ""crt\.RunBusinessProcessRequest"",\s+processName: ""<ProcessName>"",\s+\$context(: request\.\$context|),\s+scopes: \[\.\.\.request\.scopes\]",
			because: "handler guidance should keep scopes in the canonical RunBusinessProcessRequest forwarding examples");
		article.Text.Should().Contain("Standard built-in handler catalog",
			because: "handler guidance should include an explicit built-in handler catalog instead of only narrative recommendations");
		article.Text.Should().Contain("crt.HandleViewModelResumeRequest",
			because: "handler guidance should carry over the built-in lifecycle handler list");
		article.Text.Should().Contain("crt.HandleViewModelPauseRequest",
			because: "handler guidance should carry over the built-in lifecycle handler list");
		article.Text.Should().Contain("Use `crt.HandleViewModelResumeRequest` when the page must restore runtime subscriptions or reinitialize transient state after returning to the page.",
			because: "the handler guide should give AI a concrete resume-use selection rule");
		article.Text.Should().Contain("Use `crt.HandleViewModelPauseRequest` when the page must stop temporary runtime work before the page is resumed or destroyed.",
			because: "the handler guide should give AI a concrete pause-use selection rule");
		article.Text.Should().Contain("Use `crt.CancelRecordChangesRequest` when the page must discard unsaved edits and return to the clean state.",
			because: "the handler guide should include the built-in cancel-edits request in the selection hints");
		article.Text.Should().Contain("crt.DeleteRecordRequest",
			because: "handler guidance should carry over the built-in delete request entry");
		article.Text.Should().Contain("crt.LoadDataRequest",
			because: "handler guidance should carry over the built-in load-data request entry");
		article.Text.Should().Contain("crt.CreateEmailRequest",
			because: "handler guidance should carry over the built-in create-email request entry");
		article.Text.Should().Contain("crt.CopyClipboardRequest",
			because: "handler guidance should carry over the built-in clipboard request entry");
		article.Text.Should().Contain("crt.CopyInputToClipboardRequest",
			because: "handler guidance should carry over the built-in copy-input request entry");
		article.Text.Should().Contain("crt.GetSidebarStateRequest",
			because: "handler guidance should carry over the built-in sidebar entries");
		article.Text.Should().Contain("Standard handler parameter catalog",
			because: "handler guidance should expose concrete payload contracts, not only the built-in request name list");
		article.Text.Should().Contain("| Request | Kind | Params for AI authoring | Notes |",
			because: "handler guidance should optimize the standard request catalog into a compact AI-readable table");
		article.Text.Should().Contain("| `crt.CreateRecordRequest` | config | `entityName?`, `defaultValues?`, `itemsAttributeName?`, `entityPageName?`, `skipUnsavedData?` | create page/record flow |",
			because: "handler guidance should keep the create-record request in the parameter catalog");
		article.Text.Should().Contain("| `crt.OpenPageRequest` | config | `schemaName` required, `packageUId?`, `modelInitConfigs?`, `parameters?`, `skipUnsavedData?` | standard open-page request |",
			because: "handler guidance should keep the open-page request in the parameter catalog");
		article.Text.Should().Contain("| `crt.LoadDataRequest` | config | `dataSourceName`, `config` (commonly `loadType`, `useLastLoadParameters?`), `showSuccessMessage?` | reload or refresh a page/list data source |",
			because: "handler guidance should expose a compact load-data request contract instead of only naming the request");
		article.Text.Should().Contain("| `crt.DeleteRecordRequest` | config | `recordId`, `itemsAttributeName` | delete one record; source handler converts it into `crt.DeleteRecordsRequest` |",
			because: "handler guidance should expose the source-backed delete-record request fields");
		article.Text.Should().Contain("| `crt.CancelRecordChangesRequest` | config | `none` | cancel edits |",
			because: "handler guidance should expose the cancel-edits request contract");
		article.Text.Should().Contain("| `crt.RunBusinessProcessRequest` | config | `processName` required, `processParameters`, `recordIdProcessParameterName?`, `resultParameterNames?`, `processRunType?`, `dataSourceName?`, `filters?`, `sorting?`, `parameterMappings?`, `showNotification?`, `notificationText?`, `selectionStateAttributeName?`, `saveAtProcessStart?` | `processRunType`: `ForTheSelectedPage`, `RegardlessOfThePage`, `ForTheSelectedRecords` |",
			because: "handler guidance should keep the process request in the parameter catalog");
		article.Text.Should().Contain("| `crt.CreateEmailRequest` | config | `recordId?`, `bindingColumns?` | compose an email from current context |",
			because: "handler guidance should expose the create-email request contract");
		article.Text.Should().Contain("| `crt.CopyClipboardRequest` | config | `value` required | copy a prepared literal value |",
			because: "handler guidance should expose the literal clipboard request contract");
		article.Text.Should().Contain("| `crt.CopyInputToClipboardRequest` | config | `attribute` required, `successMessageArea?` | copy the value of a page attribute |",
			because: "handler guidance should expose the page-attribute clipboard request contract");
		article.Text.Should().Contain("| Request | Kind | Params visible in handler | Notes |",
			because: "handler guidance should optimize runtime lifecycle payloads into a compact AI-readable table");
		article.Text.Should().Contain("| `crt.HandleViewModelAttributeChangeRequest` | runtime | `attributeName`, `value`, `oldValue`, `silent` (deprecated), `preventAttributeChangeRequest`, `preventStateChange`, `preventRunBusinessRules` | author handlers against these runtime fields |",
			because: "handler guidance should distinguish runtime attribute-change payload fields from authorable config");
		article.Text.Should().Contain("| Request | Kind | Params | Notes |",
			because: "handler guidance should optimize sidebar contracts into a compact AI-readable table");
		article.Text.Should().Contain("| User-visible name | Source reality | Params | Notes |",
			because: "handler guidance should isolate user-visible and source-runtime mismatches in a dedicated AI-readable table");
		article.Text.Should().Contain("| `crt.ShowDialog` | source request is `crt.ShowDialogRequest`, handled by `crt.ShowDialogHandler` | `dialogConfig` with `message`, `actions`, optional `title` | in code author `type: \"crt.ShowDialogRequest\"`; `crt.ShowDialog` is the user-visible catalog label |",
			because: "handler guidance should disambiguate the user-visible dialog label from the source request type with an explicit authoring rule");
		article.Text.Should().Contain("Minimal `dialogConfig` shape:",
			because: "handler guidance should show a concrete minimal dialog payload instead of only naming the config field");
		article.Text.Should().Contain("type: \"crt.ShowDialogRequest\"",
			because: "handler guidance should show the actual request type inside the minimal dialog example");
		article.Text.Should().Contain("Anti-patterns",
			because: "handler guidance should call out incorrect handler shapes and request usage explicitly");
		article.Text.Should().Contain("Preserve the exact `/**SCHEMA_HANDLERS*/` comment markers around the handlers array;",
			because: "safe editing rules should explain that clio depends on the marker comments to find the editable section");
		article.Text.Should().Contain("This guide is only for deployed page-body handlers inside the schema body returned by `get-page`.",
			because: "safe editing rules should explicitly scope the guide to page-body handler authoring only");
		article.Text.Should().Contain("Do NOT write `handlers: { ... }`; `handlers` must remain an array.",
			because: "handler guidance should prohibit object-shaped handlers sections explicitly");
		article.Text.Should().Contain("Do NOT invent placeholder SDK services such as `<Service>.subscribe(...)`; when SDK-based subscriptions are required, use a concrete service such as `sdk.MessageChannelService` and keep `SCHEMA_DEPS` / `SCHEMA_ARGS` aligned.",
			because: "handler guidance should stop AI from inventing placeholder subscription services in schema code");
		article.Text.Should().Contain("Do NOT write `type: \"crt.ShowDialog\"` in imperative request code; use `type: \"crt.ShowDialogRequest\"`.",
			because: "handler guidance should explicitly forbid the user-visible show-dialog label in imperative request code");
		article.Text.Should().Contain("BEFORE SAVE CHECKLIST",
			because: "handler guidance should give AI callers a compact verification gate before sync-pages");
		article.Text.Should().Contain("Are the exact `/**SCHEMA_HANDLERS*/` markers still present around the handlers array?",
			because: "the checklist should verify the section markers explicitly");
		article.Text.Should().Contain("Is `SCHEMA_HANDLERS` still a JavaScript array section?",
			because: "handler guidance should express the save checklist as binary self-validation questions");
		article.Text.Should().Contain("Is every handler entry still an object with string `request` and `handler` fields?",
			because: "the checklist should verify the lightweight handler-shape contract explicitly");
		article.Text.Should().Contain("Do attribute-name guards use strict equality / inequality (`===` / `!==`) unless coercion is intentional?",
			because: "the checklist should explicitly guard against sloppy equality in generated handlers");
		article.Text.Should().Contain("Are `$context` and `scopes` forwarded in every imperative follow-up request that stays on the live page scope?",
			because: "the checklist should verify imperative forwarding consistency explicitly");
		article.Text.Should().Contain("Does page-state writeback use `await request.$context.set(...)` unless the task explicitly matches a compatibility pattern already present in the schema?",
			because: "the checklist should reinforce the canonical writeback pattern explicitly");
		article.Text.Should().Contain("Is this edit still using the canonical page-body API (`request.value`, `await request.$context[\"Attr\"]`, `await request.$context.set(...)`) rather than a compatibility form?",
			because: "the checklist should force AI to confirm that it stayed on the canonical page-body API instead of drifting to compatibility patterns");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns sdk common guidance that teaches AI callers how to use @creatio-devkit/common in page schemas.")]
	public void PageSchemaSdkCommonGuidanceResource_Should_Return_Canonical_Sdk_Common_Guide() {
		// Arrange
		PageSchemaSdkCommonGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = result.Should().BeOfType<TextResourceContents>(
			because: "the sdk common guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/page-schema-sdk-common",
			because: "the sdk common guide should expose a stable MCP URI for page-schema SDK guidance");
		article.Text.Should().Contain("clio MCP page-schema sdk common guide",
			because: "the resource should identify itself as the dedicated sdk common guide");
		article.Text.Should().Contain("Use this guide only for deployed page schema code",
			because: "the sdk common guide should clearly scope itself to schema-body authoring");
		article.Text.Should().Contain("Do NOT use this guide for remote modules or frontend-source classes.",
			because: "the sdk common guide should explicitly reject non-schema contexts");
		article.Text.Should().Contain("Prefer built-in `crt.*` requests first.",
			because: "the sdk common guide should stop AI from reaching for SDK imports when built-in requests are sufficient");
		article.Text.Should().Contain("Pattern selection order for handler-side data/service work is mandatory",
			because: "the sdk common guide should make the routing order explicit for handler-side data and service work");
		article.Text.Should().Contain("Canonical dependency pattern",
			because: "the guide should start from the AMD dependency contract that page schemas must preserve");
		article.Text.Should().Contain("function/**SCHEMA_ARGS*/(sdk)/**SCHEMA_ARGS*/",
			because: "the guide should show the canonical SCHEMA_ARGS pattern for sdk usage");
		article.Text.Should().Contain("Reuse the live alias already present in the schema body",
			because: "the guide should not encourage gratuitous alias renames");
		article.Text.Should().Contain("| read or update syssettings | `new sdk.SysSettingsService()` | `getByCode(...)`, `getByCodes(...)`, `update(...)`, `updateMany(...)` |",
			because: "the guide should include the public syssettings service contract seen in product code");
		article.Text.Should().Contain("| read a feature flag | `new sdk.FeatureService()` | `getFeatureState(...)` |",
			because: "the guide should include the public feature-service contract seen in product code");
		article.Text.Should().Contain("| call an external endpoint | `new sdk.HttpClientService()` | `get(...)`, `post(...)`, `put(...)`, `delete(...)` |",
			because: "the guide should include the public http-client service contract seen in product code");
		article.Text.Should().Contain("| check operation rights | `new sdk.RightsService()` | `getCanExecuteOperation(...)` |",
			because: "the guide should include the public rights-service contract seen in product code");
		article.Text.Should().Contain("| subscribe or send channel events | `new sdk.MessageChannelService()` | `subscribe(...)`, `sendMessage(...)`, `unsubscribe()` |",
			because: "the guide should include the public message-channel service contract seen in product code");
		article.Text.Should().Contain("| run or continue a process directly | `new sdk.ProcessEngineService()` | `executeProcessByName(...)`, `completeExecuting(...)` |",
			because: "the guide should include the public process-engine service contract seen in product code");
		article.Text.Should().Contain("| query Creatio data | `await sdk.Model.create(...)`, `new sdk.FilterGroup()` | `load(...)`, `ComparisonType`, `ModelParameterType` |",
			because: "the guide should include the public model and filter helpers seen in product code");
		article.Text.Should().Contain("const featureEnabled = await new sdk.FeatureService().getFeatureState(\"UsrFeatureCode\");",
			because: "the guide should include a minimal feature-service example instead of only naming the service");
		article.Text.Should().Contain("const response = await new sdk.HttpClientService().get(\"<Url>\");",
			because: "the guide should include a minimal http-client example instead of only naming the service");
		article.Text.Should().Contain("const canExecute = await new sdk.RightsService().getCanExecuteOperation(\"CanManageUsers\");",
			because: "the guide should include a minimal rights-service example instead of only naming the service");
		article.Text.Should().Contain("1. built-in `crt.*Request`, 2. public SDK service or `sdk.Model`, 3. raw `fetch(...)` only when the scenario is custom/external or no canonical request/SDK pattern covers it.",
			because: "the guide should explicitly order request sdk and fetch choices");
		article.Text.Should().Contain("| read or update syssettings | `new sdk.SysSettingsService()` | raw `fetch(...)` only when the target is not covered by the public syssettings API |",
			because: "the guide should define syssettings fetch as a narrow fallback rather than the default");
		article.Text.Should().Contain("| custom data query or mutation | `await sdk.Model.create(...)` | raw `fetch(...)` only when the data source is not accessible through page requests or `sdk.Model` |",
			because: "the guide should define model queries as the preferred data-access path before fetch");
		article.Text.Should().Contain("| platform endpoint with known page or sdk pattern | canonical `crt.*Request`, SDK service, or `sdk.Model` | do NOT jump to raw `fetch(...)` first |",
			because: "the guide should explicitly block premature fetch usage for known platform scenarios");
		article.Text.Should().Contain("`SysSettingsService.getByCode(...)` commonly returns an object with fields such as `value` and `displayValue`.",
			because: "the guide should explain the return shape that otherwise appears inconsistent across templates");
		article.Text.Should().Contain("Use `.value` for raw or numeric comparisons and validator thresholds.",
			because: "the guide should standardize the numeric-return usage of syssettings values");
		article.Text.Should().Contain("Use `.displayValue` only when the page attribute should receive the display text shown to the user.",
			because: "the guide should standardize the display-value usage of syssettings values");
		article.Text.Should().Contain("| `sdk.MessageChannelType` | choose channel kind for `MessageChannelService.sendMessage(...)` |",
			because: "the guide should expose the base-derived message-channel enum through the sdk contract instead of leaving it implicit");
		article.Text.Should().Contain("| `sdk.FilterGroup` | build model-query filters for `sdk.Model.load(...)` |",
			because: "the guide should expose the base-derived filter helper that appears in real schema model queries");
		article.Text.Should().Contain("| `sdk.ComparisonType` | specify comparison operators inside `FilterGroup` |",
			because: "the guide should expose the base-derived comparison enum used by filter-building helpers");
		article.Text.Should().Contain("| `sdk.ModelParameterType` | declare parameter kinds such as `Filter` in `model.load(...)` |",
			because: "the guide should expose the base-derived model-parameter enum used by schema model loads");
		article.Text.Should().Contain("| work with collection attributes | `const collection = await request.$context[\"Items\"]` | `createItem(...)`, `registerOnCollectionChangeCallback(...)`, `registerOnItemAttributesChangesCallback(...)`, `sdk.ViewModelCollectionActionType` |",
			because: "the guide should expose the collection-oriented sdk surface used in schema body code");
		article.Text.Should().Contain("| `sdk.ViewModelCollectionActionType` | filter collection change callbacks by action such as `Add` or `Remove` |",
			because: "the guide should expose the collection action enum through the sdk contract");
		article.Text.Should().Contain("| low-level request-chain dispatch in SDK-oriented schema code | `sdk.HandlerChainService.instance.process(...)` | `process({ type, $context, scopes })` |",
			because: "the guide should keep HandlerChainService available only as an advanced schema-body pattern");
		article.Text.Should().NotContain("BaseRequest",
			because: "the schema-only sdk common guide should not teach frontend-source request classes");
		article.Text.Should().NotContain("@CrtValidator",
			because: "the schema-only sdk common guide should not teach decorator-based frontend-source validators");
		article.Text.Should().Contain("const sysSettingsService = new sdk.SysSettingsService();",
			because: "the guide should provide a copyable syssettings example for page-body handlers");
		article.Text.Should().Contain("const result = await next?.handle(request);",
			because: "handler-shaped sdk examples should align with the canonical pattern of capturing and returning the downstream result");
		article.Text.Should().Contain("return result;",
			because: "handler-shaped sdk examples should return the captured downstream result after sdk side effects");
		article.Text.Should().Contain("const processService = new sdk.ProcessEngineService();",
			because: "the guide should provide a copyable process-service example for page-body handlers");
		article.Text.Should().Contain("await sdk.Model.create(\"SysProcessElementLog\")",
			because: "the guide should provide a copyable model query example");
		article.Text.Should().Contain("define(\"UsrSome_FormPage\", /**SCHEMA_DEPS*/[\"@creatio-devkit/common\"]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/(sdk)/**SCHEMA_ARGS*/ {",
			because: "the message-channel template should stay inside a full schema-body AMD wrapper");
		article.Text.Should().Contain("request.$context.subscription = await messageChannelService.subscribe(\"TestPTP\", async event => {",
			because: "the guide should keep the subscription callback async so schema-body setter failures are not dropped");
		article.Text.Should().Contain("// transient runtime handle, not a page attribute",
			because: "the guide should mark direct $context property assignment as a transient-state exception inside sdk lifecycle templates");
		article.Text.Should().Contain("await request.$context.set(\"UsrIncomingMessage\", event.body);",
			because: "the guide should keep page attribute writes on the canonical awaited setter API inside sdk-backed callbacks");
		article.Text.Should().Contain("await messageChannelService.sendMessage(\"TestPTP\", \"Hello\", sdk.MessageChannelType.PTP);",
			because: "the guide should not teach fire-and-forget channel publishing");
		article.Text.Should().Contain("`PTP` is an example; keep the local channel type pattern already used by the schema.",
			because: "the guide should explain that the sample channel type must match the existing local pattern");
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
			because: "the guide should provide a copyable dialog-service example");
		article.Text.Should().Contain("Rule: if a handler is already dispatching requests, do NOT use `DialogService`; use `crt.ShowDialogRequest`.",
			because: "the guide should make the dialog stop-rule explicit for request-dispatching handlers");
		article.Text.Should().Contain("return await sdk.HandlerChainService.instance.process({",
			because: "the guide should provide a copyable HandlerChainService example for SDK-oriented code");
		article.Text.Should().Contain("Inner handler/body snippet only: ProcessEngineService with model query helpers:",
			because: "fragment-only sdk snippets should say they are not standalone schema modules");
		article.Text.Should().Contain("Inner handler/body snippet only: DialogService from SDK code:",
			because: "fragment-only dialog snippets should say they are not standalone schema modules");
		article.Text.Should().Contain("Inner handler/body snippet only: HandlerChainService from advanced SDK-oriented schema code:",
			because: "fragment-only handler-chain snippets should say they are not standalone schema modules");
		article.Text.Should().Contain("Rule: in deployed page-body handlers, prefer `await request.$context.executeRequest(...)`.",
			because: "the guide should keep executeRequest as the default dispatch path for schema handlers");
		article.Text.Should().Contain("Do NOT use `import { ... } from \"@creatio-devkit/common\"` inside deployed page schema body code.",
			because: "the guide should explicitly block ES-module imports inside schema-body code");
		article.Text.Should().Contain("Do NOT use decorator/class-based frontend-source APIs inside deployed page schema body code.",
			because: "the guide should explicitly block frontend-source-only authoring patterns inside schema-body code");
		article.Text.Should().Contain("Do NOT use internal `ɵ*` exports from the package.",
			because: "the guide should explicitly block internal API usage for AI consumers");
		article.Text.Should().Contain("Do NOT call a standard platform endpoint with raw `fetch(...)` before checking whether a built-in `crt.*Request`, public SDK service, or `sdk.Model` pattern already covers the scenario.",
			because: "the guide should stop callers from defaulting to raw platform fetches");
		article.Text.Should().Contain("Do NOT treat raw `fetch(...)` as the default for `SysSettingsService`, model-style data access, or standard process/page actions",
			because: "the guide should classify fetch as fallback-only for standard platform cases");
		article.Text.Should().Contain("Do NOT fire-and-forget SDK promises with `void somePromise`, `.then()` without error handling, or any other pattern that drops failures silently in schema-body code.",
			because: "the guide should explicitly reject silent promise loss in schema-body SDK snippets");
		article.Text.Should().Contain("Do NOT invent collection helper APIs beyond `createItem(...)`, `registerOnCollectionChangeCallback(...)`, `registerOnItemAttributesChangesCallback(...)`, and their matching unregister methods shown here.",
			because: "the guide should keep AI from inventing unsupported collection helper APIs");
		article.Text.Should().Contain("Existing product code may still use `request.$context.attributes[...]` or direct property assignment.",
			because: "the guide should classify compatibility-only forms without making them canonical");
		article.Text.Should().Contain("Does the edited page body really need `@creatio-devkit/common`?",
			because: "the guide should end with a compact self-check for unnecessary SDK imports");
		article.Text.Should().Contain("Is this edit still inside deployed page schema body code, not a remote module?",
			because: "the checklist should explicitly keep AI in the intended schema-only context");
		article.Text.Should().Contain("Do `SCHEMA_DEPS` and `SCHEMA_ARGS` still have the same number of entries in the same order?",
			because: "the checklist should define what aligned means for AMD dependencies and aliases");
		article.Text.Should().Contain("Is every async SDK call awaited or explicitly handled with error flow instead of `void` / bare `.then()`?",
			because: "the checklist should make dropped SDK promises a concrete verification item");
		article.Text.Should().Contain("If `SysSettingsService.getByCode(...)` is used, is `.value` vs `.displayValue` chosen intentionally for this attribute or validation rule?",
			because: "the checklist should force an intentional choice for the syssettings return shape");
		article.Text.Should().Contain("If the task touches data access, system settings, processes, or backend services, was the implementation choice made in the required order: `crt.*Request` -> SDK service / `sdk.Model` -> raw `fetch(...)` only as justified fallback?",
			because: "the checklist should force an explicit routing decision for data and service scenarios");
		article.Text.Should().Contain("If raw `fetch(...)` is used, is the reason explicit and limited to a custom/external endpoint or a confirmed gap in the canonical request/SDK patterns?",
			because: "the checklist should require explicit fetch justification");
		article.Text.Should().Contain("Are all shown snippets still valid inside deployed schema body code, not standalone TypeScript/module code?",
			because: "the checklist should guard against copying inner snippets as standalone modules");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns validator guidance that keeps field validation separate from converters and handlers in clio MCP page editing.")]
	public void PageSchemaValidatorsGuidanceResource_Should_Return_Canonical_Validator_Guide() {
		// Arrange
		PageSchemaValidatorsGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = result.Should().BeOfType<TextResourceContents>(
			because: "the validator guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/page-schema-validators",
			because: "the validator guide should expose a stable MCP URI for validator authoring guidance");
		article.Text.Should().Contain("SCHEMA_VALIDATORS",
			because: "validator guidance should point callers to the marker-delimited validator section");
		article.Text.Should().Contain("also read `page-schema-sdk-common` before touching `SCHEMA_DEPS`, `SCHEMA_ARGS`, or SDK service calls",
			because: "validator guidance should route SDK-backed validator edits to the dedicated sdk common guide");
		article.Text.Should().Contain("must contain an object section, not an array section",
			because: "validator guidance should state the SCHEMA_VALIDATORS container shape directly instead of using ambiguous preserve wording");
		article.Text.Should().Contain("field-value validation",
			because: "validator guidance should define the intended responsibility of validators");
		article.Text.Should().Contain("Implement field validation as a validator entry, not as logic inside a handler.",
			because: "non-negotiables should frame validator responsibility in a direct AI-readable way");
		article.Text.Should().Contain("Decision tree",
			because: "validator guidance should front-load the main branching decisions for AI consumers");
		article.Text.Should().Contain("Standard validator decision table",
			because: "validator guidance should teach AI callers to select built-in validators from a compact decision table before creating custom ones");
		article.Text.Should().Contain("Do NOT create a custom validator when a standard validator is sufficient",
			because: "validator guidance should explicitly prevent unnecessary custom validator creation");
		article.Text.Should().Contain("| Requirement pattern | Prefer | Parameters | Custom validator needed |",
			because: "validator guidance should expose an AI-friendly decision table instead of only a narrative list");
		article.Text.Should().Contain("| field must be filled | `crt.Required` | none | no |",
			because: "validator guidance should map the required-field pattern directly to the built-in validator");
		article.Text.Should().Contain("| field must not be whitespace-only | `crt.EmptyOrWhiteSpace` | none | no |",
			because: "validator guidance should map whitespace-only rejection directly to the built-in validator");
		article.Text.Should().Contain("| minimum string length | `crt.MinLength` | `minLength` | no |",
			because: "validator guidance should map minimum string length directly to the built-in validator");
		article.Text.Should().Contain("| maximum string length | `crt.MaxLength` | `maxLength` | no |",
			because: "validator guidance should map maximum string length directly to the built-in validator");
		article.Text.Should().Contain("| minimum numeric value | `crt.Min` | `min` | no |",
			because: "validator guidance should map minimum numeric value directly to the built-in validator");
		article.Text.Should().Contain("| maximum numeric value | `crt.Max` | `max` | no |",
			because: "validator guidance should map maximum numeric value directly to the built-in validator");
		article.Text.Should().Contain("| requirement is not covered by rows above | custom `usr.*Validator` | custom | yes |",
			because: "validator guidance should explicitly tell AI callers when a custom validator is actually justified");
		article.Text.Should().Contain("crt.Required",
			because: "validator guidance should include the Academy base validator list");
		article.Text.Should().Contain("crt.MinLength",
			because: "validator guidance should include the Academy base validator list");
		article.Text.Should().Contain("crt.MaxLength",
			because: "validator guidance should include the Academy base validator list");
		article.Text.Should().Contain("crt.Min",
			because: "validator guidance should include the Academy base validator list");
		article.Text.Should().Contain("crt.Max",
			because: "validator guidance should include the Academy base validator list");
		article.Text.Should().Contain("crt.EmptyOrWhiteSpace",
			because: "validator guidance should include the Academy base validator list");
		article.Text.Should().Contain("not covered by `crt.Required`, `crt.EmptyOrWhiteSpace`, `crt.MinLength`, `crt.MaxLength`, `crt.Min`, or `crt.Max`",
			because: "validator guidance should define the exact fallback boundary for creating a custom validator");
		article.Text.Should().Contain("NON-NEGOTIABLES",
			because: "validator guidance should expose a compact high-priority rules block before long examples");
		article.Text.Should().Contain("BEFORE SAVE CHECKLIST",
			because: "validator guidance should give AI callers a compact verification gate before sync-pages");
		article.Text.Should().Contain("Name mapping",
			because: "validator guidance should explain alias, type, and error-key roles together");
		article.Text.Should().Contain("Minimal canonical template",
			because: "validator guidance should provide one compact reusable golden path before detailed variants");
		article.Text.Should().Contain("viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[",
			because: "the minimal validator template should include the control-binding section so AI callers do not omit the required UI binding");
		article.Text.Should().Contain("\"control\": \"$<AttrName>\"",
			because: "the minimal validator template should explicitly bind the control to the view-model attribute");
		article.Text.Should().Contain("const isValid = <isValidCondition>;",
			because: "the minimal validator template should show a decision point instead of an always-fail placeholder body");
		article.Text.Should().Contain("usr.UpperCaseValidator",
			because: "validator guidance should include a concrete page-body validator example");
		article.Text.Should().Contain("\"validator\": function",
			because: "validator guidance should show the runtime page-body validator function shape");
		article.Text.Should().Contain("@CrtValidator",
			because: "validator guidance should document the public frontend-source registration pattern");
		article.Text.Should().Contain("BaseValidator",
			because: "validator guidance should point to the main public validator base type in the devkit API");
		article.Text.Should().Contain("setAttributePropertyValue(...)",
			because: "validator guidance should redirect dynamic UI state rules to the supported handler-side API");
		article.Text.Should().Contain("Static `viewModelConfig` variant",
			because: "the guide should include a dedicated static viewModelConfig branch so AI callers detect format before editing");
		article.Text.Should().Contain("Treat the binding-location, control-binding, resource-string, in-place-fix, and async CRITICAL sections below as hard requirements.",
			because: "the compact non-negotiables block should now point AI callers to the full detailed critical rule set including async behavior");
		article.Text.Should().Contain(@"Wrong: validators are on `UsrNameForValidation`, but the control still uses `""control"": ""$UsrName""`",
			because: "the guide should call out the wrong control binding when validators were moved to a different declared attribute");
		article.Text.Should().Contain("The control MUST bind to that attribute — use the same declared attribute for both the control and the validators.",
			because: "the guide should explain that validator correctness depends on control and validator ownership matching on the same declared attribute");
		article.Text.Should().Contain("Fix control binding in the original operation, never add a patch merge",
			because: "the guide should explicitly forbid the anti-pattern of adding a second merge operation to patch a wrong control binding");
		article.Text.Should().Contain("When the control is inherited from a parent schema and is absent from the current schema's `viewConfigDiff`, add one local `merge` operation for that inherited control name.",
			because: "the guide should allow the first local merge for inherited controls");
		article.Text.Should().Contain("NEVER add a second local `merge` operation with the same `name` when the current schema already has a local operation for that control.",
			because: "the guide should call out duplicate local merges as the rejected case");
		article.Text.Should().Contain("Correct — inherited control from parent schema, so add the first local merge:",
			because: "the guide should show the canonical inherited-control override pattern");
		article.Text.Should().Contain("#ResourceString(UsrUpperCaseValidator_Message)#",
			because: "validator params must use #ResourceString()# macro format, not $Resources.Strings reactive binding syntax");
		article.Text.Should().Contain("NOT evaluated in validator params",
			because: "the guide should explicitly warn that $Resources.Strings syntax does not work in validator params");
		article.Text.Should().Contain(@"Set `""async"": true` ONLY when the inner function actually",
			because: "the guide should clarify that async:true is only for validators that actually await something");
		article.Text.Should().Contain("`async function` keyword alone does NOT require",
			because: "the guide should explicitly warn that the async keyword alone does not mean async:true is required");
		article.Text.Should().Contain("Async validator template",
			because: "the async validator section should use a direct AI-readable title instead of an ambiguous delta label");
		article.Text.Should().Contain("Before editing `SCHEMA_DEPS`, `SCHEMA_ARGS`, or SDK service usage here, read `page-schema-sdk-common`.",
			because: "the async validator section should route SDK-backed validator work through the dedicated sdk common guide");
		article.Text.Should().Contain("Use `Minimal canonical template` for the base binding structure",
			because: "the async validator variant should tell AI callers to reuse the canonical binding template instead of treating the section as a standalone shape");
		article.Text.Should().Contain("usr.MaxLengthFromSysSettingValidator",
			because: "the guide should include a concrete async validator example using SysSettingsService");
		article.Text.Should().Contain("new devkit.SysSettingsService()",
			because: "the async validator example must show devkit.SysSettingsService usage");
		article.Text.Should().Contain("const maxLength = await sysSettingsService.getByCode(config.settingCode);",
			because: "the async validator example should fetch the syssetting once and then choose the canonical field from the returned object");
		article.Text.Should().Contain("if (maxLength?.value != null && value.length > Number(maxLength.value)) {",
			because: "the async validator example should use the canonical numeric `.value` field instead of treating the syssetting payload inconsistently");
		article.Text.Should().Contain("@creatio-devkit/common",
			because: "the async validator template must instruct adding devkit AMD dependency");
		article.Text.Should().Contain("minimal coupled sections required for validator correctness",
			because: "the safe editing rules should no longer tell AI callers to touch only SCHEMA_VALIDATORS when coupled binding edits are required");
		article.Text.Should().Contain("When validator code needs `@creatio-devkit/common`, read `page-schema-sdk-common` first and then follow its AMD dependency and public-API rules.",
			because: "safe editing rules should route SDK-backed validator edits through the dedicated sdk common guide");
		article.Text.Should().Contain("Verify the edited body is syntactically valid JavaScript before calling `sync-pages`.",
			because: "safe editing should describe an operational verification step rather than the vague instruction to re-parse");
		article.Text.Should().Contain("Replace all placeholder identifiers consistently",
			because: "the guide should reduce cargo-cult copying by clearly marking example identifiers as placeholders");
		article.Text.Should().Contain("Regex/pattern validator example",
			because: "the regex validator variant should use a direct AI-readable title instead of an ambiguous delta label");
		article.Text.Should().Contain("Use `Minimal canonical template` for the binding structure",
			because: "the regex validator variant should tell AI callers to reuse the canonical binding template instead of treating the section as standalone");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns DataForge orchestration guidance that keeps exact package-local reuse checks on runtime context before DataForge fallback.")]
	public void DataForgeOrchestrationGuidanceResource_Should_Keep_Runtime_Context_As_Primary_Source_For_Existing_App_Reuse() {
		// Arrange
		DataForgeOrchestrationGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = result.Should().BeOfType<TextResourceContents>(
			because: "the DataForge orchestration guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/dataforge-orchestration",
			because: "the resource should expose a stable MCP URI for DataForge orchestration guidance");
		article.Text.Should().Contain("DataForge is not the primary mechanism for exact package-local reuse checks",
			because: "existing-app page/detail reuse should resolve from runtime app context before semantic discovery tools are used");
		article.Text.Should().Contain("get-app-info",
			because: "the guidance should point existing-app reuse checks to runtime app context");
		article.Text.Should().Contain("get-page",
			because: "the guidance should point existing-app reuse checks to live page inspection");
		article.Text.Should().Contain("get-entity-schema-properties",
			because: "the guidance should point existing-app reuse checks to schema inspection before falling back to DataForge");
	}

	[Test]
	[Category("Unit")]
	[Description("Standard validator contract extraction keeps all backtick-delimited param names from the decision table.")]
	public void PageSchemaValidatorsGuidanceResource_Should_Parse_Standard_Validator_Param_Contracts() {
		// Arrange / Act
		IReadOnlyDictionary<string, string[]> contracts = StandardValidatorContractParser.GetContracts();

		// Assert
		contracts.Should().ContainKey("crt.Required",
			because: "zero-param built-in validators should still be represented in the extracted contract map");
		contracts["crt.Required"].Should().BeEmpty(
			because: "the parser should convert 'none' table cells into empty parameter contracts");
		contracts.Should().ContainKey("crt.MinLength",
			because: "the extracted contract map should include built-in min-length validation");
		contracts["crt.MinLength"].Should().Equal(new[] { "minLength" },
			because: "the parser should preserve the canonical min-length param name from the decision table");
		contracts.Should().ContainKey("crt.MaxLength",
			because: "the extracted contract map should include built-in max-length validation");
		contracts["crt.MaxLength"].Should().Equal(new[] { "maxLength" },
			because: "the parser should preserve the canonical max-length param name from the decision table");
	}
}
