using Autofac;
using Clio.Command;
using Clio.Common;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture(Category = "Unit")]
internal class TelemetryCommandTests : BaseClioModuleTests
{

	private readonly IApplicationClient _creatioClient = Substitute.For<IApplicationClient>();

	public override void Setup(){}

	protected override void AdditionalRegistrations(ContainerBuilder containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.RegisterInstance(_creatioClient).As<IApplicationClient>();
	}

	[Test]
	public void TelemetryCommandShouldCallCorrectEndpoint() {
		//Arrange
		FileSystem = CreateFs();
		BindingsModule bindingModule = new(FileSystem);
		Container = bindingModule.Register(EnvironmentSettings, AdditionalRegistrations);
		
		TelemetryCommand command = Container.Resolve<TelemetryCommand>();
		TelemetryOptions options = new TelemetryOptions();
		
		// Act
		command.Execute(options);

		// Assert
		_creatioClient.Received(1)
			.ExecuteGetRequest(Arg.Is<string>(url => url.EndsWith("/rest/Telemetry")), 
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		_creatioClient.ClearReceivedCalls();
	}

	[Test]
	public void TelemetryCommandShouldUseHttpGetMethod() {
		//Arrange
		FileSystem = CreateFs();
		BindingsModule bindingModule = new(FileSystem);
		Container = bindingModule.Register(EnvironmentSettings, AdditionalRegistrations);
		
		TelemetryCommand command = Container.Resolve<TelemetryCommand>();
		
		// Assert
		Assert.AreEqual(System.Net.Http.HttpMethod.Get, command.HttpMethod);
	}

	[Test]
	public void TelemetryCommandShouldUseRetryOptions() {
		//Arrange
		FileSystem = CreateFs();
		BindingsModule bindingModule = new(FileSystem);
		Container = bindingModule.Register(EnvironmentSettings, AdditionalRegistrations);
		
		TelemetryCommand command = Container.Resolve<TelemetryCommand>();
		TelemetryOptions options = new TelemetryOptions() { 
			TimeOut = 5000, 
			RetryCount = 3, 
			RetryDelay = 1 
		};
		
		// Act
		command.Execute(options);

		// Assert
		_creatioClient.Received(1)
			.ExecuteGetRequest(Arg.Any<string>(), 5000, 3, 1);
		_creatioClient.ClearReceivedCalls();
	}
}