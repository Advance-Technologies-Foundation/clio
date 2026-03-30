namespace Clio.Tests.Command;

using System.Threading;
using Clio.Command;
using Clio.Common;
using Clio.Query;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
public class HealthCheckCommandTestCase
{
	private EnvironmentSettings _environmentSettings;
	private IApplicationClient _applicationClient;
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
		_hcCommand = new HealthCheckCommand(_applicationClient, _environmentSettings);
	}


	[Test, Category("Unit")]
	public void HealthCheckCommand_FormsCorrectApplicationRequest_WhenWebHostIsTrue() {
		HealthCheckOptions options = new() { WebHost = "true" };
		_hcCommand.Execute(options);
		_applicationClient.Received(1).ExecuteGetRequest(
			_environmentSettings.Uri + "/0/api/HealthCheck/Ping",
			options.TimeOut,
			options.RetryCount,
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
			options.RetryCount,
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
			options.RetryCount,
			options.RetryDelay);
	}

	[Test, Category("Unit")]
	public void HealthCheckCommand_UsesConfiguredNetCoreRoute_WhenNoFlagsProvided()
	{
		_environmentSettings.IsNetCore = true;
		_hcCommand = new HealthCheckCommand(_applicationClient, _environmentSettings);
		HealthCheckOptions options = new();
		_hcCommand.Execute(options);
		_applicationClient.Received(1).ExecuteGetRequest(
			_environmentSettings.Uri + "/api/HealthCheck/Ping",
			options.TimeOut,
			options.RetryCount,
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
	public void HealthCheckCommand_IsRegistered()
	{
		BindingsModule bs = new BindingsModule();
		var container = bs.Register(_environmentSettings);
		var command = container.GetRequiredService<HealthCheckCommand>();
		Assert.That(command, Is.Not.Null);
	}
}
