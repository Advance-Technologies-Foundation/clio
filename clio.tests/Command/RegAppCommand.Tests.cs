using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command
{

	[TestFixture]
	public class RegAppCommandTestCase
	{
		[Test, Category("Unit")]
		public void Execute_CallsSettingsRepositoryToConfigure() {
			var clientFactory = Substitute.For<IApplicationClientFactory>();
			var settingsRepository = Substitute.For<ISettingsRepository>();
			var command = new RegAppCommand(settingsRepository, clientFactory);
			var name = "Test";
			var login = "TestLogin";
			var password = "TestPassword";
			var uri = "http://testuri.org";
			var options = new RegAppOptions {
				Name = name,
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

		[Test, Category("Unit")]
		public void Execute_CallsSettingsRepositoryToSetActiveEnvironment_WhenEnvironmentExists() {
			var name = "Test";
			var settingsRepository = Substitute.For<ISettingsRepository>();
			settingsRepository.IsEnvironmentExists(name).Returns(true);
			var options = new RegAppOptions {
				ActiveEnvironment = name,
				Name = name
			};
			var clientFactory = Substitute.For<IApplicationClientFactory>();
			var command = new RegAppCommand(settingsRepository, clientFactory);
			command.Execute(options);
			settingsRepository.Received(1).SetActiveEnvironment(name);
		}

		[Test, Category("Unit")]
		public void Execute_DoesNotCallsSettingsRepositoryToSetActiveEnvironment_WhenNotEnvironmentExists() {
			var name = "Test";
			var settingsRepository = Substitute.For<ISettingsRepository>();
			settingsRepository.IsEnvironmentExists(name).Returns(false);
			var options = new RegAppOptions {
				ActiveEnvironment = name
			};
			var clientFactory = Substitute.For<IApplicationClientFactory>();
			var command = new RegAppCommand(settingsRepository, clientFactory);
			command.Execute(options);
			settingsRepository.Received(0).SetActiveEnvironment(name);
		}
	}
}
