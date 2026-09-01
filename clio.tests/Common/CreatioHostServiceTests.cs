using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Tests.Command;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
public sealed class CreatioHostServiceTests : BaseClioModuleTests {
	private IProcessExecutor _processExecutor;
	private ICreatioHostEnvironmentStore _environmentStore;
	private ICreatioHostService _sut;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		_processExecutor = Substitute.For<IProcessExecutor>();
		_environmentStore = Substitute.For<ICreatioHostEnvironmentStore>();
		containerBuilder.AddSingleton(_processExecutor);
		containerBuilder.AddSingleton(_environmentStore);
	}

	[SetUp]
	public void SetUp() {
		_sut = Container.GetRequiredService<ICreatioHostService>();
	}

	[TearDown]
	public override void TearDown() {
		_processExecutor.ClearReceivedCalls();
		_environmentStore.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Passes sensitive dotnet host configuration through the child process environment without adding it to command arguments.")]
	public async Task StartInBackground_ShouldPassEnvironmentVariablesToChildHost() {
		// Arrange
		_processExecutor.FireAndForgetAsync(Arg.Any<ProcessExecutionOptions>()).Returns(
			Task.FromResult(new ProcessLaunchResult { Started = true, ProcessId = 42 }));
		IReadOnlyDictionary<string, string> environmentVariables = new Dictionary<string, string> {
			["Kestrel__Endpoints__Https__Certificate__Password"] = "secret"
		};

		// Act
		int? processId = _sut.StartInBackground("/tmp/creatio", environmentVariables);

		// Assert
		processId.Should().Be(42,
			because: "the host process identifier must be returned after a successful detached launch");
		await _processExecutor.Received(1).FireAndForgetAsync(Arg.Is<ProcessExecutionOptions>(options =>
			options.Arguments == "Terrasoft.WebHost.dll"
			&& options.EnvironmentVariables["Kestrel__Endpoints__Https__Certificate__Password"] == "secret"
			&& !options.Arguments.Contains("secret")
			&& options.ClearInheritedEnvironment
			&& options.InheritedEnvironmentVariableAllowlist.Contains("PATH")
			&& options.InheritedEnvironmentVariableAllowlist.Contains("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT")
			&& options.InheritedEnvironmentVariableAllowlist.Contains("LD_LIBRARY_PATH")
			&& options.InheritedEnvironmentVariableAllowlist.Contains("ASPNETCORE_ENVIRONMENT")
			&& options.InheritedEnvironmentVariableAllowlist.Contains("DOTNET_ENVIRONMENT")
			&& !options.InheritedEnvironmentVariableAllowlist.Contains("Kestrel__Endpoints__Https__Certificate__Password")));
	}

	[Test]
	[Description("Restores persisted dotnet host environment values when a later background start omits explicit values.")]
	public async Task StartInBackground_ShouldLoadPersistedEnvironmentVariables_WhenNotSupplied() {
		// Arrange
		_processExecutor.FireAndForgetAsync(Arg.Any<ProcessExecutionOptions>()).Returns(
			Task.FromResult(new ProcessLaunchResult { Started = true, ProcessId = 43 }));
		IReadOnlyDictionary<string, string> persistedEnvironment = new Dictionary<string, string> {
			["Kestrel__Endpoints__Https__Certificate__Password"] = "persisted-secret"
		};
		_environmentStore.Load("/tmp/creatio").Returns(persistedEnvironment);

		// Act
		int? processId = _sut.StartInBackground("/tmp/creatio");

		// Assert
		processId.Should().Be(43,
			because: "a later start must return the detached host process identifier after restoring its saved environment");
		_environmentStore.Received(1).Load("/tmp/creatio");
		await _processExecutor.Received(1).FireAndForgetAsync(Arg.Is<ProcessExecutionOptions>(options =>
			options.EnvironmentVariables["Kestrel__Endpoints__Https__Certificate__Password"] == "persisted-secret"));
	}

	[Test]
	[Description("Delegates deployment host environment persistence to the protected environment store.")]
	public void PersistEnvironmentVariables_ShouldDelegateToStore() {
		// Arrange
		IReadOnlyDictionary<string, string> environmentVariables = new Dictionary<string, string> {
			["Kestrel__Endpoints__Https__Certificate__Password"] = "secret"
		};

		// Act
		_sut.PersistEnvironmentVariables("/tmp/creatio", environmentVariables);

		// Assert
		_environmentStore.Received(1).Save("/tmp/creatio", environmentVariables);
	}

	[Test]
	[Description("Quotes macOS terminal launcher paths and labels while loading certificate values from the protected store instead of embedding secrets in the shell script.")]
	public void BuildTerminalLaunchScript_ShouldQuoteDynamicValues() {
		// Arrange
		const string workingDirectory = "/tmp/creatio; touch /tmp/pwned/'quoted";
		const string environmentName = "dev; touch /tmp/name-pwned";
		IReadOnlyDictionary<string, string> environmentVariables = new Dictionary<string, string> {
			["Kestrel__Endpoints__Https__Certificate__Password"] = "secret'with;metachar"
		};

		// Act
		string script = CreatioHostService.BuildTerminalLaunchScript(
			workingDirectory,
			environmentName,
			environmentVariables);

		// Assert
		script.Should().Contain(
			$"export Kestrel__Endpoints__Https__Certificate__Password=\"$(/usr/bin/plutil -extract 'Kestrel__Endpoints__Https__Certificate__Password' raw -o - '{CreatioHostEnvironmentStore.GetStorePath(workingDirectory)}')\"",
			because: "the launcher must load certificate values from the protected store at runtime");
		script.Should().NotContain("secret'with;metachar",
			because: "a terminated terminal launcher must not leave the certificate password embedded on disk");
		script.Should().Contain(
			"cd -- '/tmp/creatio; touch /tmp/pwned/'\\''quoted'",
			because: "the terminal must enter the registered application directory without executing path metacharacters");
		script.Should().Contain(
			"echo 'Starting Creatio [dev; touch /tmp/name-pwned]...'",
			because: "the environment label must remain a literal display value in the terminal script");
		script.Should().EndWith("dotnet Terrasoft.WebHost.dll" + Environment.NewLine,
			because: "the launcher must run only the fixed Creatio host command after setting its safe inputs");
	}

	[Test]
	[Description("Rejects invalid environment variable names before a POSIX terminal launcher can interpret them as shell syntax.")]
	public void BuildTerminalLaunchScript_ShouldRejectInvalidEnvironmentVariableNames() {
		// Arrange
		IReadOnlyDictionary<string, string> environmentVariables = new Dictionary<string, string> {
			["Kestrel-Https-Password"] = "secret"
		};

		// Act
		Action act = () => CreatioHostService.BuildTerminalLaunchScript(
			"/tmp/creatio",
			"dev",
			environmentVariables);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "a terminal launcher must not turn an invalid environment key into executable shell syntax");
	}
}
