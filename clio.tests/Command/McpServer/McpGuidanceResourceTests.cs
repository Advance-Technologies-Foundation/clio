using Clio.Command.McpServer.Resources;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit tests for MCP guidance resources that expose cross-tool modeling rules.
/// </summary>
[TestFixture]
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
		article.Text.Should().Contain("component-info",
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
