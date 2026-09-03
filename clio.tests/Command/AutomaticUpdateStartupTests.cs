using System;
using System.Linq;
using System.Threading.Tasks;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class AutomaticUpdateStartupTests {
	[Test]
	[Description("Starts the existing knowledge and toolkit commands independently when their schedules are due.")]
	public void RunStartupUpdateCheck_ShouldStartExistingCommands_WhenSchedulesAreDue() {
		// Arrange
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.TryScheduleAutoupdate(Arg.Any<AutoUpdateTarget>(), Arg.Any<DateTimeOffset>()).Returns(true);
		IAppUpdater appUpdater = Substitute.For<IAppUpdater>();
		appUpdater.CheckForUpdateWithCacheAsync(Arg.Any<string>())
			.Returns(Task.FromResult((false, (string)null)));
		IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();
		processExecutor.FireAndForgetAsync(Arg.Any<ProcessExecutionOptions>())
			.Returns(call => call.Arg<ProcessExecutionOptions>().ArgumentList.Contains("update-knowledge")
				? throw new InvalidOperationException("unavailable")
				: Task.FromResult(new ProcessLaunchResult { Started = true }));
		ServiceProvider services = new ServiceCollection()
			.AddSingleton(settings)
			.AddSingleton(appUpdater)
			.AddSingleton(processExecutor)
			.BuildServiceProvider();

		// Act
		Program.RunStartupUpdateCheck(["ver"], services);

		// Assert
		processExecutor.Received(1).FireAndForgetAsync(Arg.Is<ProcessExecutionOptions>(options =>
			options.Program == "dotnet" && options.ArgumentList.Contains("update-knowledge")));
		processExecutor.Received(1).FireAndForgetAsync(Arg.Is<ProcessExecutionOptions>(options =>
			options.Program == "dotnet" && options.ArgumentList.Contains("update-toolkit")));
	}

	[Test]
	[Description("Does not resolve or launch an updater whose schedule is disabled or not yet due.")]
	public void RunStartupUpdateCheck_ShouldNotLaunchCommand_WhenScheduleIsNotDue() {
		// Arrange
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.TryScheduleAutoupdate(Arg.Any<AutoUpdateTarget>(), Arg.Any<DateTimeOffset>()).Returns(false);
		ServiceProvider services = new ServiceCollection().AddSingleton(settings).BuildServiceProvider();

		// Act
		Action act = () => Program.RunStartupUpdateCheck(["ver"], services);

		// Assert
		act.Should().NotThrow(because: "not-due schedules must avoid resolving update services entirely");
	}
}
