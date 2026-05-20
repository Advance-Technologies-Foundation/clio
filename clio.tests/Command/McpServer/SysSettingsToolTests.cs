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
	public void GetSysSetting_Should_Advertise_Stable_Tool_Name() {
		SysSettingGetTool.GetSysSettingToolName.Should().Be("get-sys-setting");
	}

	[Test]
	[Category("Unit")]
	public void GetSysSetting_Should_Return_Value_From_Environment() {
		IDataProvider dataProvider = Substitute.For<IDataProvider>();
		dataProvider.GetSysSettingValue<string>("MaxFileSize").Returns("10485760");
		SysSettingsManager manager = new(dataProvider);
		SysSettingGetTool tool = new(BuildResolver(manager));

		SysSettingGetResult result = tool.GetSysSetting(new GetSysSettingArgs("local", "MaxFileSize"));

		result.Success.Should().BeTrue();
		result.Code.Should().Be("MaxFileSize");
		result.Value.Should().Be("10485760");
		result.Error.Should().BeNull();
	}

	[Test]
	[Category("Unit")]
	public void GetSysSetting_Should_Return_Empty_When_Setting_Is_Not_Configured() {
		IDataProvider dataProvider = Substitute.For<IDataProvider>();
		dataProvider.GetSysSettingValue<string>("UnknownCode").Returns((string)null);
		SysSettingsManager manager = new(dataProvider);
		SysSettingGetTool tool = new(BuildResolver(manager));

		SysSettingGetResult result = tool.GetSysSetting(new GetSysSettingArgs("local", "UnknownCode"));

		result.Success.Should().BeTrue();
		result.Value.Should().BeEmpty();
	}

	[Test]
	[Category("Unit")]
	public void GetSysSetting_Should_Fail_When_Code_Is_Missing() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		SysSettingGetTool tool = new(BuildResolver(manager));

		SysSettingGetResult result = tool.GetSysSetting(new GetSysSettingArgs("local", ""));

		result.Success.Should().BeFalse();
		result.Error.Should().Contain("code is required");
	}

	[Test]
	[Category("Unit")]
	public void GetSysSetting_Should_Categorize_Network_Errors() {
		SysSettingGetTool tool = new(BuildResolverThatThrows(new HttpRequestException("Connection refused.")));

		SysSettingGetResult result = tool.GetSysSetting(new GetSysSettingArgs("offline", "MaxFileSize"));

		result.Success.Should().BeFalse();
		result.Value.Should().BeEmpty();
		result.Error.Should().Be("Network error reading sys-setting.");
	}

	#endregion

	#region list-sys-settings

	[Test]
	[Category("Unit")]
	public void ListSysSettings_Should_Advertise_Stable_Tool_Name() {
		SysSettingsListTool.ListSysSettingsToolName.Should().Be("list-sys-settings");
	}

	[Test]
	[Category("Unit")]
	public void ListSysSettings_Should_Categorize_Generic_Failures() {
		SysSettingsListTool tool = new(BuildResolverThatThrows(new TimeoutException("read timed out")));

		SysSettingsListResult result = tool.ListSysSettings(new ListSysSettingsArgs("local"));

		result.Success.Should().BeFalse();
		result.Settings.Should().BeEmpty();
		result.Error.Should().Be("Failed listing sys-settings.");
	}

	#endregion

	#region create-sys-setting

	[Test]
	[Category("Unit")]
	public void CreateSysSetting_Should_Advertise_Stable_Tool_Name() {
		SysSettingCreateTool.CreateSysSettingToolName.Should().Be("create-sys-setting");
	}

	[Test]
	[Category("Unit")]
	public void CreateSysSetting_Should_Reject_Missing_Required_Fields() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		SysSettingCreateTool tool = new(BuildResolver(manager));

		SysSettingCreateResult result = tool.CreateSysSetting(
			new CreateSysSettingArgs("local", "", "Display", "Integer"));

		result.Success.Should().BeFalse();
		result.Error.Should().Contain("code is required");
	}

	[Test]
	[Category("Unit")]
	public void CreateSysSetting_Should_Reject_Unsupported_Value_Type() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		SysSettingCreateTool tool = new(BuildResolver(manager));

		SysSettingCreateResult result = tool.CreateSysSetting(
			new CreateSysSettingArgs("local", "MyCode", "MyName", "UnsupportedType"));

		result.Success.Should().BeFalse();
		result.Error.Should().Contain("Unsupported value-type-name");
	}

	[Test]
	[Category("Unit")]
	public void CreateSysSetting_Should_Categorize_Authentication_Errors() {
		SysSettingCreateTool tool = new(BuildResolverThatThrows(new UnauthorizedAccessException("Forbidden")));

		SysSettingCreateResult result = tool.CreateSysSetting(
			new CreateSysSettingArgs("local", "MyCode", "MyName", "Text"));

		result.Success.Should().BeFalse();
		result.Error.Should().Be("Authentication error creating sys-setting.");
	}

	#endregion

	#region update-sys-setting

	[Test]
	[Category("Unit")]
	public void UpdateSysSetting_Should_Advertise_Stable_Tool_Name() {
		SysSettingUpdateTool.UpdateSysSettingToolName.Should().Be("update-sys-setting");
	}

	[Test]
	[Category("Unit")]
	public void UpdateSysSetting_Should_Reject_Missing_Code() {
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		SysSettingUpdateTool tool = new(BuildResolver(manager));

		SysSettingUpdateResult result = tool.UpdateSysSetting(
			new UpdateSysSettingArgs("local", "", "x"));

		result.Success.Should().BeFalse();
		result.Error.Should().Contain("code is required");
	}

	[Test]
	[Category("Unit")]
	public void UpdateSysSetting_Should_Categorize_Network_Errors() {
		SysSettingUpdateTool tool = new(BuildResolverThatThrows(new HttpRequestException("Connection refused.")));

		SysSettingUpdateResult result = tool.UpdateSysSetting(
			new UpdateSysSettingArgs("offline", "MaxFileSize", "10485760"));

		result.Success.Should().BeFalse();
		result.Error.Should().Be("Network error updating sys-setting.");
	}

	#endregion
}
