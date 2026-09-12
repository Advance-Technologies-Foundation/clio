using System.Text.Json.Nodes;
using Allure.NUnit.Attributes;
using Allure.Net.Commons;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;

namespace Clio.Mcp.E2E;

// [AllureNUnit] is intentionally omitted. Long async lifecycle calls use explicit Allure steps.
[TestFixture, Category("McpE2E.Sandbox"), Category("McpE2E.Manual"), Category("LocalOnly"), NonParallelizable]
[Explicit("Removes an exclusively owned disposable CRM and identity; requires explicit smoke-home and destructive opt-in.")]
[AllureFeature("uninstall-identity")]
public sealed class IdentityUninstallLiveE2ETests {
	[Test]
	[Description("Removes a deployed OAuth identity, verifies retained CRM, redeploys identity and removes both through real MCP.")]
	[AllureTag("uninstall-identity", "deploy-identity", "uninstall-creatio")]
	[AllureName("Standalone and combined identity lifecycle on disposable IIS")]
	[AllureDescription("Requires CLIO_IDENTITY_SMOKE_HOME containing an exclusively owned lab registration and the configured sandbox name. The final call permanently removes that CRM database, IIS targets and folders.")]
	public async Task Uninstall_ShouldPreserveThenRemoveCrm_WhenStandaloneThenCombined() {
		// Arrange
		TeamCityRunGuard.IgnoreIfRunningUnderTeamCityOrGitHubActions("Destructive identity smoke is developer-local only.");
		if (!OperatingSystem.IsWindows()) { Assert.Ignore("This smoke validates IIS on Windows."); }
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) { Assert.Ignore("Enable destructive MCP tests for an exclusively owned disposable instance."); }
		string? smokeHome = Environment.GetEnvironmentVariable("CLIO_IDENTITY_SMOKE_HOME");
		smokeHome.Should().NotBeNullOrWhiteSpace(because: "a separate smoke home must explicitly identify the disposable configuration");
		string home = Path.GetFullPath(smokeHome!);
		home.Should().NotBe(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "creatio", "clio"),
			because: "the ordinary developer catalog is not a smoke-test target");
		string name = settings.Sandbox.EnvironmentName!;
		name.Should().NotBeNullOrWhiteSpace(because: "the destructive target must be explicit");
		string configPath = Path.Combine(home, "appsettings.json");
		JsonObject before = ReadEnvironment(configPath, name);
		string crmPath = before["EnvironmentPath"]!.GetValue<string>();
		JsonObject identity = before["IdentityService"]!.AsObject();
		string identityPath = identity["EnvironmentPath"]!.GetValue<string>();
		string identityName = identity["IisTarget"]!.GetValue<string>();
		Directory.Exists(crmPath).Should().BeTrue(because: "the smoke needs a real deployed CRM");
		Directory.Exists(identityPath).Should().BeTrue(because: "the smoke needs a real deployed identity");
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = home;
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(8));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, timeout.Token);
		Dictionary<string, object?> target = new() { ["environment-name"] = name };
		// Act
		CommandExecutionEnvelope standalone = McpCommandExecutionParser.Extract(await AllureApi.Step("Remove only identity", () =>
			session.CallDestructiveAsync("uninstall-identity", target, timeout.Token)));
		// Assert
		AllureApi.Step("Verify standalone success", () => standalone.ExitCode.Should().Be(0, because: "verified identity removal must succeed"));
		AllureApi.Step("Verify CRM retained", () => Directory.Exists(crmPath).Should().BeTrue(because: "standalone identity cleanup preserves CRM files"));
		AllureApi.Step("Verify identity removed", () => Directory.Exists(identityPath).Should().BeFalse(because: "the identity directory must be removed"));
		JsonObject retained = ReadEnvironment(configPath, name);
		AllureApi.Step("Verify matching credentials cleared", () => retained["ClientSecret"]!.GetValue<string>().Should().BeEmpty(because: "removed identity credentials must not remain active"));
		AllureApi.Step("Verify empty attachment", () => retained["IdentityService"]!["EnvironmentPath"]!.GetValue<string>().Should().BeEmpty(because: "successful cleanup leaves visible empties"));
		CommandExecutionEnvelope info = McpCommandExecutionParser.Extract(await session.CallToolAsync("describe-environment", target, timeout.Token));
		AllureApi.Step("Verify CRM database remains usable", () => info.ExitCode.Should().Be(0, because: "CRM must still answer after its identity is removed"));
		// Act
		CommandExecutionEnvelope deploy = McpCommandExecutionParser.Extract(await AllureApi.Step("Redeploy identity with no OAuth app", () =>
			session.CallDestructiveAsync("deploy-identity", new Dictionary<string, object?> {
				["environment-name"] = name, ["identitySiteName"] = identityName, ["identityPath"] = identityPath, ["noApp"] = true
			}, timeout.Token)));
		// Assert
		AllureApi.Step("Verify redeployment", () => deploy.ExitCode.Should().Be(0, because: "the retained CRM can receive a new identity component"));
		// Act
		CommandExecutionEnvelope combined = McpCommandExecutionParser.Extract(await AllureApi.Step("Remove CRM with its recorded identity", () =>
			session.CallDestructiveAsync("uninstall-creatio", target, timeout.Token)));
		// Assert
		AllureApi.Step("Verify combined success", () => combined.ExitCode.Should().Be(0, because: "both components must be removed in one operation"));
		AllureApi.Step("Verify both folders removed", () => new[] { crmPath, identityPath }.Should().OnlyContain(path => !Directory.Exists(path),
			because: "combined removal must remove exactly the two recorded deployment directories"));
		AllureApi.Step("Verify final unregister", () => JsonNode.Parse(File.ReadAllText(configPath))!["Environments"]!.AsObject().ContainsKey(name)
			.Should().BeFalse(because: "registration is removed only after combined cleanup completes"));
	}

	private static JsonObject ReadEnvironment(string file, string name) => JsonNode.Parse(File.ReadAllText(file))!["Environments"]![name]!.AsObject();
}
