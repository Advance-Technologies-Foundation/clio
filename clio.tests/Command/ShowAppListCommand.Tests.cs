using System;

using Clio.Command;
using Clio.UserEnvironment;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class ShowAppListCommandTestCase
{
    [Test]
    [Category("Unit")]
    public void Execute_CallsSettingsRepository()
    {
        ISettingsRepository settingsRepository = Substitute.For<ISettingsRepository>();
        ShowAppListCommand command = new (settingsRepository);
        AppListOptions options = new () { Name = "TestEnvironment" };
        command.Execute(options);
        settingsRepository.Received(1).ShowSettingsTo(Console.Out, options.Name);
    }
}
