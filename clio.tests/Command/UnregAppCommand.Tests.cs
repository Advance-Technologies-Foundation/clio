using Clio.Command;
using Clio.UserEnvironment;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class UnregAppCommandTestCase
{
	[Test, Category("Unit")]
	public void Execute_DeletesAppFromRepository() {
		var settingsRepository = Substitute.For<ISettingsRepository>();
		var command = new UnregAppCommand(settingsRepository);
		var options = new UnregAppOptions { Name = "Test" };
		command.Execute(options);
		settingsRepository.Received(1).RemoveEnvironment("Test");
	}
}