using Clio.Command;
using Clio.UserEnvironment;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class UnregAppCommandTestCase
{
    [Test]
    [Category("Unit")]
    public void Execute_DeletesAppFromRepository()
    {
        ISettingsRepository? settingsRepository = Substitute.For<ISettingsRepository>();
        UnregAppCommand command = new (settingsRepository);
        UnregAppOptions options = new () { Name = "Test" };
        command.Execute(options);
        settingsRepository.Received(1).RemoveEnvironment("Test");
    }
}
