using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Allure.Net.Commons;
using Clio.Common;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture, Category("McpE2E.NoEnvironment"), AllureNUnit, AllureFeature("uninstall-identity")]
public sealed class UninstallIdentityToolE2ETests {
	[TestCase(false, false), TestCase(false, true), TestCase(true, false)]
	[Description("Real MCP identity removal accepts empty metadata and refuses an incomplete attachment without changing CRM files or registration.")]
	[AllureTag("uninstall-identity"), AllureName("Identity attachment governs safe MCP removal")]
	[AllureDescription("Uses an isolated configuration and real MCP worker to prove explicit environment binding, recovery flag, destructive discovery and fail-closed attachment handling.")]
	public async Task Uninstall_ShouldPreserveCrm_WhenAttachmentIsEmptyOrIncomplete(bool incomplete, bool skip) {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		string home = IsolatedClioHome.CreateAndRedirect(settings, "clio-identity-removal");
		string crm = Path.Combine(home, "crm");
		Directory.CreateDirectory(crm);
		string sentinel = Path.Combine(crm, "retain.txt");
		File.WriteAllText(sentinel, "retained");
		string configPath = Path.Combine(home, "appsettings.json");
		string config = JsonSerializer.Serialize(new {
			ActiveEnvironmentKey = "chosen", Autoupdate = false, Features = new Dictionary<string, bool> { ["deploy-identity"] = true },
			Environments = new Dictionary<string, object> { ["chosen"] = new {
				Uri = "http://localhost:1", IsNetCore = true, EnvironmentPath = crm,
				IdentityService = new { EnvironmentPath = incomplete ? Path.Combine(home, "identity") : "" }
			} }
		});
		File.WriteAllText(configPath, config);
		using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(2));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, timeout.Token);
		// Act
		IReadOnlyList<ToolContractIndexEntry> index = await session.GetToolContractIndexAsync(timeout.Token);
		CallToolResult result = await AllureApi.Step("Invoke identity removal through the real worker", () => session.CallDestructiveAsync(
			UninstallIdentityTool.ToolName, new Dictionary<string, object?> { ["environment-name"] = "chosen", ["skip-crm-cleanup"] = skip }, timeout.Token));
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(result);
		// Assert
		AllureApi.Step("Verify destructive discovery", () => index.Should().ContainSingle(item => item.Name == UninstallIdentityTool.ToolName && item.Destructive == true,
			because: "identity removal must be discoverable as destructive"));
		AllureApi.Step("Verify attachment outcome", () => execution.ExitCode.Should().Be(incomplete ? 1 : 0,
			because: "empty metadata means no identity while partial metadata cannot authorize cleanup"));
		AllureApi.Step("Verify diagnostic classification", () => execution.Output.Should().Contain(message => message.MessageType ==
			(incomplete ? LogDecoratorType.Error : LogDecoratorType.Info), because: "the caller must receive actionable execution output"));
		AllureApi.Step("Verify CRM files survive", () => File.ReadAllText(sentinel).Should().Be("retained", because: "standalone cleanup cannot delete CRM files"));
		AllureApi.Step("Verify registration survives", () => JsonDocument.Parse(File.ReadAllText(configPath)).RootElement.GetProperty("Environments").TryGetProperty("chosen", out _)
			.Should().BeTrue(because: "standalone cleanup retains its owning environment"));
	}
}
