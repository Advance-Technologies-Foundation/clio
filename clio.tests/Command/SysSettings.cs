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
	
	[Test()]
	public void GetSysSettingByCode_Prints_CorrectValue(){
		//Arrange
		SysSettingsOptions options = new() {
			IsGet = true,
			Code = "whatever"
		};

		ISysSettingsManager sysSettingsManager = Substitute.For<ISysSettingsManager>();
		ILogger logger = Substitute.For<ILogger>();

		const string mockValue = "this is sys setting value";
		sysSettingsManager.GetSysSettingValueByCode(options.Code).Returns(mockValue);

		SysSettingsCommand sut = new(sysSettingsManager, logger);

		//Act
		int actual = sut.Execute(options);

		//Assert
		string expectedLogMessage = $"SysSettings {options.Code} : {mockValue}";
		logger.Received(1).WriteInfo(expectedLogMessage);
		actual.Should().Be(0);
	}

}