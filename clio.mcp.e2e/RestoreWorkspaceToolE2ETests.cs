using System.Net;
using System.Net.Sockets;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for restore-workspace against an environment that does not answer, which needs no
/// Creatio: the registered URL is a local port nothing listens on.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(RestoreWorkspaceTool.RestoreWorkspaceToolName)]
public sealed class RestoreWorkspaceToolE2ETests {

	private const string EnvironmentName = "restore-workspace-unreachable";
	private const string PackageName = "UsrRestoreProbe";

	[Test]
	[AllureTag(RestoreWorkspaceTool.RestoreWorkspaceToolName)]
	[AllureName("restore-workspace fails with the reason when the environment does not answer")]
	[AllureDescription("Registers an environment on a closed local port in a clio home of the fixture's own, writes a workspace that lists one package, calls restore-workspace through the real MCP server, and verifies a non-zero exit code, an error that says nothing was downloaded and why, no Done line, and no package directory.")]
	[Description("Starts the real clio MCP server with an environment registered on a closed local port and a workspace that lists one package, calls restore-workspace, and verifies it fails with a non-zero exit code and an error naming why nothing was downloaded, instead of reporting Done with exit code 0 as it did when the ping failed silently.")]
	public async Task RestoreWorkspace_ShouldFailWithTheReason_WhenTheEnvironmentDoesNotAnswer() {
		// Arrange
		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-e2e-restore-{Guid.NewGuid():N}");
		string workspacePath = Path.Combine(tempHome, "workspace");
		Directory.CreateDirectory(Path.Combine(workspacePath, ".clio"));
		File.WriteAllText(Path.Combine(workspacePath, ".clio", "workspaceSettings.json"),
			$$"""{"ApplicationVersion":"8.0.0","Packages":["{{PackageName}}"]}""");
		try {
			McpE2ESettings settings = TestConfiguration.Load();
			settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
			settings.ProcessEnvironmentVariables[OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME"] = tempHome;
			// A home of this fixture's own, so the settings replacement below never rewrites the file other fixtures read.
			settings.ProcessEnvironmentVariables["CLIO_HOME"] = tempHome;
			using TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
				UnreachableEnvironmentSettings(ReserveClosedPort()), settings.ClioProcessPath,
				settings.ProcessEnvironmentVariables);
			using CancellationTokenSource scenario = new(TimeSpan.FromMinutes(3));
			await using McpServerSession session = await McpServerSession.StartAsync(settings, scenario.Token);

			// Act
			CallToolResult callResult = await session.CallToolAsync(
				RestoreWorkspaceTool.RestoreWorkspaceToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = EnvironmentName,
						["workspace-path"] = workspacePath
					}
				},
				scenario.Token);
			CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

			// Assert
			execution.ExitCode.Should().NotBe(0,
				because: "a restore that downloaded nothing must not report success to an agent or a script");
			execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Error
					&& message.Value != null && message.Value.Contains("Packages were not downloaded", StringComparison.Ordinal),
				because: "the error must say that nothing was restored and why");
			execution.Output.Should().NotContain(message => message.Value == "Done",
				because: "Done is what the silent failure used to print");
			Directory.Exists(Path.Combine(workspacePath, "packages", PackageName)).Should().BeFalse(
				because: "nothing is unpacked from an environment that did not answer");
		}
		finally {
			TryDeleteDirectory(tempHome);
		}
	}

	/// <summary>A local port that was free a moment ago and has no listener now.</summary>
	private static int ReserveClosedPort() {
		TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		int port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}

	private static string UnreachableEnvironmentSettings(int port) =>
		$$"""
		{
		  "ActiveEnvironmentKey": "{{EnvironmentName}}",
		  "Environments": {
		    "{{EnvironmentName}}": {
		      "Uri": "http://127.0.0.1:{{port}}",
		      "Login": "Supervisor",
		      "Password": "Supervisor",
		      "IsNetCore": true
		    }
		  }
		}
		""";

	private static void TryDeleteDirectory(string path) {
		try {
			if (Directory.Exists(path)) {
				Directory.Delete(path, recursive: true);
			}
		}
		catch (IOException) {
			// Best effort: a server process that has not exited yet can still hold a file in the temporary home.
		}
		catch (UnauthorizedAccessException) {
			// Same as above.
		}
	}
}
