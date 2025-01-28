using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Author("Kirill Krylov", "k.krylov@creatio.com")]
[Category("UnitTests")]
[TestFixture]
internal class SysSettingsCommandTests : BaseCommandTests<SysSettingsOptions>
{
	
	private readonly IClioGateway _clioGatewayMock = Substitute.For<IClioGateway>();
	private readonly ISysSettingsManager _sysSettingsManager = Substitute.For<ISysSettingsManager>();
	private readonly ILogger _logger = Substitute.For<ILogger>();
	private const string MinClioGateVersion = "2.0.0.0";
	[Test]
	public void GetSysSettingByCode_Prints_CorrectValue_WhenClioGateIsInstalled(){
		//Arrange
		SysSettingsOptions options = new() {
			IsGet = true,
			Code = "whatever"
		};

		_clioGatewayMock.IsCompatibleWith(Arg.Any<string>()).Returns(true);

		const string mockValue = "this is sys setting value";
		_sysSettingsManager.GetSysSettingValueByCode(options.Code).Returns(mockValue);

		SysSettingsCommand sut = new(_sysSettingsManager, _logger, _clioGatewayMock);

		//Act
		int actual = sut.Execute(options);

		//Assert
		string expectedLogMessage = $"SysSettings {options.Code} : {mockValue}";
		_logger.Received(1).WriteInfo(expectedLogMessage);
		actual.Should().Be(0);
	}
	
	[Test]
	public void GetSysSettingByCode_Prints_ErrorWhenClioGateNotInstalled(){
		//Arrange
		SysSettingsOptions options = new() {
			IsGet = true,
			Code = "whatever"
		};
		
		_clioGatewayMock.IsCompatibleWith(Arg.Any<string>()).Returns(false);

		const string mockValue = "this is sys setting value";
		_sysSettingsManager.GetSysSettingValueByCode(options.Code).Returns(mockValue);

		SysSettingsCommand sut = new(_sysSettingsManager, _logger, _clioGatewayMock);

		//Act
		int actual = sut.Execute(options);

		//Assert
		const string expectedInfoLogMessage = $"To install cliogate use the following command: clio install-gate";
		_logger.Received(1).WriteInfo(expectedInfoLogMessage);
		
		const string expectedErrorLogMessage = $"To view SysSetting value by code requires cliogate package version {MinClioGateVersion} or higher installed in Creatio.";
		_logger.Received(1).WriteError(expectedErrorLogMessage);
		
		actual.Should().Be(0);
	}

}