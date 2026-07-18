using System.Text;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Knowledge;
using Clio.Command.McpServer.Resources;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[Category("E2E")]
[AllureNUnit]
[AllureFeature("external-knowledge-bundle")]
[NonParallelizable]
public sealed class KnowledgeGuidanceActiveBundleE2ETests : McpContractFixtureBase {
	private string? _conformanceRoot;

	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		_conformanceRoot = Environment.GetEnvironmentVariable("CLIO_KNOWLEDGE_CONFORMANCE_ROOT");
		if (string.IsNullOrWhiteSpace(_conformanceRoot)) {
			return;
		}
		settings.ProcessEnvironmentVariables[EnvironmentKnowledgeBundleActivator.BundlePathVariable] =
			Path.Combine(_conformanceRoot, "fixtures", "bundles", "esq-v0", "valid.zip");
		settings.ProcessEnvironmentVariables[EnvironmentKnowledgeBundleTrustStore.KeyIdVariable] = "p1-test";
		settings.ProcessEnvironmentVariables[EnvironmentKnowledgeBundleTrustStore.PublicKeyPathVariable] =
			Path.Combine(_conformanceRoot, "fixtures", "keys", "p1-test-public.pem");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("verified external ESQ bytes flow through the real MCP tool and resource")]
	[Description("Starts the real Clio MCP process and proves get-guidance and docs resource output match the external frozen oracle byte-for-byte.")]
	public async Task GuidanceSurfaces_ShouldMatchExternalOracle_WhenVerifiedBundleIsConfigured() {
		// Arrange
		if (string.IsNullOrWhiteSpace(_conformanceRoot)) {
			Assert.Ignore("Set CLIO_KNOWLEDGE_CONFORMANCE_ROOT to a clio-knowledge checkout.");
		}
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));
		byte[] expected = await File.ReadAllBytesAsync(
			Path.Combine(_conformanceRoot, "fixtures", "oracles", "esq", "resources", "esq-filters.md"),
			context.CancellationTokenSource.Token);

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			GuidanceGetTool.ToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?> { ["name"] = "esq-filters" } },
			context.CancellationTokenSource.Token);
		GuidanceGetResponse toolResponse = EntitySchemaStructuredResultParser.Extract<GuidanceGetResponse>(callResult);
		ReadResourceResult resourceResult = await context.Session.ReadResourceAsync(
			EsqFiltersGuidanceResource.ResourceUri,
			context.CancellationTokenSource.Token);
		TextResourceContents resource = resourceResult.Contents.Single().Should().BeOfType<TextResourceContents>(
			because: "the external docs resource must return one text article").Which;

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "a compatible verified bundle must be served");
		toolResponse.Success.Should().BeTrue(because: "get-guidance must resolve the active external article");
		Encoding.UTF8.GetBytes(toolResponse.Article!.Text).Should().Equal(expected,
			because: "the real tool surface must preserve the frozen bytes");
		Encoding.UTF8.GetBytes(resource.Text).Should().Equal(expected,
			because: "the real docs surface must preserve the same frozen bytes");
	}
}

[TestFixture]
[Category("E2E")]
[AllureNUnit]
[AllureFeature("external-knowledge-bundle")]
[NonParallelizable]
public sealed class KnowledgeGuidanceColdBundleE2ETests : McpContractFixtureBase {
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		settings.ProcessEnvironmentVariables[EnvironmentKnowledgeBundleActivator.BundlePathVariable] = null;
		settings.ProcessEnvironmentVariables[EnvironmentKnowledgeBundleTrustStore.KeyIdVariable] = null;
		settings.ProcessEnvironmentVariables[EnvironmentKnowledgeBundleTrustStore.PublicKeyPathVariable] = null;
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("cold external guidance returns a typed unavailable result")]
	[Description("Starts the real Clio MCP process without a bundle and proves get-guidance fails closed with a typed unavailable result.")]
	public async Task GetGuidance_ShouldReturnTypedUnavailable_WhenNoBundleIsConfigured() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			GuidanceGetTool.ToolName,
			new Dictionary<string, object?> { ["args"] = new Dictionary<string, object?> { ["name"] = "esq-filters" } },
			context.CancellationTokenSource.Token);
		GuidanceGetResponse response = EntitySchemaStructuredResultParser.Extract<GuidanceGetResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "typed guidance unavailability is a normal tool result rather than a process crash");
		response.Success.Should().BeFalse(because: "cold guidance cannot be treated as success");
		response.ErrorCode.Should().Be(KnowledgeGuidanceUnavailableException.ErrorCode,
			because: "agents must distinguish cold guidance from an unknown guide");
		response.Article.Should().BeNull(because: "cold guidance must never become permissive empty content");
	}
}
