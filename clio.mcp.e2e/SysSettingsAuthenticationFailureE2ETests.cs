using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for issue #1222: when Creatio rejects the credentials it serves its login page
/// - HTML, under HTTP 200 - in answer to the authenticated SelectQuery, and ATF.Repository's
/// <c>RemoteDataProvider</c> swallows the resulting parser failure into
/// <c>Success = false</c> + empty <c>Items</c>, which <c>AppDataContext</c> then drops to a plain empty
/// collection. The sys-settings MCP tools must report that as a structured failure instead of a
/// valid-looking empty read, and a write must not be attempted on unproven credentials.
/// </summary>
/// <remarks>
/// The unit tests around <c>SysSettingsCommand</c> prove exception-to-envelope mapping only. This
/// fixture drives the real clio MCP server process against a stub that reproduces the rejection on
/// the wire, so the whole path - transport, provider, <c>ClassifyingDataProvider</c>, classifier,
/// envelope - is exercised.
/// </remarks>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature("sys-setting")]
[NonParallelizable]
public sealed class SysSettingsAuthenticationFailureE2ETests {
	private const string RegisterToolName = "reg-web-app";
	private const string RejectedSchemaName = "SysSettings";
	private const string KnownPlatformSetting = "Maintainer";

	[Test]
	[AllureTag(SysSettingGetTool.GetSysSettingToolName)]
	[AllureName("get-sys-setting reports rejected credentials instead of an empty value")]
	[AllureDescription("Registers an environment against a stub whose authenticated SelectQuery answers with the Creatio login page, then verifies get-sys-setting returns success:false naming both possible causes rather than a successful empty read.")]
	[Description("get-sys-setting against an environment whose credentials Creatio rejects returns success:false naming both possible causes (rejected session / unreachable Creatio URL), not a valid-looking empty value. The READ path keeps only ATF's parser message, never the body, so it cannot prove which one it was.")]
	public async Task GetSysSetting_Should_Report_Authentication_Failure() {
		await RunAgainstCredentialRejectionStubAsync(async (session, environmentName, cancellationToken) => {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
				SysSettingGetTool.GetSysSettingToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["code"] = KnownPlatformSetting
					}
				},
				cancellationToken);
			SysSettingGetResult response = EntitySchemaStructuredResultParser.Extract<SysSettingGetResult>(callResult);

			// Assert
			response.Success.Should().BeFalse(
				because: "a rejected credential must not be reported as a successful sys-setting read - that "
				+ "false success is the defect issue #1222 describes");
			response.Error.Should().NotBeNullOrWhiteSpace(
				because: "the failure envelope has to carry a diagnostic the caller can act on");
			response.Error.Should().Contain("session was rejected",
				because: "an expired password is one of the two causes and the caller has to see it");
			response.Error.Should().Contain("proxy, gateway, wrong path",
				because: "on the READ path ATF keeps only the parser message and never the body, so a login "
				+ "page and a gateway error page are indistinguishable - the envelope must not claim one");
			response.Value.Should().BeNullOrEmpty(
				because: "no value was read, so none may be advertised alongside the failure");
		});
	}

	[Test]
	[AllureTag(SysSettingsListTool.ListSysSettingsToolName)]
	[AllureName("list-sys-settings reports rejected credentials instead of an empty catalog")]
	[AllureDescription("Registers an environment against a stub whose authenticated SelectQuery answers with the Creatio login page, then verifies list-sys-settings returns success:false rather than an empty catalog.")]
	[Description("list-sys-settings against an environment whose credentials Creatio rejects returns success:false, not an empty catalog that reads as 'this environment has no settings'.")]
	public async Task ListSysSettings_Should_Report_Authentication_Failure() {
		await RunAgainstCredentialRejectionStubAsync(async (session, environmentName, cancellationToken) => {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
				SysSettingsListTool.ListSysSettingsToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName
					}
				},
				cancellationToken);
			SysSettingsListResult response = EntitySchemaStructuredResultParser.Extract<SysSettingsListResult>(callResult);

			// Assert
			response.Success.Should().BeFalse(
				because: "an authentication-collapsed catalog read must be a failure, not an empty success");
			response.Error.Should().Contain("session was rejected",
				because: "an expired password is one of the two causes and the caller has to see it");
			response.Error.Should().Contain("proxy, gateway, wrong path",
				because: "on the READ path ATF keeps only the parser message and never the body, so a login "
				+ "page and a gateway error page are indistinguishable - the envelope must not claim one");
			response.Settings.Should().BeNullOrEmpty(
				because: "nothing was read, so no catalog may be advertised alongside the failure");
		});
	}

	[Test]
	[AllureTag(SysSettingCreateTool.CreateSysSettingToolName)]
	[AllureName("create-sys-setting fails closed on rejected credentials")]
	[AllureDescription("Registers an environment against a stub whose authenticated SelectQuery answers with the Creatio login page, then verifies create-sys-setting returns success:false and reports no created value.")]
	[Description("create-sys-setting against an environment whose credentials Creatio rejects fails closed: success:false with an authentication error and no reported creation.")]
	public async Task CreateSysSetting_Should_Fail_Closed_On_Rejected_Credentials() {
		await RunAgainstCredentialRejectionStubAsync(async (session, environmentName, cancellationToken) => {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
				SysSettingCreateTool.CreateSysSettingToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["code"] = "UsrClioAuthProbe1222",
						["name"] = "Clio auth probe 1222",
						["value-type-name"] = "Text",
						["value"] = "must-not-be-written"
					}
				},
				cancellationToken);
			SysSettingCreateResult response = EntitySchemaStructuredResultParser.Extract<SysSettingCreateResult>(callResult);

			// Assert
			response.Success.Should().BeFalse(
				because: "a write must not proceed on a session the environment rejected");
			response.Error.Should().Contain("Authentication",
				because: "the WRITE path still holds the raw response body, so a login page there proves the "
				+ "session was rejected - it is not the ambiguous non-JSON answer the read path has to report");
			response.Warning.Should().BeNull(
				because: "a fail-closed refusal is not a partial success and must not be softened into a warning");
		});
	}

	/// <summary>
	/// Stands up the isolated clio home, the credential-rejection stub, a real mcp-server session, and a
	/// registered environment pointing at the stub, then runs <paramref name="act"/> against them.
	/// </summary>
	private static async Task RunAgainstCredentialRejectionStubAsync(
		Func<McpServerSession, string, CancellationToken, Task> act) {
		string tempHome = Path.Combine(Path.GetTempPath(), $"clio-syssettings-auth-e2e-{Guid.NewGuid():N}");
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
			string environmentName = $"syssettings-auth-{Guid.NewGuid():N}";
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
			because: "the stub environment must register successfully before the sys-settings tools can be exercised against it");
	}
}
