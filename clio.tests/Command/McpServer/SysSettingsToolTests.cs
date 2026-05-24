using System;
using System.Net.Http;
using ATF.Repository.Providers;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class SysSettingsToolTests {

	private static IToolCommandResolver BuildResolver(ISysSettingsManager manager) {
		SysSettingsCommand command = new(manager, Substitute.For<ILogger>());
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SysSettingsCommand>(Arg.Any<EnvironmentOptions>()).Returns(command);
		return commandResolver;
	}

	private static IToolCommandResolver BuildResolverThatThrows(Exception ex) {
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SysSettingsCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw ex);
		return commandResolver;
	}

	#region get-sys-setting

	[Test]
	[Category("Unit")]
	[Description("The get-sys-setting tool advertises a stable, dash-cased name so MCP clients can target it without version drift.")]
	public void GetSysSetting_Should_Advertise_Stable_Tool_Name() {
		SysSettingGetTool.GetSysSettingToolName.Should().Be("get-sys-setting",
			because: "downstream MCP clients address the tool by name and must not break when the assembly is recompiled");
	}

	[Test]
	[Category("Unit")]
	[Description("get-sys-setting returns the All-Users default value resolved through the typed model path when the requested code exists on the environment.")]
	public void GetSysSetting_Should_Return_AllUsers_Default_Value_From_Environment() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.GetAllUsersDefaultWithType("MaxFileSize").Returns(("10485760", "Integer"));
		SysSettingGetTool tool = new(BuildResolver(manager));

		SysSettingGetResult result = tool.GetSysSetting(new GetSysSettingArgs("local", "MaxFileSize"));

		result.Success.Should().BeTrue(
			because: "the manager resolved the requested code via the All-Users-only path, so the tool reports a structured success envelope");
		result.Code.Should().Be("MaxFileSize",
			because: "the response must echo the code that was requested for traceability");
		result.Value.Should().Be("10485760",
			because: "the tool surfaces the manager's All-Users default value verbatim for non-sensitive types");
		result.Error.Should().BeNull(
			because: "a successful read must not populate the error envelope");
		manager.Received(1).GetAllUsersDefaultWithType("MaxFileSize");
		manager.DidNotReceive().GetSysSettingValueByCode(Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("get-sys-setting masks SecureText values so the direct read path does not bypass the masking applied by list-sys-settings.")]
	public void GetSysSetting_Should_Mask_SecureText_Value() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.GetAllUsersDefaultWithType("UsrApiSecret")
			.Returns(("ENCRYPTED_CIPHERTEXT_BASE64", "SecureText"));
		SysSettingGetTool tool = new(BuildResolver(manager));

		SysSettingGetResult result = tool.GetSysSetting(new GetSysSettingArgs("local", "UsrApiSecret"));

		result.Success.Should().BeTrue(
			because: "the read itself succeeds — only the value surface needs to be sanitized");
		result.Value.Should().Be("***",
			because: "SecureText values must be masked on every MCP read path or the masking policy on list-sys-settings is trivially bypassable");
		result.Error.Should().BeNull(
			because: "masking is a transparent presentation rule, not an error condition");
	}

	[Test]
	[Category("Unit")]
	[Description("get-sys-setting returns an empty value for SecureText settings that have no All-Users row, distinguishing 'has secret' from 'no secret' without leaking either way.")]
	public void GetSysSetting_Should_Return_Empty_For_Unconfigured_SecureText() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.GetAllUsersDefaultWithType("UsrEmptySecret").Returns((string.Empty, "SecureText"));
		SysSettingGetTool tool = new(BuildResolver(manager));

		SysSettingGetResult result = tool.GetSysSetting(new GetSysSettingArgs("local", "UsrEmptySecret"));

		result.Value.Should().BeEmpty(
			because: "an unconfigured SecureText must surface as empty, not as the mask placeholder, so callers can tell whether a secret is present");
	}

	[Test]
	[Category("Unit")]
	[Description("get-sys-setting returns an empty value (without surfacing an error) when the All-Users-only manager path reports no configured value.")]
	public void GetSysSetting_Should_Return_Empty_When_Setting_Is_Not_Configured() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.GetAllUsersDefaultWithType("UnknownCode").Returns((string.Empty, (string)null));
		SysSettingGetTool tool = new(BuildResolver(manager));

		SysSettingGetResult result = tool.GetSysSetting(new GetSysSettingArgs("local", "UnknownCode"));

		result.Success.Should().BeTrue(
			because: "an unknown setting is not a tool-level failure — the platform reports it as 'no value configured'");
		result.Value.Should().BeEmpty(
			because: "the tool must surface the absence of a value as an empty string, not synthesize a placeholder");
	}

	[Test]
	[Category("Unit")]
	[Description("get-sys-setting short-circuits with a validation failure when the caller omits the required code argument.")]
	public void GetSysSetting_Should_Fail_When_Code_Is_Missing() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		SysSettingGetTool tool = new(BuildResolver(manager));

		SysSettingGetResult result = tool.GetSysSetting(new GetSysSettingArgs("local", ""));

		result.Success.Should().BeFalse(
			because: "a request without a code must not be forwarded to the platform");
		result.Error.Should().Contain("code is required",
			because: "the structured error must point the caller at the missing argument");
	}

	[Test]
	[Category("Unit")]
	[Description("get-sys-setting maps HttpRequestException raised during environment resolution to a 'Network error' diagnostic message.")]
	public void GetSysSetting_Should_Categorize_Network_Errors() {
		SysSettingGetTool tool = new(BuildResolverThatThrows(new HttpRequestException("Connection refused.")));

		SysSettingGetResult result = tool.GetSysSetting(new GetSysSettingArgs("offline", "MaxFileSize"));

		result.Success.Should().BeFalse(
			because: "an unreachable environment must surface a failure envelope, not propagate the raw exception");
		result.Value.Should().BeEmpty(
			because: "no value could be resolved, so the envelope must not synthesize one");
		result.Error.Should().Be("Network error reading sys-setting.",
			because: "the CategorizeError fallback maps HttpRequestException to the canonical 'Network error' message");
	}

	#endregion

	#region list-sys-settings

	[Test]
	[Category("Unit")]
	[Description("The list-sys-settings tool advertises a stable, dash-cased name so MCP clients can target it without version drift.")]
	public void ListSysSettings_Should_Advertise_Stable_Tool_Name() {
		SysSettingsListTool.ListSysSettingsToolName.Should().Be("list-sys-settings",
			because: "downstream MCP clients address the tool by name and must not break when the assembly is recompiled");
	}

	[Test]
	[Category("Unit")]
	[Description("list-sys-settings maps unexpected exceptions raised during resolution to a generic 'Failed listing' diagnostic.")]
	public void ListSysSettings_Should_Categorize_Generic_Failures() {
		SysSettingsListTool tool = new(BuildResolverThatThrows(new TimeoutException("read timed out")));

		SysSettingsListResult result = tool.ListSysSettings(new ListSysSettingsArgs("local"));

		result.Success.Should().BeFalse(
			because: "an unexpected exception during resolve must surface as a structured failure rather than an unhandled exception");
		result.Settings.Should().BeEmpty(
			because: "no rows were produced, so the envelope must not synthesize a partial list");
		result.Error.Should().Be("Failed listing sys-settings.",
			because: "the CategorizeError fallback maps non-network/auth exceptions to the canonical 'Failed listing' message");
	}

	#endregion

	#region create-sys-setting

	[Test]
	[Category("Unit")]
	[Description("The create-sys-setting tool advertises a stable, dash-cased name so MCP clients can target it without version drift.")]
	public void CreateSysSetting_Should_Advertise_Stable_Tool_Name() {
		SysSettingCreateTool.CreateSysSettingToolName.Should().Be("create-sys-setting",
			because: "downstream MCP clients address the tool by name and must not break when the assembly is recompiled");
	}

	[Test]
	[Category("Unit")]
	[Description("create-sys-setting short-circuits with a validation failure when a required field (code) is empty.")]
	public void CreateSysSetting_Should_Reject_Missing_Required_Fields() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		SysSettingCreateTool tool = new(BuildResolver(manager));

		SysSettingCreateResult result = tool.CreateSysSetting(
			new CreateSysSettingArgs("local", "", "Display", "Integer"));

		result.Success.Should().BeFalse(
			because: "an empty code is invalid input and must short-circuit before the platform is contacted");
		result.Error.Should().Contain("code is required",
			because: "the structured error must point the caller at the missing argument");
	}

	[Test]
	[Category("Unit")]
	[Description("create-sys-setting refuses unknown value-type-names to keep the tool surface aligned with Creatio's internal type registry.")]
	public void CreateSysSetting_Should_Reject_Unsupported_Value_Type() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		SysSettingCreateTool tool = new(BuildResolver(manager));

		SysSettingCreateResult result = tool.CreateSysSetting(
			new CreateSysSettingArgs("local", "MyCode", "MyName", "UnsupportedType"));

		result.Success.Should().BeFalse(
			because: "the tool must reject value-type-names that are not in the Creatio internal registry");
		result.Error.Should().Contain("Unsupported value-type-name",
			because: "the structured error must explain why the value-type-name was refused");
	}

	[Test]
	[Category("Unit")]
	[Description("create-sys-setting refuses Binary value-type-name because PostSysSettingsValues is scalar-only and there is no BinaryValue read column to round-trip through.")]
	public void CreateSysSetting_Should_Reject_Binary_Value_Type() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		SysSettingCreateTool tool = new(BuildResolver(manager));

		SysSettingCreateResult result = tool.CreateSysSetting(
			new CreateSysSettingArgs("local", "UsrBinCode", "Binary setting", "Binary"));

		result.Success.Should().BeFalse(
			because: "Binary is intentionally excluded from the MCP-advertised surface — it needs a dedicated upload/download flow that is out of scope for this tool set");
		result.Error.Should().Contain("Unsupported value-type-name",
			because: "Binary is no longer in the SupportedValueTypeNames whitelist so the standard refusal message applies");
	}

	[Test]
	[Category("Unit")]
	[Description("create-sys-setting maps UnauthorizedAccessException raised during environment resolution to an 'Authentication error' diagnostic.")]
	public void CreateSysSetting_Should_Categorize_Authentication_Errors() {
		SysSettingCreateTool tool = new(BuildResolverThatThrows(new UnauthorizedAccessException("Forbidden")));

		SysSettingCreateResult result = tool.CreateSysSetting(
			new CreateSysSettingArgs("local", "MyCode", "MyName", "Text"));

		result.Success.Should().BeFalse(
			because: "the resolver failed authentication and the tool must surface a structured failure envelope");
		result.Error.Should().Be("Authentication error creating sys-setting.",
			because: "the CategorizeError fallback maps UnauthorizedAccessException to the canonical 'Authentication error' message");
	}

	[Test]
	[Category("Unit")]
	[Description("create-sys-setting masks the readback value for SecureText so the structured response does not echo the freshly-written secret back to the caller in clear.")]
	public void CreateSysSetting_Should_Mask_SecureText_Readback() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.InsertSysSetting(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<Guid?>())
			.Returns(new SysSettingsManager.InsertSysSettingResponse(
				new SysSettingsManager.ResponseStatus(string.Empty, string.Empty, Array.Empty<object>()),
				Guid.NewGuid(), 1, false, true));
		manager.UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>()).Returns(true);
		manager.GetAllUsersDefaultByCode("UsrApiSecret").Returns("ENCRYPTED_CIPHERTEXT_BASE64");
		SysSettingCreateTool tool = new(BuildResolver(manager));

		SysSettingCreateResult result = tool.CreateSysSetting(
			new CreateSysSettingArgs("local", "UsrApiSecret", "API secret", "SecureText", Value: "plaintext"));

		result.Success.Should().BeTrue(
			because: "the create itself succeeds — only the readback value surface needs to be sanitized");
		result.Value.Should().Be("***",
			because: "create-sys-setting must not echo the freshly-stored SecureText back in clear or it bypasses the masking policy");
	}

	#endregion

	#region update-sys-setting

	[Test]
	[Category("Unit")]
	[Description("The update-sys-setting tool advertises a stable, dash-cased name so MCP clients can target it without version drift.")]
	public void UpdateSysSetting_Should_Advertise_Stable_Tool_Name() {
		SysSettingUpdateTool.UpdateSysSettingToolName.Should().Be("update-sys-setting",
			because: "downstream MCP clients address the tool by name and must not break when the assembly is recompiled");
	}

	[Test]
	[Category("Unit")]
	[Description("update-sys-setting short-circuits with a validation failure when the caller omits the required code argument.")]
	public void UpdateSysSetting_Should_Reject_Missing_Code() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		SysSettingUpdateTool tool = new(BuildResolver(manager));

		SysSettingUpdateResult result = tool.UpdateSysSetting(
			new UpdateSysSettingArgs("local", "", "x"));

		result.Success.Should().BeFalse(
			because: "an empty code is invalid input and must short-circuit before the platform is contacted");
		result.Error.Should().Contain("code is required",
			because: "the structured error must point the caller at the missing argument");
	}

	[Test]
	[Category("Unit")]
	[Description("update-sys-setting maps HttpRequestException raised during environment resolution to a 'Network error' diagnostic message.")]
	public void UpdateSysSetting_Should_Categorize_Network_Errors() {
		SysSettingUpdateTool tool = new(BuildResolverThatThrows(new HttpRequestException("Connection refused.")));

		SysSettingUpdateResult result = tool.UpdateSysSetting(
			new UpdateSysSettingArgs("offline", "MaxFileSize", "10485760"));

		result.Success.Should().BeFalse(
			because: "an unreachable environment must surface a failure envelope, not propagate the raw exception");
		result.Error.Should().Be("Network error updating sys-setting.",
			because: "the CategorizeError fallback maps HttpRequestException to the canonical 'Network error' message");
	}

	[Test]
	[Category("Unit")]
	[Description("update-sys-setting masks the readback value for SecureText so the response does not echo the just-written secret back to the caller in clear.")]
	public void UpdateSysSetting_Should_Mask_SecureText_Readback() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.UpdateSysSetting("UsrApiSecret", Arg.Any<object>(), Arg.Any<string>()).Returns(true);
		manager.GetAllUsersDefaultWithType("UsrApiSecret")
			.Returns(("ENCRYPTED_CIPHERTEXT_BASE64", "SecureText"));
		SysSettingUpdateTool tool = new(BuildResolver(manager));

		SysSettingUpdateResult result = tool.UpdateSysSetting(
			new UpdateSysSettingArgs("local", "UsrApiSecret", "plaintext", "SecureText"));

		result.Success.Should().BeTrue(
			because: "the update itself succeeds — only the readback value surface needs to be sanitized");
		result.Value.Should().Be("***",
			because: "update-sys-setting must not echo the freshly-stored SecureText back in clear or it bypasses the masking policy");
	}

	#endregion

	#region upsert-sys-setting

	[Test]
	[Category("Unit")]
	[Description("upsert-sys-setting probes the environment first; when the setting row is missing (GetAllUsersDefaultWithType returns null type-name), it creates instead of updating.")]
	public void UpsertSysSetting_Should_Create_When_Setting_Does_Not_Exist() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		// Phase-0 behaviour: missing setting yields (string.Empty, null).
		manager.GetAllUsersDefaultWithType("UsrBrandNewFlag").Returns((string.Empty, (string)null!));
		manager.InsertSysSetting("UsrBrandNewFlag", "UsrBrandNewFlag", "Boolean", Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<Guid?>())
			.Returns(new SysSettingsManager.InsertSysSettingResponse(
				new SysSettingsManager.ResponseStatus(string.Empty, string.Empty, Array.Empty<object>()),
				Guid.NewGuid(), 1, false, true));
		SysSettingUpsertTool tool = new(BuildResolver(manager));

		object result = tool.Upsert(new UpsertSysSettingRunArgs("local", "UsrBrandNewFlag") {
			ValueTypeName = "Boolean",
			Value = "true"
		});

		result.Should().BeOfType<SysSettingCreateResult>(
			because: "the probe returned typeName=null (setting does not exist), so upsert must create rather than update");
		((SysSettingCreateResult)result).Success.Should().BeTrue();
	}

	[Test]
	[Category("Unit")]
	[Description("upsert-sys-setting routes through update when the probe reports the setting exists on the environment (non-null type-name).")]
	public void UpsertSysSetting_Should_Update_When_Setting_Exists() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.GetAllUsersDefaultWithType("MaxFileSize").Returns(("10485760", "Integer"));
		manager.UpdateSysSetting("MaxFileSize", Arg.Any<object>(), Arg.Any<string>()).Returns(true);
		SysSettingUpsertTool tool = new(BuildResolver(manager));

		object result = tool.Upsert(new UpsertSysSettingRunArgs("local", "MaxFileSize") {
			Value = "20971520",
			ValueTypeName = "Integer"
		});

		result.Should().BeOfType<SysSettingUpdateResult>(
			because: "the probe returned a real typeName (setting exists), so upsert must update rather than create");
	}

	[Test]
	[Category("Unit")]
	[Description("upsert-sys-setting refuses to write when the probe itself failed (network/auth) — otherwise a transient read failure could overwrite an existing setting via create.")]
	public void UpsertSysSetting_Should_Refuse_When_Probe_Fails() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.GetAllUsersDefaultWithType("UsrSomething")
			.Returns<(string, string)>(_ => throw new HttpRequestException("Connection refused"));
		manager.InsertSysSetting(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<Guid?>())
			.Returns(new SysSettingsManager.InsertSysSettingResponse(
				new SysSettingsManager.ResponseStatus(string.Empty, string.Empty, Array.Empty<object>()),
				Guid.NewGuid(), 1, false, true));
		manager.UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>()).Returns(true);
		SysSettingUpsertTool tool = new(BuildResolver(manager));

		object result = tool.Upsert(new UpsertSysSettingRunArgs("local", "UsrSomething") {
			ValueTypeName = "Boolean",
			Value = "true"
		});

		result.Should().BeOfType<SysSettingUpdateResult>(
			because: "upsert-sys-setting must report a failure envelope, not silently create or update under transient read errors");
		((SysSettingUpdateResult)result).Success.Should().BeFalse();
		manager.DidNotReceive().InsertSysSetting(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<Guid?>());
		manager.DidNotReceive().UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>());
	}

	#endregion
}
