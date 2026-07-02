using Clio.Command.McpServer.Resources;
using Clio.Command.McpServer.Resources.ProcessDesigner;
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
	[Description("The guidance recommends validate-process-graph before building and names the declarative create-business-process build.")]
	public void GetGuide_ShouldRecommendValidateBeforeBuild_WhenRead() {
		// Act
		string text = new ProcessModelingGuidanceResource().GetGuide().Should().BeOfType<TextResourceContents>().Subject.Text;

		// Assert
		text.Should().Contain("validate-process-graph",
			because: "the agent should pre-check the planned graph against R1-R17 before building");
		text.Should().Contain("create-business-process",
			because: "the build path is the declarative create-business-process call, not a CDP driver");
	}

	[Test]
	[Category("Unit")]
	[Description("The guidance scopes what is buildable today (start/signalStart/end + user tasks) and marks the rest as not yet buildable.")]
	public void GetGuide_ShouldScopeSupportedSlice_WhenRead() {
		// Act
		string text = new ProcessModelingGuidanceResource().GetGuide().Should().BeOfType<TextResourceContents>().Subject.Text;

		// Assert
		text.Should().Contain("signalStart",
			because: "the record-signal start is a buildable start element");
		text.Should().Contain("Read data",
			because: "Read data is a buildable user-task activity");
		text.Should().Contain("NOT yet buildable",
			because: "elements outside the supported slice must be explicitly marked as not yet buildable");
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
		text.Should().Contain("create-business-process",
			because: "the build recipe must name the declarative create-business-process call");
	}

	[Test]
	[Category("Unit")]
	[Description("The guidance steers record-triggered processes (run on save) to a signalStart element, not a page save handler.")]
	public void GetGuide_ShouldSteerRecordTriggerToSignalStart_WhenRead() {
		// Act
		string text = new ProcessModelingGuidanceResource().GetGuide().Should().BeOfType<TextResourceContents>().Subject.Text;

		// Assert
		text.Should().Contain("signalStart",
			because: "running a process on a record event is done with a signal start element");
		text.Should().Contain("crt.SaveRecordRequest",
			because: "the guidance must explicitly warn against using a page save handler to launch a process");
	}

	[Test]
	[Category("Unit")]
	[Description("The guidance teaches how to identify a mapping-source output from describe output (isResult true or direction Out) and warns that most user-task outputs report direction Variable.")]
	public void GetGuide_ShouldTeachOutputDetectionRule_WhenRead() {
		// Act
		string text = new ProcessModelingGuidanceResource().GetGuide().Should().BeOfType<TextResourceContents>().Subject.Text;

		// Assert
		text.Should().Contain("isResult",
			because: "an element output usable as a mapping source is identified by isResult — the rule was previously only in C# XML docs, invisible to the agent");
		text.Should().Contain("Variable",
			because: "the guidance must warn that most user-task outputs report direction Variable, so output detection cannot rely on direction alone");
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
