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
			//Issue #1329: the envelope has to carry the classified parts, not only the one-line message.
			response.ErrorCategory.Should().Be(SysSettingErrorCategories.ProviderFailure,
				because: "the read path cannot prove which of the two causes it was, so it reports the "
				+ "provider verdict rather than claiming a credential rejection");
			response.Cause.Should().NotBeNullOrWhiteSpace(
				because: "the actionable cause used to be discarded (issue #1329)");
			response.RecoveryAction.Should().NotBeNullOrWhiteSpace(
				because: "the envelope must name the caller's next step");
			response.CorrelationId.Should().NotBeNullOrWhiteSpace(
				because: "#1222 requires a correlation ID so the failure can be matched to the log line");
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
			response.ErrorCategory.Should().Be(SysSettingErrorCategories.ProviderFailure,
				because: "a list failure declares its category so an agent branches on it (issue #1329)");
			response.CorrelationId.Should().NotBeNullOrWhiteSpace(
				because: "#1222 names list failures specifically as needing a correlation ID");
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
			//Issue #1333: the raw body was embedded in this diagnostic and reached the MCP envelope.
			response.Error.Should().NotContain("<html",
				because: "the login page's markup - and anything a third party put inside it - must not "
				+ "travel on a field an AI agent reads as part of its own context");
			response.Cause.Should().NotContain("<html",
				because: "the cause is a fixed local diagnostic, never composed from server prose");
			response.Warning.Should().BeNull(
				because: "a fail-closed refusal is not a partial success and must not be softened into a warning");
			response.ErrorCategory.Should().Be(SysSettingErrorCategories.Authentication,
				because: "the write path proves the session was rejected, so the category is definite (issue #1329)");
			response.Cause.Should().NotBeNullOrWhiteSpace(
				because: "#1222 names create failures specifically as needing an actionable cause");
			response.RecoveryAction.Should().NotBeNullOrWhiteSpace(
				because: "the envelope must name the caller's next step");
			response.CorrelationId.Should().NotBeNullOrWhiteSpace(
				because: "#1222 names create failures specifically as needing a correlation ID");
		});
	}

	/// <summary>
	/// Stands up the isolated clio home, the credential-rejection stub, a real mcp-server session, and a
	/// registered environment pointing at the stub, then runs <paramref name="act"/> against them.
	/// </summary>
	[TestCase("Text", "must-not-be-written",
		TestName = "UpdateSysSetting_Should_Fail_Closed_On_Rejected_Credentials(Text)")]
	[TestCase("Lookup", "b80eb7bb-193c-4bb2-ad51-e0beb1670278",
		TestName = "UpdateSysSetting_Should_Fail_Closed_On_Rejected_Credentials(Lookup)")]
	[AllureTag(SysSettingUpdateTool.UpdateSysSettingToolName)]
	[AllureName("update-sys-setting fails closed on rejected credentials")]
	[AllureDescription("Registers an environment against a stub whose authenticated SelectQuery answers with the Creatio login page, then verifies update-sys-setting returns success:false and reports no written value, for both a Text and a Lookup setting.")]
	[Description("AC3's update half: update-sys-setting against an environment whose credentials Creatio rejects fails closed for Text and for Lookup. WHERE the rejection is raised, stated precisely (PR #1372 review): the update path reads before it writes - PrepareUpdateValue calls GetAllUsersDefaultWithType and SysSettingsManager.UpdateSysSetting calls GetSysSettingByCode, both SelectQuery reads on SysSettings, which this stub rejects - so the throw happens on the READ step and the PostSysSettingsValues endpoint is never reached. This test therefore proves the fail-closed envelope of the update tool, NOT the write-path ThrowIfSessionRejected sitting inside the pre-existing catch (JsonException). Covering that interaction needs a stub mode that lets the SysSettings reads through and rejects only the write endpoints; it is tracked as a follow-up rather than claimed here.")]
	public async Task UpdateSysSetting_Should_Fail_Closed_On_Rejected_Credentials(string valueTypeName, string value) {
		await RunAgainstCredentialRejectionStubAsync(async (session, environmentName, cancellationToken) => {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
				SysSettingUpdateTool.UpdateSysSettingToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["code"] = KnownPlatformSetting,
						["value-type-name"] = valueTypeName,
						["value"] = value
					}
				},
				cancellationToken);
			SysSettingUpdateResult response = EntitySchemaStructuredResultParser.Extract<SysSettingUpdateResult>(callResult);

			// Assert
			response.Success.Should().BeFalse(
				because: "a write must not proceed on a session the environment rejected - reporting it as updated is the same false success issue #1222 describes for reads");
			response.Error.Should().NotBeNullOrWhiteSpace(
				because: "the failure envelope has to carry a diagnostic the caller can act on");
			response.Error.Should().NotContain("Invalid response format",
				because: "the login page proves the session was rejected, so the diagnostic must name that and not degrade into a shapeless parser complaint - here on the READ step, since that is where this stub raises it (see the Description)");
			response.Value.Should().BeNullOrEmpty(
				because: "nothing was written, so no value may be advertised alongside the failure");
		});
	}

	private static Task RunAgainstCredentialRejectionStubAsync(
		Func<McpServerSession, string, CancellationToken, Task> act)
		=> CredentialRejectionStubHarness.RunAsync("syssettings-auth", act);
}
