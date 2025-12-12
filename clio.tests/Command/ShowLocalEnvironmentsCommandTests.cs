using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using ConsoleTables;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class ShowLocalEnvironmentsCommandTests : BaseCommandTests<ShowLocalEnvironmentsOptions>
{
	private ISettingsRepository _settingsRepository;
	private IApplicationClientFactory _clientFactory;
	private ILogger _logger;
	private MockFileSystem _fileSystem;
	private ShowLocalEnvironmentsCommand _command;
	private string _capturedOutput;

	[SetUp]
	public override void Setup() {
		base.Setup();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_clientFactory = Substitute.For<IApplicationClientFactory>();
		_logger = Substitute.For<ILogger>();
		_fileSystem = new MockFileSystem();
		_command = new ShowLocalEnvironmentsCommand(_settingsRepository, _clientFactory, _fileSystem, _logger);
		_logger
			.When(l => l.Write(Arg.Any<string>()))
			.Do(callInfo => _capturedOutput = callInfo.Arg<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Returns OK when ping and login succeed.")]
	public void Execute_ShouldReturnOk_WhenPingAndLoginSucceed() {
		// Arrange
		EnvironmentSettings environment = CreateEnvironment("http://localhost", "/env/app", isNetCore: true);
		SetupFileSystemWithContent(environment.EnvironmentPath);
		SetupEnvironments(environment);
		SetupPingSuccess(environment);
		SetupLoginSuccess(environment);

		// Act
		int result = _command.Execute(new ShowLocalEnvironmentsOptions());

		// Assert
		result.Should().Be(0, "command should succeed when ping and login succeed");
		_capturedOutput.Should().Contain("| Name", "table header should be present");
		_capturedOutput.Should().Contain("OK", "status should be OK after successful ping and login");
		_capturedOutput.Should().Contain("healthy", "OK status should include healthy reason");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns Error Auth data when ping succeeds but login fails.")]
	public void Execute_ShouldReturnAuthError_WhenLoginFails() {
		// Arrange
		EnvironmentSettings environment = CreateEnvironment("http://localhost", "/env/app", isNetCore: false);
		SetupFileSystemWithContent(environment.EnvironmentPath);
		SetupEnvironments(environment);
		SetupPingSuccess(environment);
		SetupLoginFailure(environment, "login failed");

		// Act
		int result = _command.Execute(new ShowLocalEnvironmentsOptions());

		// Assert
		result.Should().Be(0, "command should complete even when login fails");
		_capturedOutput.Should().Contain("Error Auth data", "login failure should set Error Auth data status");
		_capturedOutput.Should().Contain("login failed", "reason should explain login failure");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns Deleted when environment directory is missing.")]
	public void Execute_ShouldReturnDeleted_WhenDirectoryMissing() {
		// Arrange
		EnvironmentSettings environment = CreateEnvironment("http://localhost", "/missing/env", isNetCore: true);
		SetupEnvironments(environment);

		// Act
		int result = _command.Execute(new ShowLocalEnvironmentsOptions());

		// Assert
		result.Should().Be(0, "command should complete even when directory is missing");
		_capturedOutput.Should().Contain("Deleted", "missing directory should be marked as Deleted");
		_capturedOutput.Should().Contain("not found", "reason should mention missing directory");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns Not runned when ping fails but directory has content.")]
	public void Execute_ShouldReturnNotRunned_WhenPingFails() {
		// Arrange
		EnvironmentSettings environment = CreateEnvironment("http://localhost", "/env/app", isNetCore: true);
		SetupFileSystemWithContent(environment.EnvironmentPath);
		SetupEnvironments(environment);
		SetupPingFailure(environment, "ping failed");

		// Act
		int result = _command.Execute(new ShowLocalEnvironmentsOptions());

		// Assert
		result.Should().Be(0, "command should handle ping failures gracefully");
		_capturedOutput.Should().Contain("Not runned", "ping failure should set Not runned status");
		_capturedOutput.Should().Contain("ping failed", "reason should carry ping failure message");
	}

	private static EnvironmentSettings CreateEnvironment(string uri, string path, bool isNetCore) => new() {
		Uri = uri,
		EnvironmentPath = path,
		IsNetCore = isNetCore
	};

	private void SetupEnvironments(EnvironmentSettings environment) {
		_settingsRepository.GetAllEnvironments().Returns(new Dictionary<string, EnvironmentSettings> {
			{ "env", environment }
		});
	}

	private void SetupFileSystemWithContent(string path) {
		_fileSystem.AddDirectory(path);
		_fileSystem.AddFile(Path.Combine(path, "web.config"), new MockFileData(""));
		_fileSystem.AddDirectory(Path.Combine(path, "Bin"));
	}

	private void SetupPingSuccess(EnvironmentSettings environment) {
		IApplicationClient pingClient = Substitute.For<IApplicationClient>();
		_clientFactory.CreateEnvironmentClient(environment).Returns(pingClient);
	}

	private void SetupPingFailure(EnvironmentSettings environment, string message) {
		IApplicationClient pingClient = Substitute.For<IApplicationClient>();
		_clientFactory.CreateEnvironmentClient(environment).Returns(pingClient);
		pingClient
			.When(c => c.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()))
			.Do(_ => throw new System.Exception(message));
		pingClient
			.When(c => c.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()))
			.Do(_ => throw new System.Exception(message));
	}

	private void SetupLoginSuccess(EnvironmentSettings environment) {
		IApplicationClient loginClient = Substitute.For<IApplicationClient>();
		_clientFactory.CreateClient(environment).Returns(loginClient);
	}

	private void SetupLoginFailure(EnvironmentSettings environment, string message) {
		IApplicationClient loginClient = Substitute.For<IApplicationClient>();
		_clientFactory.CreateClient(environment).Returns(loginClient);
		loginClient.When(c => c.Login()).Do(_ => throw new System.Exception(message));
	}
}
