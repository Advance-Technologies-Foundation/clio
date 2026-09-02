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
			// Ordinary host configuration is INHERITED: an allowlist dropped ConnectionStrings__*,
			// forwarded-header, proxy and telemetry settings the previous launcher passed through.
			&& !options.ClearInheritedEnvironment
			&& options.RemovedInheritedEnvironmentVariables.Contains("ASPNETCORE_URLS")
			&& options.RemovedInheritedEnvironmentVariables.Contains("ASPNETCORE_HTTP_PORTS")
			&& options.RemovedInheritedEnvironmentVariables.Contains("DOTNET_URLS")));
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
	[Description("Loads protected certificate values only in the final foreground dotnet host process, not in a terminal launcher process.")]
	public async Task StartInForeground_ShouldPassPersistedEnvironmentToHostProcess() {
		// Arrange
		_environmentStore.Load("/tmp/creatio").Returns(new Dictionary<string, string> {
			["Kestrel__Endpoints__Https__Certificate__Password"] = "persisted-secret"
		});
		_processExecutor.ExecuteWithRealtimeOutputAsync(Arg.Any<ProcessExecutionOptions>()).Returns(
			Task.FromResult(new ProcessExecutionResult { Started = true, ExitCode = 0 }));

		// Act
		int exitCode = _sut.StartInForeground("/tmp/creatio");

		// Assert
		exitCode.Should().Be(0,
			because: "the foreground launcher must return the host process exit code");
		await _processExecutor.Received(1).ExecuteWithRealtimeOutputAsync(Arg.Is<ProcessExecutionOptions>(options =>
			options.Arguments == "Terrasoft.WebHost.dll"
			&& options.EnvironmentVariables["Kestrel__Endpoints__Https__Certificate__Password"] == "persisted-secret"
			&& !options.ClearInheritedEnvironment
			&& options.RemovedInheritedEnvironmentVariables.Contains("ASPNETCORE_URLS")
			&& options.MirrorOutputToLogger));
	}

	[Test]
	[Description("Strips only the listener overrides and ambient certificate passwords; ordinary host configuration is left to be inherited.")]
	public void UnsafeInheritedEnvironmentVariables_ShouldRemoveOnlyBindingOverridesAndAmbientPasswords() {
		// Arrange
		Environment.SetEnvironmentVariable("Kestrel__Endpoints__Ambient__Certificate__Password", "ambient-secret");
		Environment.SetEnvironmentVariable("ConnectionStrings__db", "Server=.;");
		try {
			// Act
			IReadOnlyCollection<string> removed = CreatioHostService.UnsafeInheritedEnvironmentVariables();

			// Assert
			removed.Should().Contain("ASPNETCORE_URLS",
				because: "an ambient listener override would put the host back on a wildcard address");
			removed.Should().Contain("Kestrel__Endpoints__Ambient__Certificate__Password",
				because: "a certificate password must come from the protected store, not from the shell");
			removed.Should().NotContain("ConnectionStrings__db",
				because: "ordinary ASP.NET Core configuration keeps being inherited as it was before the hardening");
			removed.Should().NotContain("PATH",
				because: "nothing outside the two unsafe groups is removed");
		} finally {
			Environment.SetEnvironmentVariable("Kestrel__Endpoints__Ambient__Certificate__Password", null);
			Environment.SetEnvironmentVariable("ConnectionStrings__db", null);
		}
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
			$"'Kestrel__Endpoints__Https__Certificate__Password'=\"$(/usr/bin/plutil -extract 'Kestrel__Endpoints__Https__Certificate__Password' raw -o - '{CreatioHostEnvironmentStore.GetStorePath(workingDirectory)}')\" \\",
			because: "the launcher must load certificate values from the protected store at runtime");
		script.Should().Contain("/usr/bin/env \\",
			because: "the wrapper shell must remain alive so its EXIT trap removes the one-shot launcher script");
		script.Should().NotContain("secret'with;metachar",
			because: "a terminated terminal launcher must not leave the certificate password embedded on disk");
		script.Should().Contain(
			"cd -- '/tmp/creatio; touch /tmp/pwned/'\\''quoted'",
			because: "the terminal must enter the registered application directory without executing path metacharacters");
		script.Should().Contain(
			"echo 'Starting Creatio [dev; touch /tmp/name-pwned]...'",
			because: "the environment label must remain a literal display value in the terminal script");
		script.Should().EndWith("  dotnet Terrasoft.WebHost.dll" + Environment.NewLine,
			because: "the launcher must run only the fixed Creatio host command after setting its safe inputs");
	}

	[Test]
	[Description("Passes a hyphenated Kestrel endpoint environment key through env without interpreting it as shell assignment syntax")]
	public void BuildTerminalLaunchScript_ShouldSupportHyphenatedKestrelEnvironmentKeys()
	{
		// Arrange
		const string key = "Kestrel__Endpoints__https-prod__Certificate__Password";

		// Act
		string script = CreatioHostService.BuildTerminalLaunchScript(
			"/tmp/creatio",
			"dev",
			new Dictionary<string, string> { [key] = "secret" });

		// Assert
		script.Should().Contain($"'{key}'=\"$(/usr/bin/plutil -extract '{key}' raw -o -",
			because: "the env utility accepts the generated Kestrel key while shell quoting protects it");
	}

	[Test]
	[Description("Rejects invalid environment variable names before a POSIX terminal launcher can interpret them as shell syntax.")]
	public void BuildTerminalLaunchScript_ShouldRejectInvalidEnvironmentVariableNames() {
		// Arrange
		IReadOnlyDictionary<string, string> environmentVariables = new Dictionary<string, string> {
			["Kestrel__Endpoints__https.prod__Certificate__Password"] = "secret"
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

	[Test]
	[Description("Rejects Windows terminal environment names containing command metacharacters instead of interpolating them into cmd.exe arguments.")]
	public void EscapeWindowsCommandArgument_ShouldRejectCommandMetacharacters() {
		// Arrange
		const string hostileEnvironmentName = "dev\" & whoami";

		// Act
		Action act = () => CreatioHostService.EscapeWindowsCommandArgument(hostileEnvironmentName);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "registered environment names must not become executable cmd.exe syntax");
	}

	[Test]
	[Description("Reports a failed Windows, Linux, or macOS terminal launcher instead of treating an unstarted process as success")]
	public void EnsureTerminalProcessStarted_ShouldRejectUnstartedProcess()
	{
		// Arrange
		ProcessLaunchResult result = new() { Started = false, ErrorMessage = "terminal not found" };

		// Act
		Action act = () => CreatioHostService.EnsureTerminalProcessStarted(result, "terminal launcher");

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("Unable to start the terminal launcher: terminal not found.",
			because: "a terminal launch failure must not be reported as a successful Creatio start");
	}

	[Test]
	[Description("Accepts a terminal launcher result only after the operating-system process reports that it started")]
	public void EnsureTerminalProcessStarted_ShouldAcceptStartedProcess()
	{
		// Arrange
		ProcessLaunchResult result = new() { Started = true, ProcessId = 42 };

		// Act
		Action act = () => CreatioHostService.EnsureTerminalProcessStarted(result, "terminal launcher");

		// Assert
		act.Should().NotThrow(
			because: "a successfully started terminal must continue to the host startup flow");
	}
}
