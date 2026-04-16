using Clio.Command.McpServer.Resources;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

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
		TextResourceContents article = (TextResourceContents)result;

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
		article.Text.Should().Contain("docs://mcp/guides/existing-app-maintenance",
			because: "the creation-oriented guide should point callers to the dedicated existing-app maintenance guide for minimal edits");
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
		TextResourceContents article = (TextResourceContents)result;

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
		article.Text.Should().Contain("$PDS_*",
			because: "the guide should steer standard form fields toward direct datasource-backed bindings");
		article.Text.Should().Contain("$UsrStatus",
			because: "the guide should call out proxy field bindings that are now rejected");
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
	[Description("Returns handler guidance that keeps Freedom UI request-chain logic inside clio-owned page-body instructions.")]
	public void PageSchemaHandlersGuidanceResource_Should_Return_Canonical_Handler_Guide() {
		// Arrange
		PageSchemaHandlersGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = (TextResourceContents)result;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/page-schema-handlers",
			because: "the handler guide should expose a stable MCP URI for handler authoring guidance");
		article.MimeType.Should().Be("text/plain",
			because: "the handler guide should be discoverable as plain text");
		article.Text.Should().Contain("raw.body",
			because: "handler guidance should keep deployed page edits anchored to the raw page body returned by get-page");
		article.Text.Should().Contain("SCHEMA_HANDLERS",
			because: "handler guidance should point callers to the marker-delimited handler section");
		article.Text.Should().Contain("request.$context.executeRequest(...)",
			because: "handler guidance should describe the canonical way to launch secondary requests from a handler");
		article.Text.Should().Contain("next?.handle(request)",
			because: "handler guidance should explain how the request chain continues");
		article.Text.Should().Contain("setAttributePropertyValue(...)",
			because: "handler guidance should steer dynamic UI state to the supported context API");
		article.Text.Should().Contain("@CrtRequestHandler",
			because: "handler guidance should document the public frontend-source registration pattern alongside page-body editing");
		article.Text.Should().Contain("HttpClientService",
			because: "handler guidance should list the main public sdk services available to handler authors");
		article.Text.Should().Contain("Do not use a handler for pure display transformation tasks such as \"add a label that shows Name in uppercase\"",
			because: "handler guidance should explicitly reject using handlers for uppercase display-label scenarios");
		article.Text.Should().Contain("https://academy.creatio.com/docs/8.x/dev/development-on-creatio-platform/front-end-development/freedom-ui/client-schema-freedomui/overview",
			because: "handler guidance should link to the relevant Academy overview for deeper reference");
		article.Text.Should().Contain("docs://mcp/guides/page-schema-converters",
			because: "handler guidance should redirect value-transformation work to the converter guide");
		article.Text.Should().Contain("docs://mcp/guides/page-schema-validators",
			because: "handler guidance should redirect field-validation work to the validator guide");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns converter guidance that keeps value transformation separate from handlers and validators in clio MCP page editing.")]
	public void PageSchemaConvertersGuidanceResource_Should_Return_Canonical_Converter_Guide() {
		// Arrange
		PageSchemaConvertersGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = (TextResourceContents)result;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/page-schema-converters",
			because: "the converter guide should expose a stable MCP URI for converter authoring guidance");
		article.Text.Should().Contain("SCHEMA_CONVERTERS",
			because: "converter guidance should point callers to the marker-delimited converter section");
		article.Text.Should().Contain("value transformation",
			because: "converter guidance should define the intended responsibility of converters");
		article.Text.Should().Contain("usr.ToUpperCase",
			because: "converter guidance should include a concrete Academy-style converter example");
		article.Text.Should().Contain("$UsrName | usr.ToUpperCase",
			because: "converter guidance should show how a page binding references the converter");
		article.Text.Should().Contain("add another label that shows Name in uppercase",
			because: "converter guidance should directly cover the uppercase display-label scenario that previously drifted to handlers");
		article.Text.Should().Contain("UsrUppercaseNameLabel",
			because: "converter guidance should include a copyable mini diff for the uppercase label scenario");
		article.Text.Should().Contain("\"type\": \"crt.Label\"",
			because: "converter guidance should show the label component shape used in the cookbook diff");
		article.Text.Should().Contain("parentName",
			because: "converter guidance should show that the inserted label must be positioned in the live container hierarchy");
		article.Text.Should().Contain("@CrtConverter",
			because: "converter guidance should document the public frontend-source registration pattern");
		article.Text.Should().Contain("Converter<V, R>",
			because: "converter guidance should point to the public converter contract in the devkit API");
		article.Text.Should().Contain("implement-the-field-value-conversion",
			because: "converter guidance should include the Academy field-value conversion example link");
		article.Text.Should().Contain("save interception",
			because: "converter guidance should explicitly reject side-effect responsibilities");
		article.Text.Should().Contain("Do not introduce a handler that writes a second attribute only to display an uppercase",
			because: "converter guidance should explicitly reject handler-based shadow-attribute solutions for pure display conversion");
		article.Text.Should().Contain("docs://mcp/guides/page-schema-handlers",
			because: "converter guidance should redirect lifecycle and request-chain logic to handlers");
		article.Text.Should().Contain("docs://mcp/guides/page-schema-validators",
			because: "converter guidance should redirect validation work to validators");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns validator guidance that keeps field validation separate from converters and handlers in clio MCP page editing.")]
	public void PageSchemaValidatorsGuidanceResource_Should_Return_Canonical_Validator_Guide() {
		// Arrange
		PageSchemaValidatorsGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = (TextResourceContents)result;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/page-schema-validators",
			because: "the validator guide should expose a stable MCP URI for validator authoring guidance");
		article.Text.Should().Contain("SCHEMA_VALIDATORS",
			because: "validator guidance should point callers to the marker-delimited validator section");
		article.Text.Should().Contain("field-value validation",
			because: "validator guidance should define the intended responsibility of validators");
		article.Text.Should().Contain("usr.ValidateFieldValue",
			because: "validator guidance should include a concrete page-body validator example");
		article.Text.Should().Contain("\"validator\": function",
			because: "validator guidance should show the runtime page-body validator function shape");
		article.Text.Should().Contain("@CrtValidator",
			because: "validator guidance should document the public frontend-source registration pattern");
		article.Text.Should().Contain("BaseValidator",
			because: "validator guidance should point to the main public validator base type in the devkit API");
		article.Text.Should().Contain("implement-the-field-value-validation",
			because: "validator guidance should include the Academy field-value validation example link");
		article.Text.Should().Contain("setAttributePropertyValue(...)",
			because: "validator guidance should redirect dynamic UI state rules to the supported handler-side API");
		article.Text.Should().Contain("docs://mcp/guides/page-schema-handlers",
			because: "validator guidance should redirect request-chain and dynamic UI state logic to handlers");
		article.Text.Should().Contain("docs://mcp/guides/page-schema-converters",
			because: "validator guidance should redirect value transformation work to converters");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns DataForge orchestration guidance that keeps exact package-local reuse checks on runtime context before DataForge fallback.")]
	public void DataForgeOrchestrationGuidanceResource_Should_Keep_Runtime_Context_As_Primary_Source_For_Existing_App_Reuse() {
		// Arrange
		DataForgeOrchestrationGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = (TextResourceContents)result;

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
}
