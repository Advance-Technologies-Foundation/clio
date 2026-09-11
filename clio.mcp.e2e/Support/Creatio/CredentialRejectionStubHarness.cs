using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Creatio;

/// <summary>
/// Stands up an isolated clio home, a stub Creatio that answers the authenticated SelectQuery (and the
/// sys-settings write endpoints) with the login page, a real mcp-server session and an environment
/// registered against that stub, then runs the supplied body against them.
/// </summary>
/// <remarks>
/// Extracted so more than one fixture can drive the same rejection on the wire: the sys-settings tools
/// and get-schema-name-prefix all read through the same provider, and each one has to prove it reports a
/// structured failure rather than a valid-looking empty read. Duplicating the setup per fixture would
/// have meant three copies of the temp-home, stub and registration dance.
/// </remarks>
internal static class CredentialRejectionStubHarness {

	private const string RegisterToolName = "reg-web-app";

	/// <summary>
	/// The schema whose authenticated SelectQuery the stub answers with the Creatio login page. Every
	/// sys-settings read - and get-schema-name-prefix, which reads the SchemaNamePrefix setting - goes
	/// through it.
	/// </summary>
	public const string RejectedSchemaName = "SysSettings";

	public static async Task RunAsync(
		string environmentNamePrefix,
		Func<McpServerSession, string, CancellationToken, Task> act) {
		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-{environmentNamePrefix}-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempHome);
		try {
			string envVarName = OperatingSystem.IsWindows() ? "LOCALAPPDATA" : "HOME";
			McpE2ESettings settings = TestConfiguration.Load();
			settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
			settings.ProcessEnvironmentVariables[envVarName] = tempHome;
			using TemporaryClioSettingsOverride settingsOverride = TemporaryClioSettingsOverride.ReplaceContent(
				"""
				{
				  "ActiveEnvironmentKey": null,
				  "Environments": {}
				}
				""",
				settings.ClioProcessPath,
				settings.ProcessEnvironmentVariables);
			await using RuntimeDetectionStubServer stubServer = RuntimeDetectionStubServer.Start(
				new RuntimeDetectionStubServerConfiguration(
					NetCoreHealthEnabled: true,
					NetFrameworkHealthEnabled: true,
					NetCoreServiceEnabled: true,
					NetFrameworkServiceEnabled: false,
					NetCoreUiMarkerEnabled: true,
					NetFrameworkUiMarkerEnabled: false,
					AuthRejectedSelectQuerySchemaName: RejectedSchemaName));
			using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
			await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
			string environmentName = $"{environmentNamePrefix}-{Guid.NewGuid():N}";
			await RegisterEnvironmentAsync(session, environmentName, stubServer.BaseUrl, cancellationTokenSource.Token);

			await act(session, environmentName, cancellationTokenSource.Token);
		} finally {
			TryDeleteDirectory(tempHome);
		}
	}

	private static void TryDeleteDirectory(string path) {
		try {
			if (Directory.Exists(path)) {
				Directory.Delete(path, recursive: true);
			}
		} catch {
			// Best-effort cleanup of the isolated home directory; a leaked temp dir must not fail the test.
		}
	}

	private static async Task RegisterEnvironmentAsync(
		McpServerSession session,
		string environmentName,
		string baseUrl,
		CancellationToken cancellationToken) {
		IReadOnlyCollection<string> toolNames = await session.ListReachableToolNamesAsync(cancellationToken);
		toolNames.Should().Contain(RegisterToolName,
			because: $"the {RegisterToolName} MCP tool must be discoverable before the test can register the stub environment");

		CallToolResult registerResult = await session.CallToolAsync(
			RegisterToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["uri"] = baseUrl,
					["login"] = "Supervisor",
					["password"] = "Supervisor"
				}
			},
			cancellationToken);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(registerResult);
		execution.ExitCode.Should().Be(0,
			because: "the stub environment must register successfully before the tools can be exercised against it");
	}
}
