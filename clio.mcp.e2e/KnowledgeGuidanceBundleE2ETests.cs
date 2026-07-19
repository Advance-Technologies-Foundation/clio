using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Knowledge;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("persistent-knowledge-cache")]
[NonParallelizable]
public sealed class KnowledgeGuidanceColdBundleE2ETests : McpContractFixtureBase {
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = CreateIsolatedClioHome("{}", "cold-knowledge-home");
		settings.ProcessEnvironmentVariables[KnowledgeBundleNuGetClient.SourceVariable] = null;
		settings.ProcessEnvironmentVariables[KnowledgeBundleNuGetClient.PackageIdVariable] = null;
		settings.ProcessEnvironmentVariables[EnvironmentKnowledgeBundleTrustStore.KeyIdVariable] = null;
		settings.ProcessEnvironmentVariables[EnvironmentKnowledgeBundleTrustStore.PublicKeyPathVariable] = null;
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("cold disk knowledge is typed unavailable while executable safety remains discoverable")]
	[Description("Starts the real MCP process with an empty isolated cache and proves guidance fails closed without weakening executable safety metadata.")]
	public async Task ColdCache_ShouldReturnTypedUnavailable_AndRetainExecutableSafetyMetadata() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			GuidanceGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["name"] = "esq-filters" }
			},
			context.CancellationTokenSource.Token);
		GuidanceGetResponse response = EntitySchemaStructuredResultParser.Extract<GuidanceGetResponse>(callResult);
		IList<McpClientResource> resources = await context.Session.ListResourcesAsync(
			context.CancellationTokenSource.Token);
		Exception? resourceReadException = null;
		try {
			await context.Session.ReadResourceAsync(
				"docs://mcp/guides/esq-filters",
				context.CancellationTokenSource.Token);
		} catch (Exception exception) {
			resourceReadException = exception;
		}
		IReadOnlyList<ToolContractIndexEntry> contracts = await context.Session.GetToolContractIndexAsync(
			context.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "typed unavailability is a normal MCP result rather than a process crash");
		response.Success.Should().BeFalse(because: "an empty cache cannot be treated as active guidance");
		response.ErrorCode.Should().Be(KnowledgeGuidanceUnavailableException.ErrorCode,
			because: "agents must distinguish unavailable knowledge from an unknown active article");
		response.Article.Should().BeNull(because: "cold guidance must never become permissive empty content");
		resources.Select(resource => resource.Uri).Should().Contain("docs://mcp/guides/esq-filters",
			because: "external resource discovery remains stable even before knowledge is installed");
		resourceReadException.Should().NotBeNull(
			because: "resources/read must fail closed when no verified disk bundle is active");
		resourceReadException!.Message.Should().Contain(KnowledgeGuidanceUnavailableException.ErrorCode,
			because: "the real MCP resource error must retain the typed unavailable code for agents");
		ToolContractIndexEntry destructiveExecutor = contracts.Should().ContainSingle(
			entry => entry.Name == ClioRunDestructiveTool.ToolName,
			because: "the executable destructive dispatch boundary must remain discoverable without knowledge").Which;
		destructiveExecutor.Destructive.Should().BeTrue(
			because: "disk knowledge availability must not control host-enforced destructive metadata");
	}
}
