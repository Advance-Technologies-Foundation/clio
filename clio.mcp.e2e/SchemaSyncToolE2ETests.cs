using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Client;
using NUnit.Framework;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the schema-sync composite MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("schema-sync")]
[NonParallelizable]
public sealed class SchemaSyncToolE2ETests {

	private const string ToolName = SchemaSyncTool.ToolName;

	[Test]
	[Description("Advertises schema-sync MCP tool in the server tool list so callers can discover and invoke it.")]
	[AllureTag(ToolName)]
	[AllureName("schema-sync tool is advertised by the MCP server")]
	[AllureDescription("Verifies that schema-sync appears in the MCP server tool manifest.")]
	public async Task SchemaSyncTool_Should_Be_Listed_By_MCP_Server() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireEnvironment: false);

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);
		IEnumerable<string> toolNames = tools.Select(t => t.Name);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "schema-sync must be advertised so MCP clients can discover the composite tool");
	}

	private static async Task<ArrangeContext> ArrangeAsync(bool requireEnvironment) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string rootDirectory = Path.Combine(Path.GetTempPath(), $"clio-schema-sync-e2e-{System.Guid.NewGuid():N}");
		Directory.CreateDirectory(rootDirectory);
		string workspaceName = $"workspace-{System.Guid.NewGuid():N}";
		string workspacePath = Path.Combine(rootDirectory, workspaceName);
		CancellationTokenSource cancellationTokenSource = new(System.TimeSpan.FromMinutes(5));
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["create-workspace", workspaceName, "--empty", "--directory", rootDirectory],
			cancellationToken: cancellationTokenSource.Token);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(rootDirectory, workspacePath, session, cancellationTokenSource);
	}

	private sealed record ArrangeContext(
		string RootDirectory,
		string WorkspacePath,
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : System.IAsyncDisposable {

		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
			if (Directory.Exists(RootDirectory)) {
				Directory.Delete(RootDirectory, recursive: true);
			}
		}
	}
}
