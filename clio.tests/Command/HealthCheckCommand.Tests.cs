namespace Clio.Tests.Command;

using System.Threading;
using Clio.Command;
using Clio.Common;
using Clio.Query;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class HealthCheckCommandTestCase
{
	private EnvironmentSettings _environmentSettings;
	private IApplicationClient _applicationClient;
	private IJsonResponseFormater _jsonResponseFormater;
	private HealthCheckCommand _hcCommand;
	[SetUp]
	public void SetUp()
	{
		_environmentSettings = new EnvironmentSettings
		{
			Login = "Test",
			Password = "Test",
			IsNetCore = false,
			Maintainer = "Test",
			Uri = "http://test.domain.com"
		};
		_applicationClient = Substitute.For<IApplicationClient>();
		_jsonResponseFormater = Substitute.For<IJsonResponseFormater>();
		_hcCommand = new HealthCheckCommand(_applicationClient, _environmentSettings, _jsonResponseFormater);
	}


	[Test, Category("Unit")]
	public void HealthCheckCommand_FormsCorrectApplicationRequest_WhenWebHostIsTrue() {
		HealthCheckOptions options = new() { WebHost = "true" };
		_hcCommand.Execute(options);
		_applicationClient.Received(1).ExecuteGetRequest(
			_environmentSettings.Uri + "/0/api/HealthCheck/Ping",
			options.TimeOut,
			options.MaxAttempts,
			options.RetryDelay);
	}


	[Test, Category("Unit")]
	public void HealthCheckCommand_FormsCorrectApplicationRequest_WhenWebAppIsTrue()
	{
		HealthCheckOptions options = new() { WebApp = "true" };
		_hcCommand.Execute(options);
		_applicationClient.Received(1).ExecuteGetRequest(
			_environmentSettings.Uri + "/api/HealthCheck/Ping",
			options.TimeOut,
			options.MaxAttempts,
			options.RetryDelay);
	}

	[Test, Category("Unit")]
	public void HealthCheckCommand_UsesConfiguredFrameworkRoute_WhenNoFlagsProvided()
	{
		HealthCheckOptions options = new();
		_hcCommand.Execute(options);
		_applicationClient.Received(1).ExecuteGetRequest(
			_environmentSettings.Uri + "/0/api/HealthCheck/Ping",
			options.TimeOut,
			options.MaxAttempts,
			options.RetryDelay);
	}

	[Test, Category("Unit")]
	public void HealthCheckCommand_UsesConfiguredNetCoreRoute_WhenNoFlagsProvided()
	{
		_environmentSettings.IsNetCore = true;
		_hcCommand = new HealthCheckCommand(_applicationClient, _environmentSettings, _jsonResponseFormater);
		HealthCheckOptions options = new();
		_hcCommand.Execute(options);
		_applicationClient.Received(1).ExecuteGetRequest(
			_environmentSettings.Uri + "/api/HealthCheck/Ping",
			options.TimeOut,
			options.MaxAttempts,
			options.RetryDelay);
	}

	[Test, Category("Unit")]
	public void HealthCheckCommand_ReturnsFailure_WhenRequestThrows()
	{
		HealthCheckOptions options = new();
		_applicationClient
			.When(client => client.ExecuteGetRequest(
				Arg.Any<string>(),
				Arg.Any<int>(),
				Arg.Any<int>(),
				Arg.Any<int>()))
			.Do(_ => throw new System.Net.WebException("boom"));
		int result = _hcCommand.Execute(options);
		Assert.That(result, Is.EqualTo(1));
	}

	[Test, Category("Unit")]
	[Description("Execute should emit a success envelope (via FormatEnvelope) and return 0 when --json is set and all probes succeed")]
	public void Execute_ShouldEmitSuccessEnvelope_WhenJsonAndHealthy() {
		// Arrange
		HealthCheckOptions options = new() { Json = true };

		// Act
		int result = _hcCommand.Execute(options);

		// Assert
		Assert.That(result, Is.EqualTo(0));
		_jsonResponseFormater.Received(1).FormatEnvelope("healthcheck", Arg.Any<HealthCheckResult>());
	}

	[Test, Category("Unit")]
	[Description("Execute should emit an error envelope (ok=false with healthcheck-failed) and return 1 when --json is set and a probe fails")]
	public void Execute_ShouldEmitErrorEnvelope_WhenJsonAndProbeFails() {
		// Arrange
		HealthCheckOptions options = new() { Json = true };
		_applicationClient
			.When(client => client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()))
			.Do(_ => throw new System.Net.WebException("boom"));

		// Act
		int result = _hcCommand.Execute(options);

		// Assert
		Assert.That(result, Is.EqualTo(1));
		_jsonResponseFormater.Received(1).FormatEnvelope("healthcheck",
			Clio.Common.CommandErrorCodes.HealthCheckFailed, Arg.Any<string>());
	}

	[Test, Category("Unit")]
	[Description("Execute in non-JSON mode should write the exact human progress/outcome lines in order — AC#3/C1 text-output regression guard for the Probe refactor")]
	public void Execute_ShouldWriteHumanLinesInOrder_WhenNonJsonAndHealthy() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		_hcCommand.Logger = logger;
		HealthCheckOptions options = new() { WebHost = "true" };

		// Act
		_hcCommand.Execute(options);

		// Assert
		Received.InOrder(() => {
			logger.WriteInfo($"Checking WebHost {_environmentSettings.Uri}/0/api/HealthCheck/Ping ...");
			logger.WriteInfo("\tWebHost - OK");
		});
		logger.DidNotReceive().WriteLine(Arg.Any<string>()); // no JSON envelope in non-json mode
	}

	[Test, Category("Unit")]
	[Description("Execute in non-JSON mode should NOT emit a JSON envelope even when a probe fails (text-output regression guard)")]
	public void Execute_ShouldNotEmitEnvelope_WhenNonJsonAndProbeFails() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		_hcCommand.Logger = logger;
		_applicationClient
			.When(client => client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()))
			.Do(_ => throw new System.Net.WebException("boom"));

		// Act
		int result = _hcCommand.Execute(new HealthCheckOptions { WebHost = "true" });

		// Assert
		Assert.That(result, Is.EqualTo(1));
		logger.Received(1).WriteError("\tError: boom");
		logger.DidNotReceive().WriteLine(Arg.Any<string>());
		_jsonResponseFormater.DidNotReceive().FormatEnvelope(Arg.Any<string>(), Arg.Any<HealthCheckResult>());
	}

	[Test, Category("Unit")]
	public void HealthCheckCommand_IsRegistered()
	{
		BindingsModule bs = new BindingsModule();
		var container = bs.Register(_environmentSettings);
		var command = container.GetRequiredService<HealthCheckCommand>();
		Assert.That(command, Is.Not.Null);
	}
}
