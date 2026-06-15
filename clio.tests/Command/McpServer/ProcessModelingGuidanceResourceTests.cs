using Clio.Command.McpServer.Resources;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit tests for the <c>process-modeling</c> MCP guidance resource and its
/// <see cref="GuidanceCatalog"/> registration (Story 2 of ai-business-process-generation).
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ProcessModelingGuidanceResourceTests {

	[Test]
	[Category("Unit")]
	[Description("The process-modeling resource returns a plain-text guidance article on the canonical docs URI.")]
	public void GetGuide_ShouldReturnPlainTextArticleOnCanonicalUri_WhenCalled() {
		// Arrange
		ProcessModelingGuidanceResource resource = new();

		// Act
		ResourceContents result = resource.GetGuide();
		TextResourceContents article = result.Should().BeOfType<TextResourceContents>(
			because: "the modeling guide must be returned as a plain-text MCP resource").Subject;

		// Assert
		article.Uri.Should().Be("docs://mcp/guides/process-modeling",
			because: "the resource must expose a stable MCP URI for the process-modeling guidance");
		article.MimeType.Should().Be("text/plain",
			because: "the guide should be discoverable as plain text");
		article.Text.Should().NotBeNullOrWhiteSpace(
			because: "the guidance article must carry content");
	}

	[Test]
	[Category("Unit")]
	[Description("The guidance states the determinism contract: clio makes no LLM call and the agent owns intent->BPMN translation.")]
	public void GetGuide_ShouldStateDeterminismContract_WhenRead() {
		// Act
		string text = new ProcessModelingGuidanceResource().GetGuide().Should().BeOfType<TextResourceContents>().Subject.Text;

		// Assert
		text.Should().Contain("clio makes no LLM call",
			because: "the agent must know clio performs no LLM calls (research's fixed agent-as-LLM decision)");
		text.Should().Contain("own the intent->BPMN translation",
			because: "the agent must know it owns translating the request into BPMN intent");
	}

	[Test]
	[Category("Unit")]
	[Description("The guidance instructs validate-before-drive and names .djs-validate-outline as the designer's final authority.")]
	public void GetGuide_ShouldInstructValidateBeforeDrive_WhenRead() {
		// Act
		string text = new ProcessModelingGuidanceResource().GetGuide().Should().BeOfType<TextResourceContents>().Subject.Text;

		// Assert
		text.Should().Contain("validate-process-graph",
			because: "the agent must call validate-process-graph before driving the designer");
		text.Should().Contain(".djs-validate-outline",
			because: "the guide must name the live designer's invalid-connection marker as the final authority");
	}

	[Test]
	[Category("Unit")]
	[Description("The guidance scopes the drivable slice (Simple/Signal/Timer start + Read data) and marks other elements as not-yet-drivable.")]
	public void GetGuide_ShouldScopeSupportedSlice_WhenRead() {
		// Act
		string text = new ProcessModelingGuidanceResource().GetGuide().Should().BeOfType<TextResourceContents>().Subject.Text;

		// Assert
		text.Should().Contain("Simple/Signal/Timer start",
			because: "the supported start triggers for the slice must be stated");
		text.Should().Contain("Read data",
			because: "Read data is the drivable activity for this increment");
		text.Should().Contain("described for context, not yet drivable",
			because: "non-slice elements must be explicitly marked as not yet drivable by clio");
	}

	[Test]
	[Category("Unit")]
	[Description("The guidance consolidates the element catalog, the connection rules R1-R17, and the build recipe.")]
	public void GetGuide_ShouldConsolidateCatalogRulesAndRecipe_WhenRead() {
		// Act
		string text = new ProcessModelingGuidanceResource().GetGuide().Should().BeOfType<TextResourceContents>().Subject.Text;

		// Assert
		text.Should().Contain("readDataUserTask",
			because: "the element catalog must carry the data-id vocabulary the validator and driver use");
		text.Should().Contain("R1 ",
			because: "the connection rules section must start at R1");
		text.Should().Contain("R17",
			because: "the connection rules section must run through R17");
		text.Should().Contain("add.serviceTask",
			because: "the build recipe must describe the context-pad append mechanic");
	}

	[Test]
	[Category("Unit")]
	[Description("GuidanceCatalog exposes process-modeling so get-guidance can return it by canonical name.")]
	public void GuidanceCatalog_ShouldIncludeProcessModelingEntry_WhenQueried() {
		// Act
		bool found = GuidanceCatalog.TryGet("process-modeling", out GuidanceCatalogEntry entry);

		// Assert
		found.Should().BeTrue(
			because: "the catalog must expose process-modeling so get-guidance can return it by name");
		entry.Article.Uri.Should().Be("docs://mcp/guides/process-modeling",
			because: "the catalog entry must point at the same canonical resource URI");
		entry.Article.Text.Should().Contain("clio makes no LLM call",
			because: "the catalog must serve the same determinism-contract content as the resource");
	}
}
