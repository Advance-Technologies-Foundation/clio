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
		article.Text.Should().Contain("schema-sync",
			because: "the guide should steer callers toward the batch schema workflow");
		article.Text.Should().Contain("page-sync",
			because: "the guide should steer callers toward the batch page workflow");
		article.Text.Should().Contain("application-create",
			because: "the guide should explain the canonical app-creation entry point");
		article.Text.Should().Contain("docs://mcp/guides/existing-app-maintenance",
			because: "the creation-oriented guide should point callers to the dedicated existing-app maintenance guide for minimal edits");
		article.Text.Should().Contain("scalar-only for app shell fields",
			because: "the guide should state that application-create keeps shell fields as plain strings");
		article.Text.Should().Contain("Do not send localization-map fields",
			because: "the guide should prevent callers from mixing application-create with entity-schema localization maps");
		article.Text.Should().Contain("create the app first and then apply those captions through `schema-sync`",
			because: "the guide should steer callers toward follow-up schema tools when localized captions are needed");
		article.Text.Should().Contain("BaseLookup",
			because: "the guide should explain lookup inheritance and display-field behavior");
		article.Text.Should().Contain("schema default",
			because: "the guide should explain that seed rows alone do not satisfy default requirements");
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
		article.Text.Should().Contain("application-get-list",
			because: "the guide should start existing-app discovery from installed application lookup");
		article.Text.Should().Contain("application-get-info",
			because: "the guide should explain the follow-up app inspection step");
		article.Text.Should().Contain("page-list",
			because: "the guide should describe page discovery before inspection");
		article.Text.Should().Contain("page-get",
			because: "the guide should explain how callers inspect raw page bodies before editing");
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
		article.Text.Should().Contain("schema-sync",
			because: "the guide should explain when a larger ordered schema workflow is required");
		article.Text.Should().Contain("Read before write, and read back after mutations",
			because: "the guide should set the canonical verification discipline for existing-app edits");
	}
}
