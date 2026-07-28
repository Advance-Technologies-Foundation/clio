using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Author("Kirill Krylov", "k.krylov@creatio.com")]
[Category("Unit")]
[TestFixture]
[Property("Module", "Command")]
internal class SysSettingsCommandTests : BaseCommandTests<SysSettingsOptions>
{

	private ISysSettingsManager _sysSettingsManager;
	private ILogger _logger;
	private SysSettingsCommand _command;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_sysSettingsManager = Substitute.For<ISysSettingsManager>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_sysSettingsManager);
		containerBuilder.AddSingleton(_logger);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<SysSettingsCommand>();
	}

	[TearDown]
	public override void TearDown() {
		base.TearDown();
		_sysSettingsManager.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("Execute reads and prints the value on the get path")]
	public void Execute_ShouldPrintValue_WhenIsGet() {
		// Arrange
		SysSettingsOptions options = new() {
			IsGet = true,
			Code = "whatever"
		};
		const string mockValue = "this is sys setting value";
		_sysSettingsManager.GetSysSettingValueByCode(options.Code).Returns(mockValue);

		// Act
		int actual = _command.Execute(options);

		// Assert
		string expectedLogMessage = $"SysSettings {options.Code} : {mockValue}";
		_logger.Received(1).WriteInfo(expectedLogMessage);
		actual.Should().Be(0, because: "reading a setting is a successful, non-mutating operation");
	}

	[Test]
	[Description("Execute must not write when no value is provided, so a value-less set-syssetting cannot clear the setting")]
	public void Execute_ShouldNotWriteSetting_WhenValueIsNull() {
		// Arrange
		SysSettingsOptions options = new() {
			Code = "Maintainer",
			Value = null
		};

		// Act
		int actual = _command.Execute(options);

		// Assert
		actual.Should().Be(1, because: "a missing value is a usage error, not a request to clear the setting");
		_sysSettingsManager.DidNotReceiveWithAnyArgs().UpdateSysSetting(default, default);
		_sysSettingsManager.DidNotReceiveWithAnyArgs().CreateSysSettingIfNotExists(default, default, default);
		_logger.ReceivedWithAnyArgs(1).WriteError(default);
	}

	[Test]
	[Description("Execute must still write when a value is provided so set-syssetting keeps working")]
	public void Execute_ShouldUpdateSetting_WhenValueIsProvided() {
		// Arrange
		SysSettingsOptions options = new() {
			Code = "Maintainer",
			Value = "ATF",
			Type = "Text"
		};
		_sysSettingsManager.UpdateSysSetting(options.Code, options.Value).Returns(true);

		// Act
		int actual = _command.Execute(options);

		// Assert
		actual.Should().Be(0, because: "a value-bearing set-syssetting is a valid write");
		_sysSettingsManager.Received(1).CreateSysSettingIfNotExists(options.Code, options.Code, options.Type);
		_sysSettingsManager.Received(1).UpdateSysSetting(options.Code, options.Value);
	}

	[Test]
	[Description("Execute warns that the value is ignored when a value is supplied on the get path, and still reads without writing")]
	public void Execute_ShouldWarnAndReadOnly_WhenValueSuppliedWithIsGet() {
		// Arrange
		SysSettingsOptions options = new() {
			IsGet = true,
			Code = "Maintainer",
			Value = "ATF"
		};
		_sysSettingsManager.GetSysSettingValueByCode(options.Code).Returns("current");

		// Act
		int actual = _command.Execute(options);

		// Assert
		actual.Should().Be(0, because: "the get path is a successful read even when an extraneous value is supplied");
		_logger.ReceivedWithAnyArgs(1).WriteWarning(default);
		_sysSettingsManager.DidNotReceiveWithAnyArgs().UpdateSysSetting(default, default);
	}

}
