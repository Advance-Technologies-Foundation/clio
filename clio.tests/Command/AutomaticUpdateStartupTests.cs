using System;
using System.IO;
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
	[TestCase("mcp-server")]
	[TestCase("mcp")]
	[TestCase("mcp-http")]
	[Description("Skips automatic updates while an explicit command changes knowledge or toolkit files, and for every MCP host verb, which must never replace its own binaries.")]
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

	[Test]
	[Description("Defers only the clio self-update while a resident MCP host is recorded, and leaves the schedule untouched so the next cold start still performs it.")]
	public void RunStartupUpdateCheck_ShouldDeferClioUpdate_WhenMcpHostIsResident() {
		// Arrange
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.TryScheduleAutoupdate(Arg.Any<AutoUpdateTarget>(), Arg.Any<DateTimeOffset>()).Returns(true);
		IAppUpdater appUpdater = Substitute.For<IAppUpdater>();
		appUpdater.UpdateInBackgroundAsync().Returns(Task.CompletedTask);
		IKnowledgeSourceManagementService knowledge = Substitute.For<IKnowledgeSourceManagementService>();
		ISkillInstallService toolkit = Substitute.For<ISkillInstallService>();
		settings.IsAutoupdateDue(AutoUpdateTarget.Clio, Arg.Any<DateTimeOffset>()).Returns(true);
		IMcpHostPresenceRegistry presence = Substitute.For<IMcpHostPresenceRegistry>();
		presence.FindLiveHost().Returns(new McpHostPresenceMarker(4242, "8.1.0.120",
			DateTimeOffset.UtcNow, "mcp-server.4242.lock"));
		ServiceProvider services = new ServiceCollection()
			.AddSingleton(settings)
			.AddSingleton(appUpdater)
			.AddSingleton(knowledge)
			.AddSingleton(toolkit)
			.AddSingleton(presence)
			.BuildServiceProvider();

		// Act
		Program.RunStartupUpdateCheck(["ver"], services);

		// Assert
		appUpdater.DidNotReceive().UpdateInBackgroundAsync();
		settings.DidNotReceive().TryScheduleAutoupdate(AutoUpdateTarget.Clio, Arg.Any<DateTimeOffset>());
		knowledge.Received(1).Update(null);
		toolkit.Received(1).Update(null, null);
	}

	[Test]
	[Description("Performs the clio self-update as usual when no MCP host marker is live.")]
	public void RunStartupUpdateCheck_ShouldUpdateClio_WhenNoMcpHostIsResident() {
		// Arrange
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.TryScheduleAutoupdate(Arg.Any<AutoUpdateTarget>(), Arg.Any<DateTimeOffset>()).Returns(true);
		IAppUpdater appUpdater = Substitute.For<IAppUpdater>();
		appUpdater.UpdateInBackgroundAsync().Returns(Task.CompletedTask);
		IKnowledgeSourceManagementService knowledge = Substitute.For<IKnowledgeSourceManagementService>();
		ISkillInstallService toolkit = Substitute.For<ISkillInstallService>();
		IMcpHostPresenceRegistry presence = Substitute.For<IMcpHostPresenceRegistry>();
		presence.FindLiveHost().Returns((McpHostPresenceMarker)null);
		ServiceProvider services = new ServiceCollection()
			.AddSingleton(settings)
			.AddSingleton(appUpdater)
			.AddSingleton(knowledge)
			.AddSingleton(toolkit)
			.AddSingleton(presence)
			.BuildServiceProvider();

		// Act
		Program.RunStartupUpdateCheck(["ver"], services);

		// Assert
		appUpdater.Received(1).UpdateInBackgroundAsync();
		settings.Received(1).TryScheduleAutoupdate(AutoUpdateTarget.Clio, Arg.Any<DateTimeOffset>());
	}

	[SetUp]
	[TearDown]
	public void ResetSettingsWriteRefusalLatch() {
		// The latch is process-wide (one refusal per run, not one per target), so leaving it set would
		// decide the outcome of whichever test runs next by execution order.
		Program.SettingsWriteRefusalReported = false;
	}

	[Test]
	[Description("Reports a settings-write refusal instead of swallowing it, because that refusal also blocks the update that would have ended the skew.")]
	public void RunStartupUpdateCheck_ShouldReportTheRefusal_WhenSettingsCannotBeWritten() {
		// Arrange
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.TryScheduleAutoupdate(Arg.Any<AutoUpdateTarget>(), Arg.Any<DateTimeOffset>())
			.Returns(_ => throw new SettingsShapeMismatchException(
				"Cannot update settings (settings-shape-mismatch): ... use 'clio update-cli'."));
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
		Action act = () => Program.RunStartupUpdateCheck(["ver"], services);

		// Assert
		act.Should().NotThrow(
			because: "a refused update must never fail the command the user actually asked for");
		Program.SettingsWriteRefusalReported.Should().BeTrue(
			because: "the refusal must be REPORTED, not swallowed: it is what stops clio from updating its way out of the skew");
		appUpdater.DidNotReceive().UpdateInBackgroundAsync();
		settings.Received(3).TryScheduleAutoupdate(Arg.Any<AutoUpdateTarget>(), Arg.Any<DateTimeOffset>());
	}
}

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
// Replaces the process-wide console writers, so it must not overlap any other fixture.
[NonParallelizable]
public sealed class AutomaticUpdateStartupNoticeTests {
	[Test]
	[Description("Writes the deferral notice to stderr, never stdout, because it precedes the output of every command and callers such as Clio Explorer parse stdout (issue #1665).")]
	public void RunStartupUpdateCheck_ShouldWriteDeferralNoticeToStderr_WhenMcpHostIsResident() {
		// Arrange
		ISettingsRepository settings = Substitute.For<ISettingsRepository>();
		settings.IsAutoupdateDue(AutoUpdateTarget.Clio, Arg.Any<DateTimeOffset>()).Returns(true);
		IMcpHostPresenceRegistry presence = Substitute.For<IMcpHostPresenceRegistry>();
		presence.FindLiveHost().Returns(new McpHostPresenceMarker(4242, "8.1.0.120",
			DateTimeOffset.UtcNow, "mcp-server.4242.lock"));
		ServiceProvider services = new ServiceCollection()
			.AddSingleton(settings)
			.AddSingleton(Substitute.For<IAppUpdater>())
			.AddSingleton(Substitute.For<IKnowledgeSourceManagementService>())
			.AddSingleton(Substitute.For<ISkillInstallService>())
			.AddSingleton(presence)
			.BuildServiceProvider();
		ConsoleLogger logger = (ConsoleLogger)ConsoleLogger.Instance;
		logger.Start();
		logger.FlushAndSnapshotMessages();
		TextWriter originalOut = Console.Out;
		TextWriter originalError = Console.Error;
		StringWriter stdout = new();
		StringWriter stderr = new();
		Console.SetOut(stdout);
		Console.SetError(stderr);

		// Act
		try {
			Program.RunStartupUpdateCheck(["info", "-s"], services);
			logger.FlushAndSnapshotMessages();
		}
		finally {
			Console.SetOut(originalOut);
			Console.SetError(originalError);
		}

		// Assert
		stderr.ToString().Should().Contain(
			"[INF] - clio self-update deferred: MCP host pid 4242 (version 8.1.0.120) is running",
			because: "the operator still needs to learn that the update was postponed");
		stdout.ToString().Should().NotContain("self-update deferred",
			because: "stdout carries the command's own output, which other programs read line by line");
	}
}
