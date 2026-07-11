using Clio.Command.McpServer.Resources;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

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
		article.Text.Should().Contain("data-bindings",
			because: "the creation-oriented guide should point callers to the dedicated binding guide when workflow selection depends on data bindings");
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
		article.Text.Should().Contain("{code}_FormPage",
			because: "the guide should name the derived page schemas so agents can reference them without re-discovery");
		article.Text.Should().Contain("`create-app` already creates the default section for the canonical main entity",
			because: "the guide should state the positive fact before explaining when create-app-section is appropriate");
		article.Text.Should().Contain("SEQUENTIALLY, not in parallel",
			because: "the guide must tell agents to create sections one at a time to avoid the contention InsertQuery failure (ENG-93089)");
		article.Text.Should().Contain("contention",
			because: "the guide must document the retryable contention error-class so agents recover by serializing instead of abandoning");
		article.Text.Should().Contain("server-side",
			because: "the contention guidance must acknowledge a detail-less rejection may be a server-side failure, not only parallel creation (ENG-93089 C4)");
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
		article.Text.Should().Contain("Wrap MCP tool arguments under the top-level `args` JSON object",
			because: "the guide should explicitly publish the wrapped request shape required by the clio MCP tool schema");
		article.Text.Should().Contain("do not send `bundle` or `bundle.viewConfig` as the body payload",
			because: "the guide should explain the concrete page payload shape expected by the page tools");
		article.Text.Should().Contain("JSON object string",
			because: "the guide should explain the concrete resources payload shape expected by page tools");
		article.Text.Should().Contain("create-data-binding-db",
			because: "the guide should steer standalone lookup seeding back to MCP-native data-binding tools");
		article.Text.Should().Contain("data-bindings",
			because: "the guide should point callers to the dedicated binding guide for workflow selection and section registration");
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
		article.Text.Should().Contain("SEQUENTIALLY, not in parallel",
			because: "the maintenance guide must tell agents to create sections one at a time to avoid the contention InsertQuery failure (ENG-93089)");
		article.Text.Should().Contain("contention",
			because: "the maintenance guide must document the retryable contention error-class so agents recover by serializing instead of abandoning");
		article.Text.Should().Contain("server-side",
			because: "the contention guidance must acknowledge a detail-less rejection may be a server-side failure, not only parallel creation (ENG-93089 C4)");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a canonical MCP guidance article for generic lookup seeding and local binding workflows.")]
	public void DataBindingsGuidanceResource_Should_Return_Canonical_Data_Bindings_Guide() {
		// Arrange
		DataBindingsGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = (TextResourceContents)result;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/data-bindings",
			because: "the binding guide should expose a stable MCP URI for generic cross-tool binding guidance");
		article.MimeType.Should().Be("text/plain",
			because: "the binding guide should be discoverable as plain text");
		article.Text.Should().Contain("get-tool-contract",
			because: "the guide should route exact field-level questions to the executable contract tool");
		article.Text.Should().Contain("sync-schemas",
			because: "the guide should identify inline seed rows as a canonical batched path");
		article.Text.Should().Contain("create-data-binding-db",
			because: "the guide should advertise the standalone DB-first binding path");
		article.Text.Should().Contain("create-data-binding",
			because: "the guide should advertise the local binding artifact path");
		article.Text.Should().NotContain(".agents/skills/clio-data-bindings",
			because: "the guide should stay valid after install-skills copies the bundle outside the source repo layout");
		article.Text.Should().NotContain("CardSchemaUId",
			because: "section page linkage invariants do not belong in the generic binding guide");
		article.Text.Should().NotContain("assets/bindings-lookup.json",
			because: "section-specific stable ID sourcing does not belong in the generic binding guide");
		article.Text.Should().Contain("runtime-only columns are not blockers",
			because: "the guide should explain the DB-first subset-column projection rule for Account-like schemas");
		article.Text.Should().Contain("install logs or planned payloads",
			because: "the guide should reject install-log-only verification for remote binding mutations");
		article.Text.Should().Contain("Seed rows do not implement defaults",
			because: "the guide should keep lookup seeding separate from default semantics");
		article.Text.Should().Contain("DisplayValue",
			because: "the guide should make lookup and image-reference display semantics explicit");
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
		article.Text.Should().Contain("you MUST read `page-schema-creatio-devkit-common` before touching `SCHEMA_DEPS`, `SCHEMA_ARGS`, SDK services, or raw service calls",
			because: "handler guidance should route sdk-backed and service-backed handler edits to the dedicated sdk common guide");
		article.Text.Should().Contain("you MUST read `page-schema-creatio-devkit-common` before touching `SCHEMA_DEPS`, `SCHEMA_ARGS`, SDK services, or raw service calls",
			because: "handler guidance should make sdk and service routing to the sdk common guide mandatory");
		article.Text.Should().Contain("Mandatory routing rule: when the handler requirement includes any data access, system setting read/write, process execution, model query, or backend/external service call",
			because: "handler guidance should force ai callers through the sdk common guide before choosing a data or service implementation pattern");
		article.Text.Should().Contain("JavaScript array section",
			because: "handler guidance should state the SCHEMA_HANDLERS container shape directly");
		article.Text.Should().Contain("Decision tree",
			because: "handler guidance should front-load the main branching decisions for AI consumers");
		article.Text.Should().Contain("Do NOT toggle a bound `visible` attribute from a handler",
			because: "the handler decision tree must route 'hide/show an element until a field is filled' to a page business rule (is-filled-in/is-not-filled-in) and forbid the visible-bound-attribute handler anti-pattern from ENG-92154");
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
		article.Text.Should().Contain("| deployed page-body handler in `SCHEMA_HANDLERS` | `await sdk.HandlerChainService.instance.process({ type, $context, scopes })` |",
			because: "handler guidance must point page-body authors at the documented @creatio-devkit/common dispatcher used by Creatio Academy SCHEMA_HANDLERS examples");
		article.Text.Should().Contain("Do NOT default to `request.$context.executeRequest(...)` in deployed page-body handlers",
			because: "handler guidance must call out executeRequest as the non-public form so agents stop reaching for it when they have the documented HandlerChainService alternative");
		article.Text.Should().Contain("Else if the handler must read or write data, syssettings, processes, or backend services, stop and read `page-schema-creatio-devkit-common` before authoring the handler body.",
			because: "the handler decision tree should have an explicit sdk common branch for data and service work");
		article.Text.Should().Contain("| open a related page or mini page | `crt.OpenPageRequest` | button/menu `clicked.request` | no |",
			because: "handler guidance should keep simple navigation on direct request wiring instead of custom handlers");
		article.Text.Should().Contain("| create a related record from current context | `crt.CreateRecordRequest` | button/menu `clicked.request` | no |",
			because: "handler guidance should keep simple create flows on direct request wiring instead of custom handlers");
		article.Text.Should().Contain("crt.CreateRecordRequest page-resolution note",
			because: "handler guidance must carry the page-resolution note so callers know a CreateRecordRequest Add button needs a registered or explicit page");
		article.Text.Should().Contain("There is no page for new or existing record",
			because: "handler guidance must name the exact runtime error a section-less detail entity raises when CreateRecordRequest cannot resolve a page");
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
		article.Text.Should().Contain("Do NOT use `request.viewModel`, `request.sender`, `.$get(...)`, `.$set(...)`, or `request.$context.get(...)` in deployed page-body handlers.",
			because: "handler guidance should explicitly reject invented and legacy sender/get/set access patterns");
		article.Text.Should().Contain("Canonical rule for `crt.HandleViewModelAttributeChangeRequest`: use `request.value` for the triggering attribute, not `request.viewModel.get(...)`.",
			because: "handler guidance should spell out the exact attribute-change API that prevents invented request.viewModel reads");
		article.Text.Should().Contain("Do NOT choose raw `fetch(...)` to a platform endpoint before checking `page-schema-creatio-devkit-common` for a canonical `crt.*Request`, SDK service, or `sdk.Model` pattern.",
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
		article.Text.Should().Contain("Use `await sdk.HandlerChainService.instance.process({ type, $context, scopes })` when a deployed page-body handler forwards into another page-scoped request.",
			because: "the handler guide should keep HandlerChainService.instance.process as the canonical page-body orchestration path per Creatio Academy SCHEMA_HANDLERS examples");
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
		article.Text.Should().Contain("crt.PrintablesRequest",
			because: "handler guidance should include the built-in printables request in the catalog");
		article.Text.Should().Contain("crt.GoToPrintablesRequest",
			because: "handler guidance should include the go-to-printables navigation request in the catalog");
		article.Text.Should().Contain("crt.ExportDataGridToExcelRequest",
			because: "handler guidance should include the Excel export request in the catalog");
		article.Text.Should().Contain("crt.ImportDataRequest",
			because: "handler guidance should include the import data request in the catalog");
		article.Text.Should().Contain("crt.DeleteRecordsRequest",
			because: "handler guidance should include the bulk delete request in the catalog");
		article.Text.Should().Contain("crt.CopyRecordRequest",
			because: "handler guidance should include the copy/duplicate record request in the catalog");
		article.Text.Should().Contain("crt.ShowDialogRequest",
			because: "handler guidance should include the show-dialog request in the built-in catalog");
		article.Text.Should().Contain("| print the current or selected record(s) | `crt.PrintablesRequest` | button/menu `clicked.request` | no |",
			because: "handler guidance should keep printables on direct request wiring instead of a custom handler");
		article.Text.Should().Contain("| export list data to Excel | `crt.ExportDataGridToExcelRequest` | button/menu `clicked.request` | no |",
			because: "handler guidance should keep Excel export on direct request wiring instead of a custom handler");
		article.Text.Should().Contain("| import data for an entity | `crt.ImportDataRequest` | button/menu `clicked.request` | no |",
			because: "handler guidance should keep import on direct request wiring instead of a custom handler");
		article.Text.Should().Contain("| delete multiple selected records from a list | `crt.DeleteRecordsRequest` | button/menu `clicked.request` | no |",
			because: "handler guidance should distinguish bulk list delete from single-record delete in the decision table");
		article.Text.Should().Contain("| duplicate a record | `crt.CopyRecordRequest` | button/menu `clicked.request` | no |",
			because: "handler guidance should keep record duplication on direct request wiring");
		article.Text.Should().Contain("| `crt.PrintablesRequest` | config | `dataSourceName` required, `templateId?`, `printableCaption?`, `convertInPDF?`, `filters?` | generate printable document for current or selected record(s) |",
			because: "handler guidance should expose the printables request contract with its required and optional fields");
		article.Text.Should().Contain("| `crt.ExportDataGridToExcelRequest` | config | `viewName` required, `filters?` | export list data to Excel |",
			because: "handler guidance should expose the Excel export request contract");
		article.Text.Should().Contain("| `crt.ImportDataRequest` | config | `entitySchemaName` required | open import wizard for an entity |",
			because: "handler guidance should expose the import data request contract");
		article.Text.Should().Contain("| `crt.DeleteRecordsRequest` | config | `dataSourceName` required, `filters?`, `recordIds?`, `skipConfirmation?` | delete multiple records; prefer over `crt.DeleteRecordRequest` for list-based bulk delete |",
			because: "handler guidance should expose the bulk delete request contract and clarify when to prefer it over the single-record variant");
		article.Text.Should().Contain("| `crt.CopyRecordRequest` | config | `recordId` required, `itemsAttributeName?`, `entityName?` | duplicate a record |",
			because: "handler guidance should expose the copy record request contract");
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
		article.Text.Should().Contain("| `crt.RunBusinessProcessRequest` | config | `processName` + `processRunType` required — FULL parameter contract lives in the `run-process-button` guide (single source of truth) | Keys in `processParameters` / `parameterMappings` / `recordIdProcessParameterName` are process parameter CODES, NOT captions — a wrong code is silently dropped. Resolve with `get-process-signature` and get-guidance `run-process-button` before authoring this button |",
			because: "the handler catalog should point to the single-source-of-truth run-process-button guide and carry the CODE-not-caption rule instead of restating the full param list");
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
		article.Text.Should().Contain("| `crt.ShowDialog` | source request is `crt.ShowDialogRequest`, handled by `crt.ShowDialogHandler` | `dialogConfig.data` with `message`, `actions`, optional `title` | in code author `type: \"crt.ShowDialogRequest\"`; `crt.ShowDialog` is the user-visible catalog label |",
			because: "handler guidance should disambiguate the user-visible dialog label from the source request type with an explicit authoring rule");
		article.Text.Should().Contain("`message`, `actions`, and `title` go under `dialogConfig.data`, NOT directly on `dialogConfig`",
			because: "handler guidance must steer authors to nest the dialog payload under dialogConfig.data so the message renders (ENG-91748)");
		article.Text.Should().Contain("type: \"crt.ShowDialogRequest\"",
			because: "handler guidance should show the actual request type inside the minimal dialog example");
		article.Text.Should().MatchRegex(@"dialogConfig:\s*\{\s*data:\s*\{",
			because: "the minimal dialog example must nest the payload under dialogConfig.data, not flat on dialogConfig (ENG-91748)");
		article.Text.Should().NotMatchRegex(@"dialogConfig:\s*\{\s*(message|title|actions):",
			because: "the handler guide must never reintroduce the flat dialogConfig payload shape that caused the empty-dialog bug (ENG-91748)");
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
		article.Text.Should().Contain("\"just show a short confirmation message\" is a `crt.ShowDialogRequest`, NOT a browser dialog",
			because: "handler guidance must route a short confirmation message to crt.ShowDialogRequest so the agent does not fall back to alert() (ENG-91748)");
		article.Text.Should().Contain("Do NOT use `alert(...)`, `window.alert(...)`, `confirm(...)`, or `prompt(...)` to show a message from a handler",
			because: "handler guidance must forbid raw browser dialog primitives in deployed page-body handlers (ENG-91748)");
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
	public void PageSchemaCreatioDevkitCommonGuidanceResource_Should_Return_Canonical_Sdk_Common_Guide() {
		// Arrange
		PageSchemaCreatioDevkitCommonGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = result.Should().BeOfType<TextResourceContents>(
			because: "the sdk common guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/page-schema-creatio-devkit-common",
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
		article.Text.Should().Contain("| imperative request dispatch inside SCHEMA_HANDLERS | `sdk.HandlerChainService.instance.process(...)` | `process({ type, $context, scopes })` |",
			because: "the guide should mark HandlerChainService.instance.process as the canonical page-body dispatcher (per Creatio Academy SCHEMA_HANDLERS examples), not as an advanced fallback");
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
		article.Text.Should().MatchRegex(@"dialogConfig:\s*\{\s*data:\s*\{\s*message:",
			because: "the crt.ShowDialogRequest example must nest message/actions under dialogConfig.data, locking the ENG-91748 fix against a flat-shape regression");
		article.Text.Should().Contain("wraps that same config one level deeper under `dialogConfig.data`",
			because: "the guide must keep the MessageDialogConfig contrast rule distinguishing crt.ShowDialogRequest (data-wrapped) from the flat DialogService.open shape (ENG-91748)");
		article.Text.Should().NotMatchRegex(@"dialogConfig:\s*\{\s*(message|title|actions):",
			because: "the guide must never reintroduce the flat dialogConfig payload shape that caused the empty-dialog bug (ENG-91748)");
		article.Text.Should().Contain("Inner handler/body snippet only: ProcessEngineService with model query helpers:",
			because: "fragment-only sdk snippets should say they are not standalone schema modules");
		article.Text.Should().Contain("Inner handler/body snippet only: DialogService from SDK code:",
			because: "fragment-only dialog snippets should say they are not standalone schema modules");
		article.Text.Should().Contain("Inner handler/body snippet: canonical HandlerChainService dispatch from page-body handler code (per Creatio Academy SCHEMA_HANDLERS examples):",
			because: "fragment-only handler-chain snippets should say they are not standalone schema modules and that HandlerChainService.instance.process is the canonical dispatch surface");
		article.Text.Should().Contain("Rule: in deployed page-body handlers, use `await sdk.HandlerChainService.instance.process({ type, $context, scopes })` for imperative request dispatch.",
			because: "the guide should pin HandlerChainService.instance.process as the canonical schema-handler dispatch path per Creatio Academy");
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
	[Description("Returns a canonical MCP guidance article for Freedom UI page localizable strings.")]
	public void PageSchemaResourcesGuidanceResource_Should_Return_Canonical_Resources_Guide() {
		// Arrange
		PageSchemaResourcesGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = result.Should().BeOfType<TextResourceContents>(
			because: "the resources guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/page-schema-resources",
			because: "the resource should expose a stable MCP URI for page localizable string guidance");
		article.MimeType.Should().Be("text/plain",
			because: "the resources guide should be discoverable as plain text");
		article.Text.Should().Contain("$Resources.Strings.<ResourceKey>",
			because: "the guide should document the preferred reactive binding syntax");
		article.Text.Should().Contain("#ResourceString(KeyName)#",
			because: "the guide should document the macro syntax");
		article.Text.Should().Contain("`resources` parameter",
			because: "the guide should document how explicit resource entries are passed to page tools");
		article.Text.Should().Contain("HARD REJECT",
			because: "the guide should state that inline text literals are rejected, not merely discouraged");
		article.Text.Should().Contain("placeholder",
			because: "placeholders are the headline case the enforcement guidance must call out");
		article.Text.Should().Contain("default-language",
			because: "the creation rule must tell the agent to seed the default-language value via the resources parameter");
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
		article.Text.Should().Contain("also read `page-schema-creatio-devkit-common` before touching `SCHEMA_DEPS`, `SCHEMA_ARGS`, or SDK service calls",
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
		article.Text.Should().Contain("Before editing `SCHEMA_DEPS`, `SCHEMA_ARGS`, or SDK service usage here, read `page-schema-creatio-devkit-common`.",
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
		article.Text.Should().Contain("When validator code needs `@creatio-devkit/common`, read `page-schema-creatio-devkit-common` first and then follow its AMD dependency and public-API rules.",
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

	[Test]
	[Category("Unit")]
	[Description("Returns converter guidance that covers OOTB converters, custom usr.* converters, and pipe-binding syntax for Freedom UI pages.")]
	public void PageSchemaConvertersGuidanceResource_Should_Return_Canonical_Converter_Guide() {
		// Arrange
		PageSchemaConvertersGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = result.Should().BeOfType<TextResourceContents>(
			because: "the converter guide should be returned as a plain-text MCP resource").Subject;

		// Assert — URI
		article.Uri.Should().Be("docs://mcp/guides/page-schema-converters",
			because: "the converter guide should expose a stable MCP URI for converter authoring guidance");

		// Assert — scope and marker name
		article.Text.Should().Contain("SCHEMA_CONVERTERS",
			because: "converter guidance should point callers to the marker-delimited converter section");
		article.Text.Should().Contain("must contain an object section, not an array section",
			because: "converter guidance should state the SCHEMA_CONVERTERS container shape directly");

		// Assert — decision tree
		article.Text.Should().Contain("Decision tree",
			because: "converter guidance should front-load the main branching decisions for AI consumers");
		article.Text.Should().Contain("DISPLAY-ONLY value transformation",
			because: "the decision tree should distinguish display transformation from validation and business logic");
		article.Text.Should().Contain("stop and read `page-schema-validators`",
			because: "the decision tree should route validation requirements to the validators guide");
		article.Text.Should().Contain("stop and read `page-schema-handlers`",
			because: "the decision tree should route business logic requirements to the handlers guide");

		// Assert — OOTB converter decision table
		article.Text.Should().Contain("OOTB converter decision table",
			because: "converter guidance should provide a decision table of built-in converters before allowing custom ones");
		article.Text.Should().Contain("| Requirement pattern | Use | Parameters | Custom converter needed |",
			because: "the OOTB table should be structured for direct AI consumption");
		article.Text.Should().Contain("crt.ToBoolean",
			because: "guidance should document the built-in boolean converter");
		article.Text.Should().Contain("crt.InvertBooleanValue",
			because: "guidance should document the built-in boolean inversion converter");
		article.Text.Should().Contain("crt.ToEmailLink",
			because: "guidance should document the built-in email-to-link converter");
		article.Text.Should().Contain("crt.ToPhoneLink",
			because: "guidance should document the built-in phone-to-link converter");
		article.Text.Should().Contain("crt.ToObjectProp",
			because: "guidance should document the built-in object-property extractor converter");
		article.Text.Should().Contain("| requirement is not covered by rows above | custom `usr.*` converter | custom | yes |",
			because: "the table should tell AI when a custom converter is justified");

		// Assert — binding syntax
		article.Text.Should().Contain("Converter binding syntax",
			because: "guidance should explain where and how converters are bound");
		article.Text.Should().Contain("\"$AttributeName | converterName\"",
			because: "guidance should show the canonical pipe-binding syntax");
		article.Text.Should().Contain("NOT in `viewModelConfigDiff`",
			because: "guidance should prevent the common mistake of binding converters in the view-model section");

		// Assert — custom converter naming
		article.Text.Should().Contain("usr.` prefix",
			because: "custom converters must use the usr. namespace prefix");
		article.Text.Should().Contain("do NOT declare them in `SCHEMA_CONVERTERS`",
			because: "guidance should prevent declaring built-in crt.* converters in the schema section");

		// Assert — NON-NEGOTIABLES
		article.Text.Should().Contain("NON-NEGOTIABLES",
			because: "converter guidance should expose a compact high-priority rules block");
		article.Text.Should().Contain("Converters affect DISPLAY only",
			because: "the non-negotiables should enforce that converters do not write back to the model");
		article.Text.Should().Contain("Never put side effects inside a converter function",
			because: "the non-negotiables should prohibit side effects (but not async) inside converters");
		article.Text.Should().Contain("Async converters are allowed",
			because: "the non-negotiables must explicitly permit async converters after the async section was introduced");
		article.Text.Should().Contain("Do NOT call non-cached HTTP endpoints",
			because: "the non-negotiables should restrict uncached HTTP inside converters");

		// Assert — async converters section
		article.Text.Should().Contain("Async converters",
			because: "the guide must include a dedicated section on async converter authoring");
		article.Text.Should().Contain("instanceof Promise",
			because: "the async section should explain the runtime's Promise detection mechanism");
		article.Text.Should().Contain("SysSettingsService",
			because: "the async section must name SysSettingsService as a safe example of a cached service");
		article.Text.Should().Contain("pre-loaded at startup into a two-layer cache",
			because: "the guide should explain that OOTB settings are cached at startup");
		article.Text.Should().Contain("Custom `usr.*` settings are NOT pre-loaded",
			because: "the guide must clarify that custom settings are not in the startup preload set");
		article.Text.Should().Contain("usr.FormatPhoneNumber",
			because: "the async section should include a complete named example");
		article.Text.Should().Contain("async (value) =>",
			because: "the guide should show the async arrow function syntax that AI will copy");
		article.Text.Should().Contain("prefer async converter when the async call is cheap/cached",
			because: "the guide should give an explicit decision rule for choosing async converter vs handler");
		article.Text.Should().Contain("If the converter is async, ensure the SDK service used is cached",
			because: "the before-save checklist should require verifying caching for async converters");

		// Assert — templates and examples
		article.Text.Should().Contain("Minimal canonical template",
			because: "converter guidance should provide a reusable template before detailed variants");
		article.Text.Should().Contain("usr.ToUpperCase",
			because: "guidance should include a concrete custom converter example");
		article.Text.Should().Contain("value?.toUpperCase() ?? ''",
			because: "the example converter body should show a realistic implementation");
		article.Text.Should().Contain("\"caption\": \"$UsrName | usr.ToUpperCase\"",
			because: "the example should show the converter wired to a real viewConfigDiff property");

		// Assert — OOTB binding examples
		article.Text.Should().Contain("crt.InvertBooleanValue",
			because: "guidance should include an OOTB boolean inversion binding example");
		article.Text.Should().Contain("crt.ToEmailLink",
			because: "guidance should include an OOTB email link binding example");
		article.Text.Should().Contain("crt.ToPhoneLink",
			because: "guidance should include an OOTB phone link binding example");
		article.Text.Should().Contain("crt.ToObjectProp:'displayValue'",
			because: "guidance should show that ToObjectProp params must be quoted strings, not bare identifiers");

		// Assert — BEFORE SAVE CHECKLIST
		article.Text.Should().Contain("BEFORE SAVE CHECKLIST",
			because: "converter guidance should give AI callers a compact verification gate before sync-pages");
		article.Text.Should().Contain("$Attr | converterName` format, not `$Attr.converterName`",
			because: "the checklist should guard against using dot notation instead of pipe syntax");
		article.Text.Should().Contain("`SCHEMA_CONVERTERS` is an object literal, not an array",
			because: "the checklist should reinforce the object-not-array shape constraint");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a canonical MCP guidance article for executing approved plans through clio MCP transport, ordering, branching, and recovery rules.")]
	public void AgentExecutionGuidanceResource_Should_Return_Canonical_Execution_Guide() {
		// Arrange
		AgentExecutionGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = result.Should().BeOfType<TextResourceContents>(
			because: "the agent execution guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/agent-execution",
			because: "the resource should expose a stable MCP URI for agent execution guidance");
		article.MimeType.Should().Be("text/plain",
			because: "the agent execution guide should be discoverable as plain text");
		article.Text.Should().Contain("clio MCP agent execution guide",
			because: "the article should open with the canonical title for downstream contains-checks");
		article.Text.Should().Contain("get-tool-contract",
			because: "the guide should require contract discovery before invocation");
		article.Text.Should().Contain("Pass boolean MCP parameters as booleans",
			because: "the guide should publish the canonical boolean transport rule");
		article.Text.Should().Contain("Execution order",
			because: "the guide should publish a numbered execution order section");
		article.Text.Should().Contain("Branching rules",
			because: "the guide should publish branching rules between new-app and existing-app flows");
		article.Text.Should().Contain("Schema sync recovery patterns",
			because: "the guide should cover schema-sync recovery patterns owned by clio");
		article.Text.Should().Contain("Failed to create section",
			because: "the guide should describe the wiring-failure recovery rule for reuse decisions using the current section-create error marker");
		article.Text.Should().Contain("metadata readback timeout",
			because: "the guide should describe the section readback timeout recovery path");
		article.Text.Should().Contain("delete the orphaned entity using `delete-schema`",
			because: "the guide should encode the orphan-cleanup step for the section recovery path");
		article.Text.Should().Contain("Database update required",
			because: "the guide should require materialized metadata before treating schema work as successful");
		article.Text.Should().Contain("validate-page",
			because: "the guide should require client-side validation before persisting page bodies");
		article.Text.Should().Contain("docs://mcp/guides/support-mode",
			because: "the guide should redirect support-run readers to the dedicated support-mode guide");
	}

	[Test]
	[Category("Unit")]
	[Description("GuidanceCatalog exposes page-schema-converters so AI callers can retrieve converter authoring guidance by name.")]
	public void GuidanceCatalog_Should_Include_Page_Schema_Converters_Entry() {
		// Act
		bool found = GuidanceCatalog.TryGet("page-schema-converters", out GuidanceCatalogEntry entry);

		// Assert
		found.Should().BeTrue(
			because: "the catalog must expose page-schema-converters so get-guidance can return it by name");
		entry.Name.Should().Be("page-schema-converters",
			because: "the catalog entry name must match the lookup key exactly");
		entry.Description.Should().Contain("converters",
			because: "the catalog description should identify the subject of the guidance article");
		entry.Article.Should().NotBeNull(
			because: "the catalog entry must carry the guidance text article");
		entry.Article.Uri.Should().Be("docs://mcp/guides/page-schema-converters",
			because: "the article URI in the catalog must match the resource URI");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns configuration web-service guidance and references as stable MCP resources.")]
	public void ConfigurationWebServiceGuidanceResource_Should_Return_Guide_And_References() {
		// Arrange
		ConfigurationWebServiceGuidanceResource resource = new();

		// Act
		TextResourceContents guide = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "configuration web-service guidance should be returned as a plain-text MCP resource").Subject;
		TextResourceContents dtoPatterns = resource.GetDtoPatterns().Should().BeOfType<TextResourceContents>(
			because: "DTO patterns should be returned as a plain-text MCP reference").Subject;
		TextResourceContents statusCodePatterns = resource.GetStatusCodePatterns().Should().BeOfType<TextResourceContents>(
			because: "status-code patterns should be returned as a plain-text MCP reference").Subject;
		TextResourceContents compositionRootPattern = resource.GetCompositionRootPattern().Should().BeOfType<TextResourceContents>(
			because: "composition-root patterns should be returned as a plain-text MCP reference").Subject;
		TextResourceContents manualRuntimeChecklist = resource.GetManualRuntimeChecklist().Should().BeOfType<TextResourceContents>(
			because: "manual runtime verification should be returned as a plain-text MCP reference").Subject;

		// Assert
		guide.Uri.Should().Be("docs://mcp/guides/configuration-webservice",
			because: "the guide should expose the stable URI requested for configuration web-service guidance");
		guide.MimeType.Should().Be("text/plain",
			because: "the guide should be discoverable as plain text");
		guide.Text.Should().Contain("Inherit BaseService, IReadOnlySessionState",
			because: "the guide should preserve the web-service shape rule from the source skill");
		guide.Text.Should().Contain("docs://mcp/references/configuration-webservice/dto-patterns",
			because: "the guide should point callers to its detailed DTO reference resource");

		dtoPatterns.Uri.Should().Be("docs://mcp/references/configuration-webservice/dto-patterns",
			because: "the DTO reference URI should be stable for direct MCP resource reads");
		dtoPatterns.MimeType.Should().Be("text/plain",
			because: "references should be exposed as plain text");
		dtoPatterns.Text.Should().Contain("Return a concrete DTO type",
			because: "the DTO reference should preserve the concrete return-type rule");

		statusCodePatterns.Uri.Should().Be("docs://mcp/references/configuration-webservice/status-code-patterns",
			because: "the status-code reference URI should be stable for direct MCP resource reads");
		statusCodePatterns.Text.Should().Contain("NET472: /0/rest/<ServiceName>/<MethodName>",
			because: "the status-code reference should preserve framework-specific route guidance");

		compositionRootPattern.Uri.Should().Be("docs://mcp/references/configuration-webservice/composition-root-pattern",
			because: "the composition-root reference URI should be stable for direct MCP resource reads");
		compositionRootPattern.Text.Should().Contain("Keep the service thin",
			because: "the composition-root reference should preserve the thin-service guidance");

		manualRuntimeChecklist.Uri.Should().Be("docs://mcp/references/configuration-webservice/manual-runtime-checklist",
			because: "the manual runtime checklist URI should be stable for direct MCP resource reads");
		manualRuntimeChecklist.Text.Should().Contain("Send a representative success request",
			because: "the runtime checklist should preserve the manual verification workflow");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns configuration web-service test guidance and references as stable MCP resources.")]
	public void ConfigurationWebServiceTestsGuidanceResource_Should_Return_Guide_And_References() {
		// Arrange
		ConfigurationWebServiceTestsGuidanceResource resource = new();

		// Act
		TextResourceContents guide = resource.GetGuide().Should().BeOfType<TextResourceContents>(
			because: "configuration web-service test guidance should be returned as a plain-text MCP resource").Subject;
		TextResourceContents testFixturePattern = resource.GetTestFixturePattern().Should().BeOfType<TextResourceContents>(
			because: "test fixture patterns should be returned as a plain-text MCP reference").Subject;
		TextResourceContents assertionStyle = resource.GetAssertionStyle().Should().BeOfType<TextResourceContents>(
			because: "assertion style should be returned as a plain-text MCP reference").Subject;
		TextResourceContents endpointTestPatterns = resource.GetEndpointTestPatterns().Should().BeOfType<TextResourceContents>(
			because: "endpoint test patterns should be returned as a plain-text MCP reference").Subject;

		// Assert
		guide.Uri.Should().Be("docs://mcp/guides/configuration-webservice-tests",
			because: "the guide should expose the stable URI requested for configuration web-service test guidance");
		guide.MimeType.Should().Be("text/plain",
			because: "the guide should be discoverable as plain text");
		guide.Text.Should().Contain("Add or update tests for production web-service changes",
			because: "the guide should preserve the primary test-coverage rule from the source skill");
		guide.Text.Should().Contain("docs://mcp/references/configuration-webservice-tests/test-fixture-pattern",
			because: "the guide should point callers to its detailed fixture reference resource");

		testFixturePattern.Uri.Should().Be("docs://mcp/references/configuration-webservice-tests/test-fixture-pattern",
			because: "the fixture reference URI should be stable for direct MCP resource reads");
		testFixturePattern.Text.Should().Contain("Register test doubles through InjectedServices",
			because: "the fixture reference should preserve dependency-injection mocking guidance");

		assertionStyle.Uri.Should().Be("docs://mcp/references/configuration-webservice-tests/assertion-style",
			because: "the assertion-style reference URI should be stable for direct MCP resource reads");
		assertionStyle.Text.Should().Contain("Add because:",
			because: "the assertion-style reference should preserve the assertion reason rule");

		endpointTestPatterns.Uri.Should().Be("docs://mcp/references/configuration-webservice-tests/endpoint-test-patterns",
			because: "the endpoint-test reference URI should be stable for direct MCP resource reads");
		endpointTestPatterns.Text.Should().Contain("DTO-returning method",
			because: "the endpoint-test reference should preserve coverage guidance by endpoint response style");
	}

	[Test]
	[Category("Unit")]
	[Description("GuidanceCatalog exposes configuration web-service guides so AI callers can retrieve them by name.")]
	public void GuidanceCatalog_Should_Include_Configuration_WebService_Entries() {
		// Act
		bool webServiceFound = GuidanceCatalog.TryGet("configuration-webservice", out GuidanceCatalogEntry webServiceEntry);
		bool testsFound = GuidanceCatalog.TryGet("configuration-webservice-tests", out GuidanceCatalogEntry testsEntry);

		// Assert
		webServiceFound.Should().BeTrue(
			because: "the catalog must expose configuration-webservice so get-guidance can return the implementation guide by name");
		webServiceEntry.Article.Uri.Should().Be("docs://mcp/guides/configuration-webservice",
			because: "the catalog entry should point at the stable configuration web-service guide URI");
		webServiceEntry.Description.Should().Contain("configuration web services",
			because: "the catalog description should identify the guide subject");

		testsFound.Should().BeTrue(
			because: "the catalog must expose configuration-webservice-tests so get-guidance can return the test guide by name");
		testsEntry.Article.Uri.Should().Be("docs://mcp/guides/configuration-webservice-tests",
			because: "the catalog entry should point at the stable configuration web-service test guide URI");
		testsEntry.Description.Should().Contain("testing",
			because: "the catalog description should identify that the guide is test-focused");
	}

	[Test]
	[Category("Unit")]
	[Description("Generated composable-app skill resources expose all remaining skill guides and references as plain-text MCP articles.")]
	public void ComposableAppSkillResourceCatalog_Should_Expose_All_Generated_Skill_Resources() {
		// Arrange
		IReadOnlyList<ComposableAppSkillResourceEntry> entries = ComposableAppSkillResourceCatalog.GetEntries();

		// Act
		ComposableAppSkillResourceEntry atfGuide = entries.Single(entry =>
			entry.Article.Uri == "docs://mcp/guides/atf-repository-dev");
		ComposableAppSkillResourceEntry featureReference = entries.Single(entry =>
			entry.Article.Uri == "docs://mcp/references/feature-toggle/implementation-patterns");
		ComposableAppSkillResourceEntry sysSettingTestsReference = entries.Single(entry =>
			entry.Article.Uri == "docs://mcp/references/sys-setting-tests/setup-sys-settings-pattern");

		// Assert
		entries.Should().HaveCount(49,
			because: "the generated catalog should include 13 remaining skill guides plus their 36 reference files");
		entries.Should().OnlyContain(entry => entry.Article.MimeType == "text/markdown",
			because: "generated skill resources come from Markdown skill and reference files");
		entries.Should().OnlyContain(entry =>
				entry.Article.Uri.StartsWith("docs://mcp/guides/") ||
				entry.Article.Uri.StartsWith("docs://mcp/references/"),
			because: "generated resources should use the agreed MCP docs URI namespace");

		atfGuide.IsGuide.Should().BeTrue(
			because: "top-level SKILL.md documents should be guide resources");
		atfGuide.Article.Text.Should().Contain("ATF.Repository",
			because: "the generated guide should preserve the source skill content");

		featureReference.IsGuide.Should().BeFalse(
			because: "reference markdown files should remain direct reference resources instead of get-guidance entries");
		featureReference.Article.Text.Should().Contain("Feature",
			because: "the generated reference should preserve the source reference content");

		sysSettingTestsReference.Article.Text.Should().Contain("SetupSysSettings",
			because: "the generated sys-setting test reference should preserve its fixture setup guidance");
	}

	[Test]
	[Category("Unit")]
	[Description("GuidanceCatalog exposes generated composable-app skill guides while keeping generated references resource-only.")]
	public void GuidanceCatalog_Should_Include_Generated_Composable_App_Skill_Guides() {
		// Arrange
		IReadOnlyList<ComposableAppSkillResourceEntry> generatedGuides = ComposableAppSkillResourceCatalog.GetGuides();
		IReadOnlyList<ComposableAppSkillResourceEntry> generatedReferences = ComposableAppSkillResourceCatalog.GetEntries()
			.Where(entry => !entry.IsGuide)
			.ToArray();

		// Act
		bool allGuidesFound = generatedGuides.All(guide => GuidanceCatalog.TryGet(guide.Skill, out _));
		bool anyReferenceFound = generatedReferences.Any(reference =>
			GuidanceCatalog.TryGet($"{reference.Skill}/{reference.Reference}", out _));

		// Assert
		generatedGuides.Should().HaveCount(13,
			because: "only the remaining top-level composable-app skill documents should be registered for get-guidance");
		allGuidesFound.Should().BeTrue(
			because: "get-guidance should resolve every generated top-level skill guide by skill name");
		anyReferenceFound.Should().BeFalse(
			because: "generated references should be read through MCP resources directly, not through get-guidance names");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a canonical MCP guidance article for diagnostic-first behavior under support mode.")]
	public void SupportModeGuidanceResource_Should_Return_Canonical_Support_Mode_Guide() {
		// Arrange
		SupportModeGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = result.Should().BeOfType<TextResourceContents>(
			because: "the support-mode guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/support-mode",
			because: "the resource should expose a stable MCP URI for support-mode guidance");
		article.MimeType.Should().Be("text/plain",
			because: "the support-mode guide should be discoverable as plain text");
		article.Text.Should().Contain("clio MCP support-mode guide",
			because: "the article should open with the canonical title for downstream contains-checks");
		article.Text.Should().Contain("Diagnostic-first execution",
			because: "the guide should publish the diagnostic-first execution policy");
		article.Text.Should().Contain("clio_mcp_issue",
			because: "the guide should declare the primary critical defect category");
		article.Text.Should().Contain("instruction_issue",
			because: "the guide should classify guidance and pattern defects");
		article.Text.Should().Contain("environment_issue",
			because: "the guide should classify auth/network/runtime defects");
		article.Text.Should().Contain("orchestration_tool_failure",
			because: "the guide should classify caller/wrapper invocation defects");
		article.Text.Should().Contain("one confirmation probe",
			because: "the guide should publish the single confirmation-probe rule");
		article.Text.Should().Contain("exit_decision=fail_fast",
			because: "the guide should encode the fail-fast evidence triple emitted before stopping");
		article.Text.Should().Contain("blocked_stage=",
			because: "the guide should require blocked-stage evidence in fail-fast output");
		article.Text.Should().Contain("why_continue_is_unsafe=",
			because: "the guide should require why-continue-is-unsafe evidence in fail-fast output");
		article.Text.Should().Contain("error_signature",
			because: "the canonical failure record contract must include the normalized error signature");
		article.Text.Should().Contain("Confirmed failures",
			because: "the final reporting section list must include Confirmed failures");
		article.Text.Should().Contain("Non-target friction",
			because: "the final reporting section list must include Non-target friction");
		article.Text.Should().Contain("Support mode is on. Please share this session with support for analysis.",
			because: "the guide should encode the canonical handoff line appended to support-mode final responses");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a canonical MCP guidance article for Freedom UI business rules so AI callers use declarative rules instead of handlers.")]
	public void BusinessRulesGuidanceResource_Should_Return_Canonical_Business_Rules_Guide() {
		// Arrange
		BusinessRulesGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = result.Should().BeOfType<TextResourceContents>(
			because: "the business-rules guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/business-rules",
			because: "the resource should expose a stable MCP URI for business-rules guidance");
		article.MimeType.Should().Be("text/plain",
			because: "the business-rules guide should be discoverable as plain text");
		article.Text.Should().Contain("clio MCP business rules guide",
			because: "the article should identify itself as the dedicated business-rules guide");
		article.Text.Should().Contain("create-entity-business-rule",
			because: "the guide should advertise the entity-level business rule tool");
		article.Text.Should().Contain("create-page-business-rule",
			because: "the guide should advertise the page-level business rule tool");
		article.Text.Should().Contain("SEPARATE first-class artifacts",
			because: "the guide should make clear that business rules are not page-body code");
		article.Text.Should().Contain("Do NOT write JavaScript handler or validator code",
			because: "the guide should explicitly forbid implementing business-rule logic as handlers");
		article.Text.Should().Contain("condition group",
			because: "the guide should explain the condition-action structure of business rules");
		article.Text.Should().Contain("Entity-level business rules",
			because: "the guide should distinguish entity-level from page-level rules");
		article.Text.Should().Contain("Page-level business rules",
			because: "the guide should distinguish page-level from entity-level rules");
		article.Text.Should().Contain("State-changing actions are one-way",
			because: "the guide should warn that state-changing business-rule actions are directional");
		article.Text.Should().Contain("explicit inverse business rule",
			because: "the guide should instruct AI callers to model reversible state with an inverse rule");
		article.Text.Should().Contain("prefer `populateValue=true` by default",
			because: "the guide should steer AI callers toward the UI-like default for standard dependent lookup scenarios");
		article.Text.Should().Contain("classify the requirement into one mechanism",
			because: "the guide must teach lookup-restriction routing as a mechanism taxonomy, not a list of memorized business phrases");
		article.Text.Should().Contain("never a handler/crt.InitRequest",
			because: "the guide must steer every lookup-restriction mechanism away from handlers/crt.InitRequest");
		article.Text.Should().Contain("is-filled-in",
			because: "the guide must bridge 'hide/show an element until a field is entered' to the is-filled-in condition token (ENG-92154)");
		article.Text.Should().Contain("is-not-filled-in",
			because: "the guide must offer the inverse is-not-filled-in token so callers model both directions of a show/hide-until-filled rule");
		article.Text.Should().Contain("Do NOT toggle element visibility from a handler",
			because: "the guide must name the visible-bound-attribute-toggled-from-a-handler anti-pattern from ENG-92154 and keep element visibility on a business rule");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a canonical MCP guidance article for Freedom UI indicator widgets so AI callers can translate Copilot metric intent into runtime widget config.")]
	public void IndicatorWidgetGuidanceResource_Should_Return_Canonical_Indicator_Widget_Guide() {
		// Arrange
		IndicatorWidgetGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = result.Should().BeOfType<TextResourceContents>(
			because: "the indicator widget guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/indicator-widget",
			because: "the resource should expose a stable MCP URI for indicator widget guidance");
		article.MimeType.Should().Be("text/plain",
			because: "the indicator widget guide should be discoverable as plain text");
		article.Text.Should().Contain("clio MCP indicator widget guide",
			because: "the article should identify itself as the dedicated indicator-widget guide");
		article.Text.Should().Contain("get-component-info",
			because: "the trimmed guide should point callers to get-component-info as the single source of truth");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a canonical MCP guidance article for Freedom UI chart widgets so AI callers can translate Copilot chart intent into runtime widget config.")]
	public void ChartWidgetGuidanceResource_Should_Return_Canonical_Chart_Widget_Guide() {
		// Arrange
		ChartWidgetGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = result.Should().BeOfType<TextResourceContents>(
			because: "the chart widget guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/chart-widget",
			because: "the resource should expose a stable MCP URI for chart widget guidance");
		article.MimeType.Should().Be("text/plain",
			because: "the chart widget guide should be discoverable as plain text");
		article.Text.Should().Contain("clio MCP chart widget guide",
			because: "the article should identify itself as the dedicated chart-widget guide");
		article.Text.Should().Contain("get-component-info",
			because: "the trimmed guide should point callers to get-component-info as the single source of truth");
		article.Text.Should().Contain("data.providing.dependencies",
			because: "the guide must teach the dependencies wiring that filters a chart by page data on a record page");
		article.Text.Should().Contain("sectionBindingColumnRecordId",
			because: "the guide must document the designer-style page-data binding pair instead of claiming it is auto-injected");
		article.Text.Should().Contain("related-list",
			because: "the page-data wiring section should cross-link the canonical related-list guidance");
		article.Text.Should().NotContain("do not author it",
			because: "the prior wording wrongly told the agent the page-data binding is auto-handled, which left charts unfiltered");
		article.Text.Should().Contain("Title and header",
			because: "the guide must tell the agent to always set and register a title so the widget header is not blank");
		article.Text.Should().Contain("hideTools",
			because: "the guide must warn against the hidden hideTitle/hideTools flags that strip the title and the full-screen button");
		article.Text.Should().Contain("Style (theme) by page surface",
			because: "the guide must set the chart theme by page surface (dashboard→white, desktop→glassmorphism, home→full-fill), mirroring the indicator policy");
		article.Text.Should().Contain("glassmorphism",
			because: "Desktop charts must use the glassmorphism theme");
		article.Text.Should().Contain("ONLY when the user explicitly asks to sort",
			because: "the guide must tell the agent not to impose a default sort — emit seriesOrder only on explicit request");
		article.Text.Should().Contain("`config.color` is REQUIRED for a VISIBLE title",
			because: "the guide must require config.color so the title is not rendered white-on-white (invisible) on without-fill");
		article.Text.Should().Contain("`layoutConfig.rowSpan` >= 6",
			because: "the guide must set a grid size floor so charts are not generated unreadably short");
		article.Text.Should().Contain("`layoutConfig.height` >= 350",
			because: "the guide must set a flex-container height floor so a chart does not collapse");
		article.Text.Should().Contain("INDEPENDENT of `config.color`",
			because: "the guide must separate series color (data marks) from config.color (title) so a series-color change does not recolor the title");
		article.Text.Should().Contain("aggregation.column.expression",
			because: "the guide keeps the aggregation column path and enum rules; the structural aggregation.column nesting is now enforced by the registry-driven chart-widget validator");
		article.Text.Should().Contain("FixedGridSlot_qwe4asds",
			because: "the guide must steer Desktop chart placement into the exact editable slot FixedGridSlot_qwe4asds (CentralAreaDesktopTemplate), not the Main frame");
		article.Text.Should().Contain("show values by DEFAULT",
			because: "the guide must default data labels on (dataLabel.display:true) unless the user explicitly opts out");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a canonical MCP guidance article for adding and filtering a Freedom UI related/child list (detail) so AI callers can scope a list by the current page record.")]
	public void RelatedListGuidanceResource_Should_Return_Canonical_Related_List_Guide() {
		// Arrange
		RelatedListGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = result.Should().BeOfType<TextResourceContents>(
			because: "the related-list guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/related-list",
			because: "the resource should expose a stable MCP URI for related-list guidance");
		article.MimeType.Should().Be("text/plain",
			because: "the related-list guide should be discoverable as plain text");
		article.Text.Should().Contain("clio MCP related list guide",
			because: "the article should identify itself as the dedicated related-list guide");
		article.Text.Should().Contain("get-component-info",
			because: "the guide should point callers to get-component-info as the source of truth for crt.DataGrid and crt.ExpansionPanel");
		article.Text.Should().Contain("isCollection",
			because: "the guide must teach the collection attribute that backs the child list");
		article.Text.Should().Contain("modelConfig.dependencies",
			because: "the guide must teach the declarative dependencies entry that scopes the list by the open record");
		article.Text.Should().Contain("attributePath",
			because: "the guide must name the child foreign-key column field of the dependency");
		article.Text.Should().Contain("relationPath",
			because: "the guide must name the master-id path field of the dependency");
		article.Text.Should().Contain("\"relationPath\": \"PDS.Id\"",
			because: "the guide must show the canonical relationPath pointing at the page primary data source id");
		article.Text.Should().Contain("handlers: []",
			because: "the guide must emphasize that the declarative dependency needs no handler");
		article.Text.Should().Contain("Use `modelConfig.dependencies` instead",
			because: "the guide must warn against the init-handler scoping anti-pattern and redirect to dependencies");
		article.Text.Should().Contain("is not a container for other items",
			because: "the guide must warn that an inserted container without an initialized items slot fails at runtime and the page does not render");
		article.Text.Should().Contain("\"items\": []",
			because: "the guide must show that every inserted container (especially crt.ExpansionPanel) needs its content slot initialized in values");
		article.Text.Should().Contain("There is no page for new or existing record",
			because: "the guide must warn that a header CreateRecordRequest Add button on a section-less detail entity throws this exact runtime error on click");
		article.Text.Should().Contain("inline add row IS the add affordance",
			because: "the guide must steer callers to inline grid add (its editable flags fetched from get-component-info crt.DataGrid) as the safe default add affordance for a related list");
		article.Text.Should().Contain("entityPageName",
			because: "the guide must offer the explicit-page escape hatch for a header Add button when inline add is not wanted");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a canonical MCP guidance article for ESQ-style filters so AI callers can avoid common path, lookup, and relative-date mistakes.")]
	public void EsqFiltersGuidanceResource_Should_Return_Canonical_Esq_Filters_Guide() {
		// Arrange
		EsqFiltersGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = result.Should().BeOfType<TextResourceContents>(
			because: "the ESQ filters guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/esq-filters",
			because: "the resource should expose a stable MCP URI for ESQ filter guidance");
		article.MimeType.Should().Be("text/plain",
			because: "the ESQ filters guide should be discoverable as plain text");
	}

	[Test]
	[Category("Unit")]
	[Description("GuidanceCatalog exposes indicator-widget so AI callers can retrieve indicator widget authoring guidance by name.")]
	public void GuidanceCatalog_Should_Include_Indicator_Widget_Entry() {
		// Act
		bool found = GuidanceCatalog.TryGet("indicator-widget", out GuidanceCatalogEntry entry);

		// Assert
		found.Should().BeTrue(
			because: "the catalog must expose indicator-widget so get-guidance can return it by name");
		entry.Name.Should().Be("indicator-widget",
			because: "the catalog entry name must match the lookup key exactly");
		entry.Description.Should().Contain("indicator widgets",
			because: "the catalog description should identify the subject of the guidance article");
		entry.Article.Should().NotBeNull(
			because: "the catalog entry must carry the guidance text article");
		entry.Article.Uri.Should().Be("docs://mcp/guides/indicator-widget",
			because: "the article URI in the catalog must match the resource URI");
	}

	[Test]
	[Category("Unit")]
	[Description("GuidanceCatalog exposes chart-widget so AI callers can retrieve chart widget authoring guidance by name.")]
	public void GuidanceCatalog_Should_Include_Chart_Widget_Entry() {
		// Act
		bool found = GuidanceCatalog.TryGet("chart-widget", out GuidanceCatalogEntry entry);

		// Assert
		found.Should().BeTrue(
			because: "the catalog must expose chart-widget so get-guidance can return it by name");
		entry.Name.Should().Be("chart-widget",
			because: "the catalog entry name must match the lookup key exactly");
		entry.Description.Should().Contain("chart widgets",
			because: "the catalog description should identify the subject of the guidance article");
		entry.Article.Should().NotBeNull(
			because: "the catalog entry must carry the guidance text article");
		entry.Article.Uri.Should().Be("docs://mcp/guides/chart-widget",
			because: "the article URI in the catalog must match the resource URI");
	}

	[Test]
	[Category("Unit")]
	[Description("GuidanceCatalog exposes related-list so AI callers can retrieve detail/master-detail filter guidance by name.")]
	public void GuidanceCatalog_Should_Include_Related_List_Entry() {
		// Act
		bool found = GuidanceCatalog.TryGet("related-list", out GuidanceCatalogEntry entry);

		// Assert
		found.Should().BeTrue(
			because: "the catalog must expose related-list so get-guidance can return it by name");
		entry.Name.Should().Be("related-list",
			because: "the catalog entry name must match the lookup key exactly");
		entry.Description.Should().Contain("related/child list",
			because: "the catalog description should identify the subject of the guidance article");
		entry.Article.Should().NotBeNull(
			because: "the catalog entry must carry the guidance text article");
		entry.Article.Uri.Should().Be("docs://mcp/guides/related-list",
			because: "the article URI in the catalog must match the resource URI");
	}

	[Test]
	[Category("Unit")]
	[Description("GuidanceCatalog exposes esq-filters so AI callers can retrieve filter authoring guidance by name.")]
	public void GuidanceCatalog_Should_Include_Esq_Filters_Entry() {
		// Act
		bool found = GuidanceCatalog.TryGet("esq-filters", out GuidanceCatalogEntry entry);

		// Assert
		found.Should().BeTrue(
			because: "the catalog must expose esq-filters so get-guidance can return it by name");
		entry.Name.Should().Be("esq-filters",
			because: "the catalog entry name must match the lookup key exactly");
		entry.Description.Should().Contain("filter authoring",
			because: "the catalog description should identify the subject of the guidance article");
		entry.Article.Should().NotBeNull(
			because: "the catalog entry must carry the guidance text article");
		entry.Article.Uri.Should().Be("docs://mcp/guides/esq-filters",
			because: "the article URI in the catalog must match the resource URI");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a canonical MCP guidance article for mobile page editing that explicitly documents business-rule support and offline limitations.")]
	public void MobilePageGuidanceResource_Should_Return_Canonical_Mobile_Page_Guide() {
		// Arrange
		MobilePageGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = result.Should().BeOfType<TextResourceContents>(
			because: "the mobile page guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/mobile-page-modification",
			because: "the resource should expose a stable MCP URI for mobile page guidance");
		article.MimeType.Should().Be("text/plain",
			because: "the mobile page guide should be discoverable as plain text");
		article.Text.Should().Contain("clio MCP mobile page modification guide",
			because: "the article should identify itself as the dedicated mobile page guide");
		article.Text.Should().Contain("create-page-business-rule",
			because: "the mobile guide should explicitly document that mobile pages support page-level business rules");
		article.Text.Should().Contain("create-entity-business-rule",
			because: "the mobile guide should explicitly document that mobile guidance covers entity-level business rules too");
		article.Text.Should().Contain("identically to web",
			because: "the mobile guide should clarify that business rule generation works the same as web");
		article.Text.Should().Contain("OFFLINE LIMITATION",
			because: "the mobile guide should warn callers that not all rules are guaranteed to work offline");
		article.Text.Should().Contain("separate artifacts",
			because: "the guide should keep page-level business rules separate from mobile page body editing");
		article.Text.Should().Contain("Read `business-rules` for rule semantics",
			because: "the guide should keep detailed business-rule semantics in the dedicated shared guidance instead of duplicating them here");
		article.Text.Should().Contain("Mobile pages do not support validators at all",
			because: "the guide should preserve the validator limitation while clarifying business-rule support");
	}

	[Test]
	[Category("Unit")]
	[Description("GuidanceCatalog exposes business-rules so AI callers can retrieve business-rule authoring guidance by name.")]
	public void GuidanceCatalog_Should_Include_Business_Rules_Entry() {
		// Act
		bool found = GuidanceCatalog.TryGet("business-rules", out GuidanceCatalogEntry entry);

		// Assert
		found.Should().BeTrue(
			because: "the catalog must expose business-rules so get-guidance can return it by name");
		entry.Name.Should().Be("business-rules",
			because: "the catalog entry name must match the lookup key exactly");
		entry.Description.Should().Contain("business rules",
			because: "the catalog description should identify the subject of the guidance article");
		entry.Article.Should().NotBeNull(
			because: "the catalog entry must carry the guidance text article");
		entry.Article.Uri.Should().Be("docs://mcp/guides/business-rules",
			because: "the article URI in the catalog must match the resource URI");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a canonical MCP guidance article dedicated to the apply-static-filter friendly filter contract.")]
	public void BusinessRuleFiltersGuidanceResource_Should_Return_Canonical_Filter_Contract_Guide() {
		BusinessRuleFiltersGuidanceResource resource = new();

		ResourceContents result = resource.GetGuide();
		TextResourceContents article = result.Should().BeOfType<TextResourceContents>(
			because: "the filter-contract guide should be returned as a plain-text MCP resource").Subject;

		article.Uri.Should().Be("docs://mcp/guides/business-rule-filters",
			because: "the resource should expose a stable MCP URI for the filter contract");
		article.MimeType.Should().Be("text/plain");
		article.Text.Should().Contain("apply-static-filter",
			because: "the guide should identify itself as the apply-static-filter filter contract");
		article.Text.Should().Contain("backwardReferenceFilters",
			because: "the guide should document backward reference filters");
		article.Text.Should().Contain("aggregationType",
			because: "the guide should document COUNT/SUM/AVG/MIN/MAX aggregations");
		article.Text.Should().Contain("discovery flow",
			because: "the guide should keep the no-assumptions discovery flow");
		article.Text.Should().Contain("validate before create (MANDATORY)",
			because: "the guide must require an execute-esq dry-run of the filter before the rule is created");
		article.Text.Should().Contain("DRY-RUN the same filter as an `execute-esq` SelectQuery",
			because: "the guide must spell out the pre-save execute-esq validation discipline borrowed from the component widget recipe");
		article.Text.Should().Contain("before-create checklist",
			because: "the guide should end with a compact before-create checklist of the hard-won filter invariants");
	}

	[Test]
	[Category("Unit")]
	[Description("GuidanceCatalog exposes business-rule-filters so AI callers can retrieve the filter contract by name.")]
	public void GuidanceCatalog_Should_Include_Business_Rule_Filters_Entry() {
		bool found = GuidanceCatalog.TryGet("business-rule-filters", out GuidanceCatalogEntry entry);

		found.Should().BeTrue(
			because: "the catalog must expose business-rule-filters so get-guidance can return it by name");
		entry.Name.Should().Be("business-rule-filters",
			because: "the catalog entry name must match the lookup key exactly");
		entry.Article.Uri.Should().Be("docs://mcp/guides/business-rule-filters",
			because: "the article URI in the catalog must match the resource URI");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a canonical MCP guidance article for external server-to-server OAuth callers using client ID and client secret credentials.")]
	public void ServerToServerOAuthGuidanceResource_Should_Return_Canonical_Client_Credentials_Guide() {
		// Arrange
		ServerToServerOAuthGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = result.Should().BeOfType<TextResourceContents>(
			because: "the server-to-server OAuth guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/server-to-server-oauth",
			because: "the resource should expose a stable MCP URI for OAuth client-credentials guidance");
		article.MimeType.Should().Be("text/plain",
			because: "the OAuth guide should be discoverable as plain text");
		article.Text.Should().Contain("create-server-to-server-oauth-app",
			because: "the guide should connect credential usage to the MCP setup tool");
		article.Text.Should().Contain("/connect/token",
			because: "the guide should show the IdentityService token endpoint");
		article.Text.Should().Contain("grant_type=client_credentials",
			because: "the guide should show the exact OAuth grant used by server-to-server apps");
		article.Text.Should().Contain("Authorization: Bearer",
			because: "the guide should show how to send the access token to Creatio APIs");
		article.Text.Should().Contain("DataService/json/SyncReply/SelectQuery",
			because: "the guide should include a concrete Creatio API request example");
		article.Text.Should().Contain("does not use refresh tokens",
			because: "the guide should prevent callers from looking for a refresh-token flow that is not configured");
		article.Text.Should().Contain("mint a new token",
			because: "the guide should explain the correct token-expiry recovery path");
	}

	[Test]
	[Category("Unit")]
	[Description("GuidanceCatalog exposes server-to-server-oauth so AI callers can retrieve external OAuth credential usage guidance by name.")]
	public void GuidanceCatalog_Should_Include_Server_To_Server_OAuth_Entry() {
		// Act
		bool found = GuidanceCatalog.TryGet("server-to-server-oauth", out GuidanceCatalogEntry entry);

		// Assert
		found.Should().BeTrue(
			because: "the catalog must expose server-to-server-oauth so get-guidance can return it by name");
		entry.Name.Should().Be("server-to-server-oauth",
			because: "the catalog entry name must match the lookup key exactly");
		entry.Description.Should().Contain("client_credentials",
			because: "the catalog description should identify the server-to-server OAuth grant");
		entry.Article.Should().NotBeNull(
			because: "the catalog entry must carry the guidance text article");
		entry.Article.Uri.Should().Be("docs://mcp/guides/server-to-server-oauth",
			because: "the article URI in the catalog must match the resource URI");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a canonical MCP guidance article for managing package dependencies and the schema-designer HTML-error recovery (ENG-91314).")]
	public void PackageDependenciesGuidanceResource_Should_Return_Canonical_Recovery_Guide() {
		// Arrange
		PackageDependenciesGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = result.Should().BeOfType<TextResourceContents>(
			because: "the package-dependencies guide should be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/package-dependencies",
			because: "the resource should expose a stable MCP URI for package-dependency guidance");
		article.MimeType.Should().Be("text/plain",
			because: "the guide should be discoverable as plain text");
		article.Text.Should().Contain("GetSchemaDesignItem returned an HTML error page",
			because: "the guide must be keyed to the exact symptom an agent sees so it maps the error to this recovery");
		article.Text.Should().Contain("add-package-dependency",
			because: "the guide must point at the one-call recovery tool");
		article.Text.Should().Contain("remove-package-dependency",
			because: "the guide must document the symmetric cleanup tool");
		article.Text.Should().Contain("CrtLeadOppMgmtApp",
			because: "the guide should give the canonical Opportunity-layer example that misdirected agents before");
	}

	[Test]
	[Category("Unit")]
	[Description("GuidanceCatalog exposes package-dependencies so AI callers can retrieve the dependency-recovery guidance by name (ENG-91314).")]
	public void GuidanceCatalog_Should_Include_Package_Dependencies_Entry() {
		// Act
		bool found = GuidanceCatalog.TryGet("package-dependencies", out GuidanceCatalogEntry entry);

		// Assert
		found.Should().BeTrue(
			because: "the catalog must expose package-dependencies so get-guidance can return it by name");
		entry.Name.Should().Be("package-dependencies",
			because: "the catalog entry name must match the lookup key exactly");
		entry.Article.Uri.Should().Be("docs://mcp/guides/package-dependencies",
			because: "the article URI in the catalog must match the resource URI");
	}

	[Test]
	[Category("Unit")]
	[Description("The routing map points the package-dependencies symptom at its guide so an agent reaches it from the schema-designer failure (ENG-91314).")]
	public void RoutingGuidanceResource_Should_Route_Package_Dependencies_Symptom() {
		// Arrange
		RoutingGuidanceResource resource = new();

		// Act
		TextResourceContents article = resource.GetGuide().Should().BeOfType<TextResourceContents>().Subject;

		// Assert
		article.Text.Should().Contain("name=package-dependencies",
			because: "the routing map must direct the agent to the package-dependencies guide");
		article.Text.Should().Contain("GetSchemaDesignItem returned an HTML error page",
			because: "the routing row must be keyed to the exact symptom so the agent recognizes it");
	}

	[Test]
	[Category("Unit")]
	[Description("The routing map points the run-a-process-button task at the run-process-button guide so an agent reaches the shipped contract from the Pages domain.")]
	public void RoutingGuidanceResource_Should_Route_RunProcessButton_Task() {
		// Arrange
		RoutingGuidanceResource resource = new();

		// Act
		TextResourceContents article = resource.GetGuide().Should().BeOfType<TextResourceContents>().Subject;

		// Assert
		article.Text.Should().Contain("name=run-process-button",
			because: "the routing map must direct the agent to the run-process-button guide");
		article.Text.Should().Contain("runs a business process",
			because: "the routing row must be keyed to the task wording so the agent recognizes it");
	}
}
