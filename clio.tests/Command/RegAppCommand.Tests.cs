using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class RegAppCommandTestCase {

	#region Fields: Private

	private readonly ILogger _loggerMock = Substitute.For<ILogger>();

	#endregion

	[Test]
	[Category("Unit")]
	public void Execute_CallsSettingsRepositoryToConfigure(){
		IApplicationClientFactory clientFactory = Substitute.For<IApplicationClientFactory>();
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();

		RegAppCommand command = new(settingsRepository, clientFactory, null, _loggerMock);
		string name = "Test";
		string login = "TestLogin";
		string password = "TestPassword";
		string uri = "http://testuri.org";
		RegAppOptions options = new() {
			EnvironmentName = name,
			Login = login,
			Password = password,
			Uri = uri,
			IsNetCore = true
		};
		command.Execute(options);
		settingsRepository.Received(1).ConfigureEnvironment(name, Arg.Is<EnvironmentSettings>(
			e => e.Login == login
				&& e.Password == password
				&& e.Uri == uri
				&& e.IsNetCore));
	}

	[Test]
	[Category("Unit")]
	public void Execute_CallsSettingsRepositoryToSetActiveEnvironment_WhenEnvironmentExists(){
		// Arrange
		const string name = "Test";
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.IsEnvironmentExists(name).Returns(true);
		RegAppOptions options = new() {
			ActiveEnvironment = name,
			EnvironmentName = name
		};
		IApplicationClientFactory clientFactory = Substitute.For<IApplicationClientFactory>();
		RegAppCommand command = new(settingsRepository, clientFactory, null, _loggerMock);

		// Act
		int result = command.Execute(options);

		// Assert
		settingsRepository.Received(1).SetActiveEnvironment(name);
		result.Should().Be(0);
	}

	[Test]
	[Category("Unit")]
	public void Execute_DoesNotCallsSettingsRepositoryToSetActiveEnvironment_WhenNotEnvironmentExists(){
		string name = "Test";
		ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
		settingsRepository.IsEnvironmentExists(name).Returns(false);
		RegAppOptions options = new() {
			ActiveEnvironment = name
		};
		IApplicationClientFactory clientFactory = Substitute.For<IApplicationClientFactory>();
		RegAppCommand command = new(settingsRepository, clientFactory, null, _loggerMock);
		command.Execute(options);
		settingsRepository.Received(0).SetActiveEnvironment(name);
	}

}
