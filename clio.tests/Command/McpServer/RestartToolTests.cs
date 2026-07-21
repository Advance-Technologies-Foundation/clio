using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class RestartToolTests {

	[Test]
	[Category("Unit")]
	[Description("With waitReady=false, restart-by-environment-name resolves the command synchronously and never touches the readiness wait.")]
	public async Task RestartInstanceByName_Should_Resolve_Command_Synchronously_WhenWaitReadyFalse() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeRestartCommand defaultCommand = new();
		FakeRestartCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<RestartCommand>(Arg.Any<RestartOptions>()).Returns(resolvedCommand);
		RestartTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		try {
			// Act
			CommandExecutionResult result = await tool.RestartInstanceByName("sandbox", waitReady: false);

			// Assert
			result.ExitCode.Should().Be(0, because: "the resolved fake command reports success");
			commandResolver.Received(1).Resolve<RestartCommand>(Arg.Is<RestartOptions>(options =>
				options.Environment == "sandbox" &&
				options.WaitReady == false &&
				options.TimeOut == 30_000));
			resolvedCommand.CapturedOptions.Should().NotBeNull(
				because: "the resolved command should receive the forwarded options");
		} finally {
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("waitReady defaults to true and waitTimeoutSeconds defaults to 600 when the caller omits both.")]
	public async Task RestartInstanceByName_Should_Default_WaitReady_True_And_Timeout_600() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeRestartCommand defaultCommand = new();
		FakeRestartCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<RestartCommand>(Arg.Any<RestartOptions>()).Returns(resolvedCommand);
		RestartTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		try {
			// Act
			CommandExecutionResult result = await tool.RestartInstanceByName("sandbox");

			// Assert
			result.ExitCode.Should().Be(0,
				because: "the resolved fake command reports success and completes well within the response deadline");
			commandResolver.Received(1).Resolve<RestartCommand>(Arg.Is<RestartOptions>(options =>
				options.WaitReady == true &&
				options.ReadyTimeout == 600));
		} finally {
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("An empty environment-name is rejected before any command resolution is attempted.")]
	public async Task RestartInstanceByName_Should_ReturnValidationError_WhenEnvironmentNameEmpty() {
		// Arrange
		FakeRestartCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		RestartTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = await tool.RestartInstanceByName("  ");

		// Assert
		result.ExitCode.Should().Be(1, because: "environment-name is required and cannot be empty");
		commandResolver.DidNotReceive().Resolve<RestartCommand>(Arg.Any<RestartOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("When command resolution throws (e.g. an unregistered environment), the tool returns a caller-actionable exit code instead of propagating the exception or hanging on the readiness wait.")]
	public async Task RestartInstanceByName_Should_ReturnFailure_WhenResolutionThrows() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeRestartCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<RestartCommand>(Arg.Any<RestartOptions>())
			.Returns(_ => throw new EnvironmentResolutionException("Environment 'sandbox' not found."));
		RestartTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		try {
			// Act
			CommandExecutionResult result = await tool.RestartInstanceByName("sandbox");

			// Assert
			result.ExitCode.Should().Be(1,
				because: "an environment-resolution failure is an expected, caller-actionable error, not an unhandled exception");
		} finally {
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Credential validation rejects an empty password before any command resolution is attempted.")]
	public async Task RestartInstanceByCredentials_Should_ReturnValidationError_WhenPasswordEmpty() {
		// Arrange
		FakeRestartCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		RestartTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = await tool.RestartInstanceByCredentials("http://localhost:5000", "Supervisor", "");

		// Assert
		result.ExitCode.Should().Be(1, because: "password is required and cannot be empty");
		commandResolver.DidNotReceive().Resolve<RestartCommand>(Arg.Any<RestartOptions>());
	}

	[Test]
	[Category("Unit")]
	[Description("restart-by-credentials forwards waitReady/waitTimeoutSeconds and the isNetCore default alongside the credentials payload.")]
	public async Task RestartInstanceByCredentials_Should_Forward_Credentials_And_Defaults() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeRestartCommand defaultCommand = new();
		FakeRestartCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<RestartCommand>(Arg.Any<RestartOptions>()).Returns(resolvedCommand);
		RestartTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		try {
			// Act
			CommandExecutionResult result = await tool.RestartInstanceByCredentials(
				"http://localhost:5000", "Supervisor", "Supervisor");

			// Assert
			result.ExitCode.Should().Be(0, because: "the resolved fake command reports success");
			commandResolver.Received(1).Resolve<RestartCommand>(Arg.Is<RestartOptions>(options =>
				options.Uri == "http://localhost:5000" &&
				options.Login == "Supervisor" &&
				options.Password == "Supervisor" &&
				options.IsNetCore == false &&
				options.WaitReady == true &&
				options.ReadyTimeout == 600));
		} finally {
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Both restart tools advertise stable, destructive MCP metadata for host confirmation prompts.")]
	public void RestartTools_Should_Expose_Expected_Mcp_Metadata() {
		// Arrange

		// Act
		McpServerToolAttribute byNameAttribute = (McpServerToolAttribute)typeof(RestartTool)
			.GetMethod(nameof(RestartTool.RestartInstanceByName))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();
		McpServerToolAttribute byCredentialsAttribute = (McpServerToolAttribute)typeof(RestartTool)
			.GetMethod(nameof(RestartTool.RestartInstanceByCredentials))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		byNameAttribute.Name.Should().Be(RestartTool.RestartByEnvironmentNameToolName,
			because: "the metadata should reuse the production tool-name constant");
		byNameAttribute.Destructive.Should().BeTrue(because: "restarting interrupts the active session");
		byCredentialsAttribute.Name.Should().Be(RestartTool.RestartByCredentialsToolName,
			because: "the metadata should reuse the production tool-name constant");
		byCredentialsAttribute.Destructive.Should().BeTrue(because: "restarting interrupts the active session");
	}

	[Test]
	[Category("Unit")]
	[Description("The in-progress notice names the target, the healthcheck poll target, and tells the agent not to retry.")]
	public void BuildInProgressMessage_Should_Reference_Target_PollTarget_And_NoRetry() {
		// Arrange

		// Act
		string message = RestartTool.BuildInProgressMessage("environment 'sandbox'", RestartTool.RestartByEnvironmentNameToolName, 600);

		// Assert
		message.Should().Contain("sandbox", because: "the agent must know which target is still warming up");
		message.Should().Contain("healthcheck", because: "the notice must point the agent at the healthcheck poll target");
		message.Should().Contain("600", because: "the notice should reflect the actual readiness budget");
		message.Should().Contain("do NOT retry", because: "retrying restart while the instance is warming up is unnecessary and disruptive");
	}

	[Test]
	[Category("Unit")]
	[Description("When the readiness wait exceeds the MCP response deadline, restart-by-environment-name returns exit-code 0 with an in-progress notice pointing at the healthcheck poll target — the AC-#3 deadline -> FromInfo branch — instead of blocking or hard-failing the client.")]
	public async Task RestartInstanceByName_Should_Return_InProgressNotice_When_ResponseDeadlineExceeded() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeRestartCommand defaultCommand = new();
		// The resolved restart blocks on the gate until the test releases it, so Task.Delay(deadline)
		// deterministically wins the WhenAny race (no timing dependence). The gate is released in finally so
		// the detached work completes promptly and does not hold the tenant lock past the test.
		ManualResetEventSlim executeGate = new(false);
		FakeRestartCommand resolvedCommand = new() { ExecuteGate = executeGate };
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<RestartCommand>(Arg.Any<RestartOptions>()).Returns(resolvedCommand);
		RestartTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver) {
			ResponseDeadlineOverride = TimeSpan.FromMilliseconds(50)
		};

		try {
			// Act
			CommandExecutionResult result = await tool.RestartInstanceByName("sandbox");

			// Assert
			result.ExitCode.Should().Be(0,
				because: "an over-deadline readiness wait returns a non-error in-progress envelope so a hard-ceiling client does not fail the call");
			result.Output.Should().Contain(
				message => message.Value != null && message.Value.ToString()!.Contains("healthcheck"),
				because: "the in-progress notice must point the agent at the healthcheck poll target while the restart warms up server-side");
		} finally {
			executeGate.Set(); // release the detached work so it completes and frees the tenant lock
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	private sealed class FakeRestartCommand : RestartCommand {
		public RestartOptions? CapturedOptions { get; private set; }

		public int ExitCodeToReturn { get; init; } = 0;

		/// <summary>When set, <see cref="Execute"/> blocks on this gate so a test can force the response-deadline
		/// branch deterministically, then release the detached work in its finally.</summary>
		public ManualResetEventSlim? ExecuteGate { get; init; }

		public FakeRestartCommand()
			: base(
				Substitute.For<IApplicationClient>(),
				new EnvironmentSettings(),
				Substitute.For<IServerReadinessWaiter>()) {
		}

		public override int Execute(RestartOptions options) {
			CapturedOptions = options;
			ExecuteGate?.Wait();
			return ExitCodeToReturn;
		}
	}
}
