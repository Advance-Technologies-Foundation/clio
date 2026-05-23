using Clio.Command;
using Clio.Common;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
internal class PingAppCommandTests : BaseCommandTests<PingAppOptions>{
	
	private PingAppCommand _command;
	private IApplicationClient _creatioClient;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<PingAppCommand>();
	}

	public override void TearDown() {
		_creatioClient.ClearReceivedCalls();
		base.TearDown();
		
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_creatioClient = Substitute.For<IApplicationClient>();
		containerBuilder.AddTransient<IApplicationClient>(_=> _creatioClient);
	}

	[TestCase(true)]
	[TestCase(false)]
	public void PingAppCommandShouldBeUsesAllRetryOptions(bool isNetCore) {
		//Arrange
		EnvironmentSettings.IsNetCore = isNetCore;
		PingAppOptions options = new () {
			TimeOut = 1, 
			RetryCount = 2, 
			RetryDelay = 3
		};
			
		// Act
		_command.Execute(options);

		// Assert
		if(isNetCore) {
			_creatioClient.Received(1)
				.ExecuteGetRequest(Arg.Any<string>(), 1, 2, 3);
		}else {
			_creatioClient.Received(1)
				.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), 1, 2, 3);
		}
	}

	[TestCase(true)]
	[TestCase(false)]
	public void PingAppCommandShouldBeUsesAllRetryOptionsOnNet6Environment(bool isNetCore) {
		//Arrange
		PingAppOptions options = new () {
			TimeOut = 1,
			RetryCount = 2,
			RetryDelay = 3,
			IsNetCore = isNetCore
		};
		_command.EnvironmentSettings.IsNetCore = true;
			
		// Act
		_command.Execute(options);

		//Assert
		if(!isNetCore) {
			_creatioClient.Received(1).ExecuteGetRequest(Arg.Any<string>(), 1, 2, 3);
		}
	}
}
