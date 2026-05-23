using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Author("Kirill Krylov", "k.krylov@creatio.com")]
[Category("Unit")]
[TestFixture]
[Property("Module", "Command")]
internal class SysSettingsCommandTests : BaseCommandTests<SysSettingsOptions>
{

	private readonly ISysSettingsManager _sysSettingsManager = Substitute.For<ISysSettingsManager>();
	private readonly ILogger _logger = Substitute.For<ILogger>();

	[Test]
	public void GetSysSettingByCode_Prints_CorrectValue(){
		SysSettingsOptions options = new() {
			IsGet = true,
			Code = "whatever"
		};

		const string mockValue = "this is sys setting value";
		_sysSettingsManager.GetSysSettingValueByCode(options.Code).Returns(mockValue);

		SysSettingsCommand sut = new(_sysSettingsManager, _logger);

		int actual = sut.Execute(options);

		string expectedLogMessage = $"SysSettings {options.Code} : {mockValue}";
		_logger.Received(1).WriteInfo(expectedLogMessage);
		actual.Should().Be(0);
	}

}
