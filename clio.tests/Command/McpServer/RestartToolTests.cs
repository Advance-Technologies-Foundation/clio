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
		RestartTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, new RestartOperationRegistry());

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
			resolvedCommand.CapturedReadinessOptions.Should().BeNull(
				because: "with waitReady=false the readiness wait must never run");
		} finally {
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("waitReady defaults to true and waitTimeoutSeconds defaults to 600 when the caller omits both; the wait-enabled options drive a SEPARATE readiness call.")]
	public async Task RestartInstanceByName_Should_Default_WaitReady_True_And_Timeout_600() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeRestartCommand defaultCommand = new();
		FakeRestartCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<RestartCommand>(Arg.Any<RestartOptions>()).Returns(resolvedCommand);
		RestartTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, new RestartOperationRegistry());

		try {
			// Act
			CommandExecutionResult result = await tool.RestartInstanceByName("sandbox");

			// Assert
			result.ExitCode.Should().Be(0,
				because: "the resolved fake command reports success and readiness within the response deadline");
			// The readiness wait resolves the command with the ORIGINAL wait-enabled options (WaitReady=true);
			// the Phase-1 request-only resolve carries WaitReady=false, so only ONE resolve matches this predicate.
			commandResolver.Received(1).Resolve<RestartCommand>(Arg.Is<RestartOptions>(options =>
				options.WaitReady == true &&
				options.ReadyTimeout == 600));
			resolvedCommand.CapturedReadinessOptions.Should().NotBeNull(
				because: "readiness must be polled as a separate step after the restart request");
		} finally {
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Finding 2: the restart request runs under the per-tenant lock but the readiness wait does NOT — Phase 1 resolves request-only (WaitReady=false), Phase 2 polls readiness separately (WaitReady=true).")]
	public async Task RestartInstanceByName_Should_Split_Request_UnderLock_From_LockFree_ReadinessWait() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeRestartCommand defaultCommand = new();
		FakeRestartCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<RestartCommand>(Arg.Any<RestartOptions>()).Returns(resolvedCommand);
		RestartTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, new RestartOperationRegistry());

		try {
			// Act
			CommandExecutionResult result = await tool.RestartInstanceByName("sandbox");

			// Assert
			result.ExitCode.Should().Be(0, because: "the request succeeded and the instance became ready");
			resolvedCommand.CapturedOptions!.WaitReady.Should().BeFalse(
				because: "the restart REQUEST executed under the lock must NOT carry the wait — the wait is kept out of the locked path");
			resolvedCommand.CapturedReadinessOptions!.WaitReady.Should().BeTrue(
				because: "the readiness wait is a separate, lock-free step driven by the original wait-enabled options");
			commandResolver.Received(2).Resolve<RestartCommand>(Arg.Any<RestartOptions>());
		} finally {
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Finding 2: while a restart's readiness wait is in flight, another same-tenant call is NOT serialized behind it — proving the per-tenant execution lock is released before the wait.")]
	public async Task ReadinessWait_Should_Not_Hold_TenantLock_Blocking_Concurrent_SameTenant_Calls() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		using ManualResetEventSlim readinessEntered = new(false);
		using ManualResetEventSlim releaseReadiness = new(false);
		FakeRestartCommand resolvedCommand = new() {
			ReadinessEntered = readinessEntered,
			ReadinessGate = releaseReadiness
		};
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<RestartCommand>(Arg.Any<RestartOptions>()).Returns(resolvedCommand);
		RestartTool tool = new(new FakeRestartCommand(), ConsoleLogger.Instance, commandResolver, new RestartOperationRegistry());

		try {
			// Call A: waits in the (lock-free) readiness poll until released.
			Task<CommandExecutionResult> callA = tool.RestartInstanceByName("sandbox");
			readinessEntered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue(
				because: "call A must reach the readiness wait so the concurrency window is open");

			// Call B: a same-tenant restart request while A's readiness wait is still blocked. If the wait held
			// the per-tenant lock, this would deadlock until the timeout; it must complete promptly instead.
			Task<CommandExecutionResult> callB = tool.RestartInstanceByName("sandbox", waitReady: false);
			bool bCompleted = callB.Wait(TimeSpan.FromSeconds(5));

			// Assert
			bCompleted.Should().BeTrue(
				because: "the readiness wait released the tenant lock, so a concurrent same-tenant call is not serialized behind the warm-up");
			(await callB).ExitCode.Should().Be(0, because: "the concurrent restart request itself succeeded");

			// Release A and let it finish cleanly.
			releaseReadiness.Set();
			(await callA).ExitCode.Should().Be(0, because: "call A's instance became ready once released");
		} finally {
			releaseReadiness.Set();
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
		RestartTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, new RestartOperationRegistry());

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
		RestartTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, new RestartOperationRegistry());

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
		RestartTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, new RestartOperationRegistry());

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
		RestartTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, new RestartOperationRegistry());

		try {
			// Act
			CommandExecutionResult result = await tool.RestartInstanceByCredentials(
				"http://localhost:5000", "Supervisor", "Supervisor");

			// Assert
			result.ExitCode.Should().Be(0, because: "the resolved fake command reports success");
			// Only the wait-enabled Phase-2 resolve matches (WaitReady=true); the Phase-1 request-only resolve
			// carries WaitReady=false.
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
	[Description("The in-progress notice names the target, the restart-status poll target with the operation-id, and tells the agent not to retry.")]
	public void BuildInProgressMessage_Should_Reference_Target_PollTarget_And_NoRetry() {
		// Act
		string message = RestartTool.BuildInProgressMessage(
			"environment 'sandbox'", RestartTool.RestartByEnvironmentNameToolName, 600, "op-1234");

		// Assert
		message.Should().Contain("sandbox", because: "the agent must know which target is still warming up");
		message.Should().Contain("restart-status", because: "the notice must point the agent at the restart-status MCP poll tool");
		message.Should().Contain("op-1234", because: "the notice must carry the operation-id the agent can poll by");
		message.Should().Contain("600", because: "the notice should reflect the actual readiness budget");
		message.Should().Contain("do NOT retry", because: "retrying restart while the instance is warming up is unnecessary and disruptive");
	}

	[Test]
	[Category("Unit")]
	[Description("When the readiness wait exceeds the MCP response deadline, restart-by-environment-name returns exit-code 0 with an in-progress notice pointing at the restart-status poll tool instead of blocking or hard-failing the client.")]
	public async Task RestartInstanceByName_Should_Return_InProgressNotice_When_ResponseDeadlineExceeded() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeRestartCommand defaultCommand = new();
		// The resolved readiness wait blocks on the gate until the test releases it, so Task.Delay(deadline)
		// deterministically wins the WhenAny race (no timing dependence). The gate is released in finally so
		// the detached work completes promptly.
		ManualResetEventSlim readinessGate = new(false);
		FakeRestartCommand resolvedCommand = new() { ReadinessGate = readinessGate };
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<RestartCommand>(Arg.Any<RestartOptions>()).Returns(resolvedCommand);
		RestartTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver, new RestartOperationRegistry()) {
			ResponseDeadlineOverride = TimeSpan.FromMilliseconds(50)
		};

		try {
			// Act
			CommandExecutionResult result = await tool.RestartInstanceByName("sandbox");

			// Assert
			result.ExitCode.Should().Be(0,
				because: "an over-deadline readiness wait returns a non-error in-progress envelope so a hard-ceiling client does not fail the call");
			result.Output.Should().Contain(
				message => message.Value != null && message.Value.ToString()!.Contains("restart-status"),
				because: "the in-progress notice must point the agent at the restart-status poll tool while the restart warms up server-side");
		} finally {
			readinessGate.Set(); // release the detached readiness wait so it completes
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	private sealed class FakeRestartCommand : RestartCommand {
		public RestartOptions? CapturedOptions { get; private set; }
		public RestartOptions? CapturedReadinessOptions { get; private set; }

		public int ExitCodeToReturn { get; init; } = 0;
		public bool ReadyToReturn { get; init; } = true;

		/// <summary>Signaled the moment <see cref="WaitForReadiness"/> is entered, so a test can open a concurrency window.</summary>
		public ManualResetEventSlim? ReadinessEntered { get; init; }

		/// <summary>When set, <see cref="WaitForReadiness"/> blocks on this gate so a test can force the response-deadline
		/// branch or hold the lock-free wait open while it probes a concurrent call.</summary>
		public ManualResetEventSlim? ReadinessGate { get; init; }

		public FakeRestartCommand()
			: base(
				Substitute.For<IApplicationClient>(),
				new EnvironmentSettings(),
				Substitute.For<IServerReadinessWaiter>()) {
		}

		public override int Execute(RestartOptions options) {
			CapturedOptions = options;
			return ExitCodeToReturn;
		}

		public override bool WaitForReadiness(RestartOptions options) {
			CapturedReadinessOptions = options;
			ReadinessEntered?.Set();
			ReadinessGate?.Wait();
			return ReadyToReturn;
		}
	}
}
