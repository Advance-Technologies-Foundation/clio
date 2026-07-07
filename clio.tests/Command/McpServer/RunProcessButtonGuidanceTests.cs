using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class RunProcessButtonGuidanceTests {

	[Test]
	[Category("Unit")]
	[Description("Returns the run-process-button article; the guide documents the shipped run-process scenario and is not feature-gated.")]
	public async Task GuidanceGet_Should_Return_RunProcessButton_Article() {
		// Arrange
		IFeatureToggleService featureToggleService = Substitute.For<IFeatureToggleService>();
		GuidanceGetTool tool = new(featureToggleService);

		// Act
		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("run-process-button"));

		// Assert
		result.Success.Should().BeTrue(because: "run-process-button is ungated and must resolve with a bare toggle substitute");
		result.Article.Should().NotBeNull(
			because: "the ungated guide must return the resolved article");
		result.Article!.Uri.Should().Be("docs://mcp/guides/run-process-button",
			because: "the guidance tool should preserve the canonical run-process-button guide URI");
		result.Article.Text.Should().Contain("clio MCP run-process-button guide",
			because: "the guidance tool should return the canonical run-process-button article text");
		result.Article.Text.Should().Contain("get-process-signature",
			because: "the guide must require the signature lookup before authoring the button");
		result.Article.Text.Should().Contain("crt.RunBusinessProcessRequest",
			because: "the guide must name the request the button issues");
		result.Article.Text.Should().Contain("parameter CODE",
			because: "the guide must warn that the key is the code, not the caption");
	}
}
