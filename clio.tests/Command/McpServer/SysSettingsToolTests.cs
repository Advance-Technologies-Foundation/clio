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
	[Description("get-sys-setting returns the value reported by the data provider when the requested code resolves on the environment.")]
	public void GetSysSetting_Should_Return_Value_From_Environment() {
		IDataProvider dataProvider = Substitute.For<IDataProvider>();
		dataProvider.GetSysSettingValue<string>("MaxFileSize").Returns("10485760");
		SysSettingsManager manager = new(dataProvider);
		SysSettingGetTool tool = new(BuildResolver(manager));

		SysSettingGetResult result = tool.GetSysSetting(new GetSysSettingArgs("local", "MaxFileSize"));

		result.Success.Should().BeTrue(
			because: "the data provider resolved the requested code, so the tool reports a structured success envelope");
		result.Code.Should().Be("MaxFileSize",
			because: "the response must echo the code that was requested for traceability");
		result.Value.Should().Be("10485760",
			because: "the tool surfaces the provider's value verbatim without re-formatting Text settings");
		result.Error.Should().BeNull(
			because: "a successful read must not populate the error envelope");
	}

	[Test]
	[Category("Unit")]
	[Description("get-sys-setting returns an empty value (without surfacing an error) when the setting is unknown or unconfigured.")]
	public void GetSysSetting_Should_Return_Empty_When_Setting_Is_Not_Configured() {
		IDataProvider dataProvider = Substitute.For<IDataProvider>();
		dataProvider.GetSysSettingValue<string>("UnknownCode").Returns((string)null);
		SysSettingsManager manager = new(dataProvider);
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

	#endregion
}
