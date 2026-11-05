namespace Clio.Tests.Command;

using System;
using Clio;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
internal class RestoreFromPackageBackupCommandTests : BaseCommandTests<RestoreFromPackageBackupOptions>
{

	private const string FailureResponse =
		"{\"errorInfo\":{\"errorCode\":\"Exception\",\"message\":\"Errors occured during configuration restoring from backup\",\"stackTrace\":null},\"success\":false}";

	private RestoreFromPackageBackupCommand _command;
	private IApplicationClient _applicationClient;
	private IApplicationLogProvider _applicationLogProvider;
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<RestoreFromPackageBackupCommand>();
	}

	public override void TearDown() {
		_applicationClient.ClearReceivedCalls();
		_applicationLogProvider.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_applicationLogProvider = Substitute.For<IApplicationLogProvider>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient<IApplicationClient>(_ => _applicationClient);
		containerBuilder.AddSingleton(_applicationLogProvider);
		containerBuilder.AddSingleton(_logger);
	}

	private void ArrangeResponse(string response) {
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(response);
	}

	private void ArrangeInstallationLog(string logBeforeRestore, string logAfterRestore) {
		_applicationLogProvider.GetInstallationLog(Arg.Any<IApplicationClient>(), Arg.Any<EnvironmentSettings>())
			.Returns(logBeforeRestore, logAfterRestore);
	}

	[Test]
	[Description("Posts the restore request once, with both option flags, to the RestoreFromPackageBackup endpoint and exits with 0 when the service reports success; a retried restore would answer success:true after a failed one")]
	public void Execute_PostsRestoreRequest_AndSucceeds_WhenServiceReportsSuccess() {
		// Arrange
		RestoreFromPackageBackupOptions options = new() {
			InstallPackageData = true,
			IgnoreSqlScriptBackwardCompatibilityCheck = true
		};
		ArrangeInstallationLog("before\n", "before\nConfiguration restoring from backup finished\n");
		ArrangeResponse("{\"success\":true}");

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "the service reported that the configuration was restored");
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(uri => uri.EndsWith("/ServiceModel/PackageInstallerService.svc/RestoreFromPackageBackup")),
			"{\"installPackageData\": true, \"ignoreSqlScriptBackwardCompatibilityCheck\": true}",
			Arg.Any<int>(), 1, Arg.Any<int>());
		_logger.Received(1).WriteInfo("Done restore-configuration");
		_logger.DidNotReceive().WriteError(Arg.Any<string>());
	}

	[Test]
	[Description("Defaults the request timeout to 60 minutes, because the restore is sent once and the service saves the packages, updates the DB structure and compiles inside that one request")]
	public void TimeOut_DefaultsTo60Minutes() {
		// Arrange
		RestoreFromPackageBackupOptions options = new();

		// Act
		int timeOut = options.TimeOut;

		// Assert
		timeOut.Should().Be((int)TimeSpan.FromMinutes(60).TotalMilliseconds,
			because: "with a single attempt the 100 s default would report a slow but successful restore as failed");
	}

	[Test]
	[Description("Exits with 1 and reports the service message when the service answers success:false, which is how a failed structure update during the restore is returned")]
	public void Execute_ReturnsFailure_WhenServiceReportsFailure() {
		// Arrange
		ArrangeInstallationLog("before\n", "before\nIt is not possible to change type of column UsrCode\n");
		ArrangeResponse(FailureResponse);

		// Act
		int result = _command.Execute(new RestoreFromPackageBackupOptions());

		// Assert
		result.Should().Be(1, because: "the service reported that the restore failed");
		_logger.Received(1).WriteError(
			"Configuration restore from backup failed: Errors occured during configuration restoring from backup");
		_logger.DidNotReceive().WriteInfo("Done restore-configuration");
	}

	[Test]
	[Description("Exits with 1 when the response carries no success flag, because a restore that is not confirmed must not be reported as done")]
	public void Execute_ReturnsFailure_WhenResponseHasNoSuccessFlag() {
		// Arrange
		ArrangeInstallationLog("before\n", "before\n");
		ArrangeResponse("{}");

		// Act
		int result = _command.Execute(new RestoreFromPackageBackupOptions());

		// Assert
		result.Should().Be(1, because: "the response does not confirm that the restore succeeded");
		_logger.Received(1).WriteError("Configuration restore from backup failed: Unknown error");
	}

	[TestCase("")]
	[TestCase("<html>500 - Internal Server Error</html>")]
	[TestCase("[true]")]
	[Description("Exits with 1 when the response is empty, is not JSON, or is not a JSON object")]
	public void Execute_ReturnsFailure_WhenResponseIsNotAServiceResponse(string response) {
		// Arrange
		ArrangeInstallationLog("before\n", "before\n");
		ArrangeResponse(response);

		// Act
		int result = _command.Execute(new RestoreFromPackageBackupOptions());

		// Assert
		result.Should().Be(1, because: "a response that is not a service response does not confirm the restore");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.StartsWith("Configuration restore from backup failed")));
	}

	[Test]
	[Description("Prints only the installation log entries written by this run, read through the session that ran the restore, because the log keeps every earlier installation")]
	public void Execute_PrintsOnlyThisRunsInstallationLog() {
		// Arrange
		const string earlierLog = "2026-10-29 16:02:11 Package \"UsrTstRbk\" loaded\n";
		const string logOfThisRun = "2026-10-29 16:04:19 It is not possible to change type of column UsrCode\n";
		ArrangeInstallationLog(earlierLog, earlierLog + logOfThisRun);
		ArrangeResponse(FailureResponse);

		// Act
		_command.Execute(new RestoreFromPackageBackupOptions());

		// Assert
		_applicationLogProvider.Received(2).GetInstallationLog(Arg.Is(_applicationClient), Arg.Any<EnvironmentSettings>());
		_logger.Received(1).Write(logOfThisRun);
		_logger.DidNotReceive().Write(Arg.Is<string>(value => value.Contains("UsrTstRbk\" loaded")));
	}

	[TestCase("")]
	[TestCase("2026-10-29 16:02:11 Log of the node before it restarted\n")]
	[Description("Prints the whole installation log with a warning when it does not continue the log read before the restore, so the reason of a failure is never hidden")]
	public void Execute_PrintsWholeInstallationLog_WhenItCannotBeAttributedToThisRun(string logBeforeRestore) {
		// Arrange
		const string completeLog = "It is not possible to change type of column UsrCode\n";
		ArrangeInstallationLog(logBeforeRestore, completeLog);
		ArrangeResponse(FailureResponse);

		// Act
		_command.Execute(new RestoreFromPackageBackupOptions());

		// Assert
		_logger.Received(1).Write(completeLog);
		_logger.Received(1).WriteWarning(Arg.Is<string>(message => message.Contains("printed in full")));
	}

	[Test]
	[Description("Warns that the installation log could not be read and still reports the service outcome")]
	public void Execute_WarnsAndKeepsOutcome_WhenInstallationLogCannotBeRead() {
		// Arrange
		ArrangeInstallationLog(string.Empty, string.Empty);
		ArrangeResponse(FailureResponse);

		// Act
		int result = _command.Execute(new RestoreFromPackageBackupOptions());

		// Assert
		result.Should().Be(1, because: "an unreadable log must not change the outcome the service reported");
		_logger.Received(1).WriteWarning("The installation log could not be read.");
		_logger.DidNotReceive().Write(Arg.Any<string>());
	}

}
