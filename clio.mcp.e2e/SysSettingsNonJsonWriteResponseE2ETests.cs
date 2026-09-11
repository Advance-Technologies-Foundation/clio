using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage for issue #1378: the sys-settings WRITE endpoints answer with a gateway page
/// while every read is served normally - the shape <c>ThrowIfSessionRejected</c> deliberately does not
/// fire on, because the body proves nothing about the session.
/// </summary>
/// <remarks>
/// This is the follow-up <see cref="SysSettingsAuthenticationFailureE2ETests"/> names and does not
/// claim: rejecting the session rejects the reads too, so the write endpoint is never reached there and
/// the interaction could not be exercised. What changed for an agent is the UPDATE envelope - a gateway
/// page used to be swallowed into a <c>false</c> reported as <c>ProviderFailure</c> / "the setting may
/// not exist, or the value did not match its type", a claim about the setting that the body does not
/// support - and it now carries the <c>Network</c> category and the non-JSON cause. The create envelope
/// is deliberately byte-identical to what the bare parser fault already produced, and is covered here
/// because its LEAK surface is not: the create tool surfaces a different result record.
/// </remarks>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature("sys-setting")]
[NonParallelizable]
public sealed class SysSettingsNonJsonWriteResponseE2ETests {

	private const string KnownPlatformSetting = "Maintainer";

	/// <summary>The credential the stub's gateway page hides, which must not appear anywhere in the envelope.</summary>
	private const string PlantedCredential = "hunter2";

	/// <summary>A right-to-left override, which reorders whatever a terminal or a transcript renders after it.</summary>
	private const string PlantedBidiControl = "‮";

	[Test]
	[AllureTag(SysSettingUpdateTool.UpdateSysSettingToolName)]
	[AllureName("update-sys-setting reports a non-JSON write answer as a Network failure")]
	[AllureDescription("Registers an environment against a stub that serves reads normally and answers the sys-settings write endpoints with a gateway page, then verifies update-sys-setting reports a Network failure naming the non-JSON answer rather than claiming the environment refused the value, and that nothing from the page reaches any part of the tool result.")]
	[Description("A gateway page answering PostSysSettingsValues is reported as Network with the non-JSON cause, carries a correlation id and a recovery action, and no part of the page - marker, planted credential or bidi control - appears anywhere in the serialized tool result.")]
	public async Task UpdateSysSetting_ShouldReportANonJsonAnswer_WhenTheWriteEndpointServesAGatewayPage() {
		await RunAgainstNonJsonWriteStubAsync(async (session, environmentName, cancellationToken) => {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
				SysSettingUpdateTool.UpdateSysSettingToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["code"] = KnownPlatformSetting,
						["value-type-name"] = "Text",
						["value"] = "must-not-be-written"
					}
				},
				cancellationToken);
			SysSettingUpdateResult response = EntitySchemaStructuredResultParser.Extract<SysSettingUpdateResult>(callResult);

			// Assert
			response.Success.Should().BeFalse(
				because: "a gateway page is not an acknowledgement, so the write must not be reported as applied");
			response.ErrorCategory.Should().Be(SysSettingErrorCategories.Network,
				because: "the request never reached Creatio - reporting ProviderFailure would claim the environment "
				+ "itself refused the value, which is what the swallowed `false` used to say");
			response.Cause.Should().Contain("not JSON",
				because: "the cause has to name what actually happened instead of the setting-shaped guess it replaced");
			response.Error.Should().NotContain("Invalid response format",
				because: "that shapeless line is exactly what issue #1378 replaces - it named neither the operation nor what arrived");
			response.RecoveryAction.Should().NotBeNullOrWhiteSpace(
				because: "an agent needs the next step, not only the classification");
			response.CorrelationId.Should().NotBeNullOrWhiteSpace(
				because: "the ID is the only bridge from this envelope to the debug line carrying the server excerpt");
			response.Value.Should().BeNullOrEmpty(
				because: "nothing was written, so no value may be advertised alongside the failure");
			AssertNothingFromThePageLeaked(callResult);
		});
	}

	[Test]
	[AllureTag(SysSettingCreateTool.CreateSysSettingToolName)]
	[AllureName("create-sys-setting reports a non-JSON write answer as a Network failure")]
	[AllureDescription("Registers an environment against a stub that answers InsertSysSettingRequest with a gateway page, then verifies create-sys-setting reports a Network failure naming the non-JSON answer and leaks no part of that page.")]
	[Description("A gateway page answering InsertSysSettingRequest is reported as Network with the non-JSON cause rather than as the bare parser fault it used to be, and no part of the page appears anywhere in the serialized tool result.")]
	public async Task CreateSysSetting_ShouldReportANonJsonAnswer_WhenTheInsertEndpointServesAGatewayPage() {
		await RunAgainstNonJsonWriteStubAsync(async (session, environmentName, cancellationToken) => {
			// Act
			CallToolResult callResult = await session.CallToolAsync(
				SysSettingCreateTool.CreateSysSettingToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["code"] = "Usr1378E2EProbe",
						["name"] = "Usr1378E2EProbe",
						["value-type-name"] = "Text"
					}
				},
				cancellationToken);
			SysSettingCreateResult response = EntitySchemaStructuredResultParser.Extract<SysSettingCreateResult>(callResult);

			// Assert
			response.Success.Should().BeFalse(
				because: "the insert was never acknowledged, so the setting must not be reported as created");
			response.ErrorCategory.Should().Be(SysSettingErrorCategories.Network,
				because: "the create half has to classify the same answer the same way the update half does");
			response.Cause.Should().Contain("not JSON",
				because: "the operator has to be told what arrived instead of being handed a byte offset");
			response.Error.Should().NotContain("invalid start of a value",
				because: "the raw parser text is what issue #1378 replaces - it named neither the operation nor the environment");
			response.CorrelationId.Should().NotBeNullOrWhiteSpace(
				because: "the ID is the only bridge to the debug line carrying the server excerpt");
			AssertNothingFromThePageLeaked(callResult);
		});
	}

	/// <summary>
	/// Asserts that no part of the stub's gateway page appears ANYWHERE in the tool result - every content
	/// block, the structured content and any forwarded log line - rather than only in the two fields that
	/// are fixed local constants by construction and therefore cannot fail.
	/// </summary>
	/// <param name="callResult">The tool result exactly as the MCP client received it.</param>
	private static void AssertNothingFromThePageLeaked(CallToolResult callResult) {
		string serialized = JsonSerializer.Serialize(callResult);
		serialized.Should().NotContain(RuntimeDetectionStubServer.SysSettingsWriteNonJsonBodyMarker,
			because: "issue #1333: no part of the server-authored page may reach an agent's context by default, "
			+ "and the marker is the proxy for every other byte of it");
		serialized.Should().NotContain(PlantedCredential,
			because: "the page hides a credential in a URI, which is precisely what the redactor exists to catch");
		serialized.Should().NotContain("proxy.internal.example",
			because: "the internal hostname must not be promoted into any field of the envelope");
		serialized.Should().NotContain(PlantedBidiControl,
			because: "a right-to-left override reorders whatever a transcript renders after it, so it must not survive into the result");
	}

	private static Task RunAgainstNonJsonWriteStubAsync(
		Func<McpServerSession, string, CancellationToken, Task> act)
		=> CredentialRejectionStubHarness.RunAsync("syssettings-nonjson-write", act,
			nonJsonWriteEndpoints: true);
}
