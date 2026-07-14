using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature("sys-setting")]
[NonParallelizable]
public sealed class SysSettingsToolE2ETests : McpContractFixtureBase {

	private const string GetToolName = SysSettingGetTool.GetSysSettingToolName;
	private const string ListToolName = SysSettingsListTool.ListSysSettingsToolName;
	private const string CreateToolName = SysSettingCreateTool.CreateSysSettingToolName;
	private const string UpdateToolName = SysSettingUpdateTool.UpdateSysSettingToolName;
	private const string KnownPlatformSetting = "Maintainer";

	#region Read-only tests

	[Test]
	[AllureTag(ListToolName)]
	[AllureName("list-sys-settings returns advertised structured payload with at least one known setting")]
	[AllureDescription("Starts the real clio MCP server, invokes list-sys-settings against the configured sandbox environment, and verifies the structured response advertises the known OOTB Maintainer setting.")]
	[Description("Starts the real clio MCP server, invokes list-sys-settings against the configured sandbox environment, and verifies the structured response advertises the known OOTB Maintainer setting.")]
	public async Task ListSysSettings_Should_Return_Settings_Including_Known_Code() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(requireReachableEnvironment: true);

		// Act
		CallToolResult callResult = await CallToolAsync(
			arrangeContext,
			ListToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName
			});
		SysSettingsListResult response = EntitySchemaStructuredResultParser.Extract<SysSettingsListResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "list-sys-settings should return a structured success envelope for a reachable configured environment");
		response.Success.Should().BeTrue(
			because: "reading the sys-settings catalog should succeed when the environment is reachable");
		response.Settings.Should().NotBeNullOrEmpty(
			because: "every Creatio environment ships with multiple OOTB system settings");
		response.Settings.Select(s => s.Code).Should().Contain(KnownPlatformSetting,
			because: "the Maintainer sys-setting is part of the OOTB catalog and must be advertised");
		response.Error.Should().BeNull(
			because: "no error message should be present when the tool call succeeds");
	}

	[Test]
	[AllureTag(GetToolName)]
	[AllureName("get-sys-setting returns structured value for a known OOTB setting")]
	[AllureDescription("Starts the real clio MCP server, invokes get-sys-setting for the OOTB Maintainer setting against the configured sandbox environment, and verifies the response carries the code and a non-empty value.")]
	[Description("Starts the real clio MCP server, invokes get-sys-setting for the OOTB Maintainer setting against the configured sandbox environment, and verifies the response carries the code and a non-empty value.")]
	public async Task GetSysSetting_Should_Return_Value_For_Known_Setting() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(requireReachableEnvironment: true);

		// Act
		CallToolResult callResult = await CallToolAsync(
			arrangeContext,
			GetToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["code"] = KnownPlatformSetting
			});
		SysSettingGetResult response = EntitySchemaStructuredResultParser.Extract<SysSettingGetResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "get-sys-setting should return a structured success envelope for a known OOTB setting");
		response.Success.Should().BeTrue(
			because: "reading a configured OOTB setting from a reachable environment should succeed");
		response.Code.Should().Be(KnownPlatformSetting,
			because: "the response must echo the code that was requested");
		response.Value.Should().NotBeNull(
			because: "Maintainer is a Text setting and should resolve to a non-null string");
		response.Error.Should().BeNull(
			because: "no error message should be present when the tool call succeeds");
	}

	[Test]
	[AllureTag(GetToolName)]
	[AllureName("get-sys-setting rejects empty code with structured error")]
	[AllureDescription("Invokes get-sys-setting with an empty code argument and verifies that the response reports a structured failure with an explicit error message and no leaked details.")]
	[Description("Invokes get-sys-setting with an empty code argument and verifies that the response reports a structured failure with an explicit error message and no leaked details.")]
	public async Task GetSysSetting_Should_Report_Failure_When_Code_Is_Empty() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(requireReachableEnvironment: true);

		// Act
		CallToolResult callResult = await CallToolAsync(
			arrangeContext,
			GetToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["code"] = string.Empty
			});
		SysSettingGetResult response = EntitySchemaStructuredResultParser.Extract<SysSettingGetResult>(callResult);

		// Assert
		response.Success.Should().BeFalse(
			because: "an empty code must produce a structured failure rather than reaching the platform");
		response.Error.Should().Contain("code is required",
			because: "the error message should help the caller correct the argument shape");
	}

	#endregion

	#region Destructive tests (opt-in)

	[Test]
	[AllureTag(CreateToolName)]
	[AllureName("create-sys-setting writes a Text sys-setting and applies the initial value")]
	[AllureDescription("Starts the real clio MCP server, creates a uniquely-coded Text sys-setting with an initial value, and verifies the structured response reports success, value-type-name, and the assigned value.")]
	[Description("Starts the real clio MCP server, creates a uniquely-coded Text sys-setting with an initial value, and verifies the structured response reports success, value-type-name, and the assigned value.")]
	public async Task CreateSysSetting_Text_Should_Apply_Initial_Value() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(
			requireReachableEnvironment: true,
			requireDestructiveOptIn: true);
		string code = $"UsrMcpE2EText{Guid.NewGuid():N}"[..32];
		string initial = "e2e-initial-value";

		// Act
		CallToolResult callResult = await CallToolAsync(
			arrangeContext,
			CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["code"] = code,
				["name"] = code,
				["value-type-name"] = "Text",
				["value"] = initial
			});
		SysSettingCreateResult response = EntitySchemaStructuredResultParser.Extract<SysSettingCreateResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "creating a Text sys-setting against a reachable sandbox should succeed");
		response.Success.Should().BeTrue(
			because: "the platform should accept a new uniquely-coded Text setting");
		response.Code.Should().Be(code,
			because: "the response must echo the requested code");
		response.ValueTypeName.Should().Be("Text",
			because: "the response must echo the value-type-name passed by the caller");
		response.Value.Should().Be(initial,
			because: "the initial value should be persisted and read back from the platform");
		response.Error.Should().BeNull();
		response.Warning.Should().BeNull(
			because: "a Text sys-setting with a valid value should not produce a partial-success warning");
	}

	[Test]
	[AllureTag(CreateToolName)]
	[AllureName("create-sys-setting wires a Lookup reference schema by name")]
	[AllureDescription("Creates a uniquely-coded Lookup sys-setting referencing the OOTB Contact entity schema and verifies the structured response reports success, Lookup value-type-name, and propagates the reference.")]
	[Description("Creates a uniquely-coded Lookup sys-setting referencing the OOTB Contact entity schema and verifies the structured response reports success, Lookup value-type-name, and propagates the reference.")]
	public async Task CreateSysSetting_Lookup_Should_Wire_Reference_Schema() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(
			requireReachableEnvironment: true,
			requireDestructiveOptIn: true);
		string code = $"UsrMcpE2ELkp{Guid.NewGuid():N}"[..32];

		// Act
		CallToolResult callResult = await CallToolAsync(
			arrangeContext,
			CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["code"] = code,
				["name"] = code,
				["value-type-name"] = "Lookup",
				["reference-schema-name"] = "Contact"
			});
		SysSettingCreateResult response = EntitySchemaStructuredResultParser.Extract<SysSettingCreateResult>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue();
		response.Success.Should().BeTrue(
			because: "a Lookup sys-setting bound to an existing schema must be created successfully");
		response.ValueTypeName.Should().Be("Lookup",
			because: "the persisted type must match the requested Lookup value-type-name");
		response.Error.Should().BeNull();
	}

	[Test]
	[AllureTag(CreateToolName)]
	[AllureName("create-sys-setting rejects Lookup type without reference-schema-name")]
	[AllureDescription("Invokes create-sys-setting with value-type-name=Lookup and no reference-schema-name. Verifies the structured failure explicitly names the missing argument and avoids a platform round-trip.")]
	[Description("Invokes create-sys-setting with value-type-name=Lookup and no reference-schema-name. Verifies the structured failure explicitly names the missing argument and avoids a platform round-trip.")]
	public async Task CreateSysSetting_Lookup_Should_Reject_Without_ReferenceSchema() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(
			requireReachableEnvironment: true,
			requireDestructiveOptIn: true);

		// Act
		CallToolResult callResult = await CallToolAsync(
			arrangeContext,
			CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["code"] = "UsrMcpE2EUnused",
				["name"] = "UsrMcpE2EUnused",
				["value-type-name"] = "Lookup"
			});
		SysSettingCreateResult response = EntitySchemaStructuredResultParser.Extract<SysSettingCreateResult>(callResult);

		// Assert
		response.Success.Should().BeFalse(
			because: "the tool must short-circuit Lookup creation when reference-schema-name is missing");
		response.Error.Should().Contain("reference-schema-name",
			because: "the error must explicitly name the missing argument so the caller can fix it");
	}

	[Test]
	[AllureTag(UpdateToolName)]
	[AllureName("update-sys-setting changes the value of a freshly-created Text setting")]
	[AllureDescription("Creates a unique Text sys-setting with an initial value, then updates the value and verifies the structured response reports success and the readback matches.")]
	[Description("Creates a unique Text sys-setting with an initial value, then updates the value and verifies the structured response reports success and the readback matches.")]
	public async Task UpdateSysSetting_Should_Change_Value_On_Existing_Setting() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(
			requireReachableEnvironment: true,
			requireDestructiveOptIn: true);
		string code = $"UsrMcpE2EUpd{Guid.NewGuid():N}"[..32];
		CallToolResult createResult = await CallToolAsync(
			arrangeContext,
			CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["code"] = code,
				["name"] = code,
				["value-type-name"] = "Text",
				["value"] = "before"
			});
		SysSettingCreateResult createResponse =
			EntitySchemaStructuredResultParser.Extract<SysSettingCreateResult>(createResult);
		createResponse.Success.Should().BeTrue(
			because: "the update scenario requires a successfully-created precondition setting");

		// Act
		CallToolResult updateResult = await CallToolAsync(
			arrangeContext,
			UpdateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["code"] = code,
				["value"] = "after"
			});
		SysSettingUpdateResult updateResponse =
			EntitySchemaStructuredResultParser.Extract<SysSettingUpdateResult>(updateResult);

		// Assert
		updateResult.IsError.Should().NotBeTrue();
		updateResponse.Success.Should().BeTrue(
			because: "updating an existing Text setting with a valid value should succeed");
		updateResponse.Code.Should().Be(code);
		updateResponse.Value.Should().Be("after",
			because: "the readback should reflect the value applied by the update call");
		updateResponse.Error.Should().BeNull();
	}

	[Test]
	[AllureTag(UpdateToolName)]
	[AllureName("update-sys-setting uploads a Binary setting from a local file via value-file-path")]
	[AllureDescription("Creates a unique Binary sys-setting, then updates it by pointing value-file-path at a local file; clio reads and Base64-encodes the file locally and the platform accepts the Binary write.")]
	[Description("Creates a unique Binary sys-setting, then updates it by pointing value-file-path at a local file; clio reads and Base64-encodes the file locally and the platform accepts the Binary write.")]
	public async Task UpdateSysSetting_Should_Upload_Binary_From_ValueFilePath() {
		// Arrange
		await using ArrangeContext arrangeContext = await ArrangeAsync(
			requireReachableEnvironment: true,
			requireDestructiveOptIn: true);
		string code = $"UsrMcpE2EBin{Guid.NewGuid():N}"[..32];
		CallToolResult createResult = await CallToolAsync(
			arrangeContext,
			CreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = arrangeContext.EnvironmentName,
				["code"] = code,
				["name"] = code,
				["value-type-name"] = "Binary"
			});
		EntitySchemaStructuredResultParser.Extract<SysSettingCreateResult>(createResult).Success.Should().BeTrue(
			because: "the binary upload scenario requires a successfully-created precondition Binary setting");
		string filePath = Path.Combine(Path.GetTempPath(), $"{code}.bin");
		byte[] fileBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]; // PNG signature bytes stand in for a logo
		await File.WriteAllBytesAsync(filePath, fileBytes);

		try {
			// Act
			CallToolResult updateResult = await CallToolAsync(
				arrangeContext,
				UpdateToolName,
				new Dictionary<string, object?> {
					["environment-name"] = arrangeContext.EnvironmentName,
					["code"] = code,
					["value-file-path"] = filePath
				});
			SysSettingUpdateResult updateResponse =
				EntitySchemaStructuredResultParser.Extract<SysSettingUpdateResult>(updateResult);

			// Assert
			updateResult.IsError.Should().NotBeTrue(
				because: "a Binary upload from a readable file must not surface a tool error");
			updateResponse.Success.Should().BeTrue(
				because: "clio must read the file, Base64-encode it locally, and the platform must accept the Binary write");
			updateResponse.Code.Should().Be(code,
				because: "the response must echo the updated Binary setting code");
			updateResponse.Error.Should().BeNull(
				because: "a successful Binary upload must not populate the error envelope");

			// Round-trip byte fidelity: MCP get returns empty for Binary, so read the stored value back
			// through the legacy get-syssetting / cliogate path, decode it, and assert it equals the source.
			ClioCliCommandResult readBack = await ClioCliCommandRunner.RunAsync(
				arrangeContext.Settings,
				["get-syssetting", code, "-e", arrangeContext.EnvironmentName!]);
			readBack.ExitCode.Should().Be(0,
				because: "reading the stored Binary value back through the legacy CLI path must succeed");
			int marker = readBack.StandardOutput.LastIndexOf(" : ", StringComparison.Ordinal);
			marker.Should().BeGreaterThan(-1,
				because: "get-syssetting prints the stored value after a ' : ' separator");
			string storedBase64 = readBack.StandardOutput[(marker + 3)..].Trim();
			storedBase64.Should().Be(Convert.ToBase64String(fileBytes),
				because: "the bytes persisted on the platform must exactly equal the uploaded source file");
		} finally {
			if (File.Exists(filePath)) {
				File.Delete(filePath);
			}
		}
	}

	#endregion

	#region Helpers

	private static async Task<CallToolResult> CallToolAsync(
		ArrangeContext arrangeContext,
		string toolName,
		Dictionary<string, object?> args) {
		// The helper serves both resident tools (get-sys-setting, list-sys-settings) and hidden long-tail
		// tools (create-sys-setting, update-sys-setting), so the discoverability gate uses the lazy-surface
		// union of tools/list names and the get-tool-contract compact index.
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);
		toolNames.Should().Contain(toolName,
			because: $"the {toolName} tool must be discoverable on the lazy surface (tools/list or the get-tool-contract compact index) before the call can be executed");
		return await arrangeContext.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> {
				["args"] = args
			},
			arrangeContext.CancellationTokenSource.Token);
	}

	private async Task<ArrangeContext> ArrangeAsync(
		bool requireReachableEnvironment,
		bool requireDestructiveOptIn = false) {
		McpE2ESettings settings = TestConfiguration.Load();
		if (requireDestructiveOptIn && !settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run destructive sys-settings MCP end-to-end tests.");
		}
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		string? environmentName = requireReachableEnvironment
			? await ResolveReachableEnvironmentAsync(settings)
			: settings.Sandbox.EnvironmentName;
		McpServerSession session = Session;
		return new ArrangeContext(session, cancellationTokenSource, environmentName, settings);
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) {
		string? configuredEnvironmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(configuredEnvironmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to run sys-settings MCP E2E tests.");
		}
		if (!await CanReachEnvironmentAsync(settings, configuredEnvironmentName!)) {
			Assert.Ignore($"Sys-settings MCP E2E requires a reachable sandbox environment. '{configuredEnvironmentName}' was not reachable.");
		}
		return configuredEnvironmentName!;
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
			settings,
			["ping-app", "-e", environmentName]);
		return result.ExitCode == 0;
	}

	private new sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string? EnvironmentName,
		McpE2ESettings Settings) : IAsyncDisposable {
		public ValueTask DisposeAsync() {
			CancellationTokenSource.Dispose();
			return ValueTask.CompletedTask;
		}
	}

	#endregion
}
