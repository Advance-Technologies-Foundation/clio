using Allure.NUnit;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;

namespace Clio.Mcp.E2E;

/// <summary>Exercises actual MCP discovery and invocation without a cluster.</summary>
[TestFixture(false, false), TestFixture(true, false), TestFixture(true, true), Category("McpE2E.NoEnvironment"), AllureNUnit, NonParallelizable]
public class StudioToolsContractE2ETests(bool enabled, bool host) : McpContractFixtureBase {
	private string _profile = "missing.json";
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		settings.RuntimeHost = host;
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = CreateIsolatedClioHome("{\"Autoupdate\":false,\"Features\":{\"runtime\":" + (enabled ? "true" : "false") + "},\"Environments\":{}}", GetType().Name);
		if (host) {
			_profile = Path.Combine(settings.ProcessEnvironmentVariables["CLIO_HOME"]!, "invalid-studio.json");
			File.WriteAllText(_profile, "{\"schema\":[],\"name\":{}}");
		}
	}
	[TestCase("studio-deploy"), TestCase("studio-checkout"), TestCase("studio-status")]
	[Description("Studio tools are feature-gated and refuse an MCP process without host opt-in.")]
	public async Task Tools_ShouldHonorFeatureAndHostBoundary(string name) {
		await using var context = Arrange();
		var index = await context.Session.GetToolContractIndexAsync(context.CancellationTokenSource.Token);
		if (!enabled) {
			index.Should().NotContain(t => t.Name == name, "disabled experimental tools must be undiscoverable");
			return;
		}
		index.Should().Contain(t => t.Name == name, "enabled tools must be discoverable through the actual server");
		var args = name switch {
			"studio-deploy" => new Dictionary<string, object?> { ["profile"] = _profile, ["context"] = "invalid" },
			"studio-checkout" => new Dictionary<string, object?> { ["profile"] = _profile, ["directory"] = "missing" },
			_ => new Dictionary<string, object?> { ["name"] = "INVALID", ["context"] = "invalid", ["namespaceName"] = "demo" }
		};
		var result = name == "studio-status" ? await context.Session.CallToolAsync(name, args, context.CancellationTokenSource.Token)
			: await context.Session.CallDestructiveAsync(name, args, context.CancellationTokenSource.Token);
		var execution = McpCommandExecutionParser.Extract(result);
		execution.ExitCode.Should().Be(1, "invalid requests must fail before any side effects");
		if (!host) execution.Output.Should().Contain(m => m.Value!.Contains("--runtime-host"), "the agent must receive actionable host configuration guidance");
		else execution.Output.Should().NotContain(m => m.Value!.Contains("--runtime-host"), "host opt-in must reach the actual command's validation");
		if (host && name != "studio-status") execution.Output.Should().Contain(m => m.Value!.Contains("Invalid Studio profile"), "malformed scalars must produce a structured failure rather than an unhandled exception");
	}
}
