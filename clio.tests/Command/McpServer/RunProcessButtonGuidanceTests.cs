using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class RunProcessButtonGuidanceTests {

	[Test]
	[Category("Unit")]
	public async Task GuidanceGet_Should_Return_RunProcessButton_Article() {
		GuidanceGetTool tool = new(new GuidanceAccessLedger());

		GuidanceGetResponse result = await tool.GetGuidance(new GuidanceGetArgs("run-process-button"));

		result.Success.Should().BeTrue(because: "run-process-button is a registered guidance name");
		result.Article.Should().NotBeNull();
		result.Article!.Uri.Should().Be("docs://mcp/guides/run-process-button");
		result.Article.Text.Should().Contain("clio MCP run-process-button guide");
		result.Article.Text.Should().Contain("get-process-signature",
			because: "the guide must require the signature lookup before authoring the button");
		result.Article.Text.Should().Contain("crt.RunBusinessProcessRequest");
		result.Article.Text.Should().Contain("parameter CODE",
			because: "the guide must warn that the key is the code, not the caption");
	}
}
