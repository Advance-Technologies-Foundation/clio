using System;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using Clio.Common.Skills;
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
	[Description("Runs the existing clio, knowledge, and toolkit services independently when their schedules are due.")]
	public void RunStartupUpdateCheck_ShouldStartExistingCommands_WhenSchedulesAreDue() {
		// Arrange
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.TryScheduleAutoupdate(Arg.Any<AutoUpdateTarget>(), Arg.Any<DateTimeOffset>()).Returns(true);
		IAppUpdater appUpdater = Substitute.For<IAppUpdater>();
		appUpdater.UpdateInBackgroundAsync().Returns(Task.CompletedTask);
		IKnowledgeSourceManagementService knowledge = Substitute.For<IKnowledgeSourceManagementService>();
		knowledge.When(service => service.Update(null)).Do(_ => throw new InvalidOperationException("unavailable"));
		ISkillInstallService toolkit = Substitute.For<ISkillInstallService>();
		ServiceProvider services = new ServiceCollection()
			.AddSingleton(settings)
			.AddSingleton(appUpdater)
			.AddSingleton(knowledge)
			.AddSingleton(toolkit)
			.BuildServiceProvider();

		// Act
		Program.RunStartupUpdateCheck(["ver"], services);

		// Assert
		appUpdater.Received(1).UpdateInBackgroundAsync();
		knowledge.Received(1).Update(null);
		toolkit.Received(1).Update(null, null);
	}

	[Test]
	[Description("Does not resolve or launch an updater whose schedule is disabled or not yet due.")]
	public void RunStartupUpdateCheck_ShouldNotLaunchCommand_WhenScheduleIsNotDue() {
		// Arrange
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.TryScheduleAutoupdate(Arg.Any<AutoUpdateTarget>(), Arg.Any<DateTimeOffset>()).Returns(false);
		IAppUpdater appUpdater = Substitute.For<IAppUpdater>();
		IKnowledgeSourceManagementService knowledge = Substitute.For<IKnowledgeSourceManagementService>();
		ISkillInstallService toolkit = Substitute.For<ISkillInstallService>();
		ServiceProvider services = new ServiceCollection()
			.AddSingleton(settings)
			.AddSingleton(appUpdater)
			.AddSingleton(knowledge)
			.AddSingleton(toolkit)
			.BuildServiceProvider();

		// Act
		Program.RunStartupUpdateCheck(["ver"], services);

		// Assert
		appUpdater.DidNotReceive().UpdateInBackgroundAsync();
		knowledge.DidNotReceive().Update(null);
		toolkit.DidNotReceive().Update(null, null);
	}

	[TestCase("install-knowledge")]
	[TestCase("update-knowledge")]
	[TestCase("delete-knowledge")]
	[TestCase("add-knowledge-source")]
	[TestCase("remove-knowledge-source")]
	[TestCase("enable-knowledge-source")]
	[TestCase("disable-knowledge-source")]
	[TestCase("install-toolkit")]
	[TestCase("install-skills")]
	[TestCase("update-toolkit")]
	[TestCase("update-skill")]
	[TestCase("delete-toolkit")]
	[TestCase("delete-skill")]
	[Description("Skips automatic updates while an explicit command changes knowledge or toolkit files.")]
	public void RunStartupUpdateCheck_ShouldSkipUpdate_WhenCommandMutatesUpdateTarget(string command) {
		// Arrange
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		ServiceProvider services = new ServiceCollection().AddSingleton(settings).BuildServiceProvider();

		// Act
		Program.RunStartupUpdateCheck([command], services);

		// Assert
		settings.DidNotReceive().TryScheduleAutoupdate(
			Arg.Any<AutoUpdateTarget>(), Arg.Any<DateTimeOffset>());
	}
}
