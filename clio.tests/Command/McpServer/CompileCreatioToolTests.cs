using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class CompileCreatioToolTests
{
	[TearDown]
	public void TearDown()
	{
		// A deadline-branch test detaches its compile; its compile reservation is released on that detached
		// continuation, which can outlive the test method. Clear the process-global reservations so a leaked
		// one cannot fast-fail the next test's compile for the shared "sandbox-tenant" key.
		McpToolExecutionLock.ResetCompileReservationsForTests();
	}

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable MCP tool name for Creatio compilation.")]
	public void CompileCreatio_Should_Advertise_Stable_Tool_Name()
	{
		// Arrange

		// Act
		string toolName = CompileCreatioTool.CompileCreatioToolName;

		// Assert
		toolName.Should().Be("compile-creatio",
			because: "clients and tests should share one stable tool name for full and package-only compilation");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves CompileConfigurationCommand with All=true when package-name is omitted.")]
	public async Task CompileCreatio_Should_Use_Full_Compilation_When_Package_Name_Is_Omitted()
	{
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns("sandbox-tenant");
		FakeCompileConfigurationCommand resolvedCommand = new();
		commandResolver.Resolve<CompileConfigurationCommand>(Arg.Any<CompileConfigurationOptions>())
			.Returns(resolvedCommand);
		ICompileOperationRegistry registry = new CompileOperationRegistry();
		CompileCreatioTool tool = new(ConsoleLogger.Instance, commandResolver, registry);

		try
		{
			// Act
			CommandExecutionResult result = await tool.CompileCreatio(new CompileCreatioArgs("sandbox", null));

			// Assert
			result.ExitCode.Should().Be(0,
				because: "omitting package-name should invoke the full compilation path");
			commandResolver.Received(1).Resolve<CompileConfigurationCommand>(Arg.Is<CompileConfigurationOptions>(options =>
				options.Environment == "sandbox" &&
				options.All));
			commandResolver.DidNotReceive().Resolve<CompilePackageCommand>(Arg.Any<CompilePackageOptions>());
			resolvedCommand.CapturedOptions.Should().NotBeNull(
				because: "the resolved full compile command should receive the forwarded options");
			resolvedCommand.CapturedOptions!.All.Should().BeTrue(
				because: "the MCP tool should set All=true for the full compilation path");
			CompileOperationRecord tracked = registry.GetLatest("sandbox-tenant");
			tracked.Should().NotBeNull(because: "compile-creatio must record the operation so compile-status can find it");
			tracked!.Status.Should().Be(CompileOperationStatus.Succeeded,
				because: "a zero exit code finalizes the tracked operation as succeeded");
			tracked.PackageName.Should().BeNull(because: "a full compilation tracks no single package name");
		}
		finally
		{
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves CompilePackageCommand with the exact package-name when package-only compilation is requested.")]
	public async Task CompileCreatio_Should_Use_Package_Compilation_When_Package_Name_Is_Provided()
	{
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns("sandbox-tenant");
		FakeCompilePackageCommand resolvedCommand = new();
		commandResolver.Resolve<CompilePackageCommand>(Arg.Any<CompilePackageOptions>())
			.Returns(resolvedCommand);
		ICompileOperationRegistry registry = new CompileOperationRegistry();
		CompileCreatioTool tool = new(ConsoleLogger.Instance, commandResolver, registry);

		try
		{
			// Act
			CommandExecutionResult result = await tool.CompileCreatio(new CompileCreatioArgs("sandbox", "MyPackage"));

			// Assert
			result.ExitCode.Should().Be(0,
				because: "providing package-name should invoke the package-only compilation path");
			commandResolver.Received(1).Resolve<CompilePackageCommand>(Arg.Is<CompilePackageOptions>(options =>
				options.Environment == "sandbox" &&
				options.PackageName == "MyPackage"));
			commandResolver.DidNotReceive().Resolve<CompileConfigurationCommand>(Arg.Any<CompileConfigurationOptions>());
			resolvedCommand.CapturedOptions.Should().NotBeNull(
				because: "the resolved package compile command should receive the forwarded options");
			resolvedCommand.CapturedOptions!.PackageName.Should().Be("MyPackage",
				because: "the MCP tool should preserve the exact requested package name");
			registry.GetLatest("sandbox-tenant")!.PackageName.Should().Be("MyPackage",
				because: "the tracked operation should record which package was compiled");
		}
		finally
		{
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Records a failed tracked operation when the resolved compile command reports a non-zero exit code.")]
	public async Task CompileCreatio_Should_Record_Failed_Operation_When_Compile_Exits_NonZero()
	{
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns("sandbox-tenant");
		FakeCompileConfigurationCommand resolvedCommand = new() { ExitCodeToReturn = 1 };
		commandResolver.Resolve<CompileConfigurationCommand>(Arg.Any<CompileConfigurationOptions>())
			.Returns(resolvedCommand);
		ICompileOperationRegistry registry = new CompileOperationRegistry();
		CompileCreatioTool tool = new(ConsoleLogger.Instance, commandResolver, registry);

		try
		{
			// Act
			CommandExecutionResult result = await tool.CompileCreatio(new CompileCreatioArgs("sandbox", null));

			// Assert
			result.ExitCode.Should().Be(1, because: "the tool must surface the command's real exit code");
			CompileOperationRecord tracked = registry.GetLatest("sandbox-tenant");
			tracked.Should().NotBeNull(because: "compile-creatio must record the operation even when it fails");
			tracked!.Status.Should().Be(CompileOperationStatus.Failed,
				because: "a non-zero exit code finalizes the tracked operation as failed");
			tracked.ExitCode.Should().Be(1, because: "the tracked operation must preserve the command's real exit code");
			tracked.FinishedUtc.Should().NotBeNull(because: "a finished operation must carry a finish timestamp");
		}
		finally
		{
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Records a failed tracked operation and returns a caller-actionable exit code when command resolution itself throws (e.g. an unregistered environment) — resolution runs outside the resolved command's own try/catch, so this must be handled explicitly.")]
	public async Task CompileCreatio_Should_Record_Failed_Operation_When_Resolution_Throws()
	{
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns("sandbox-tenant");
		commandResolver.Resolve<CompileConfigurationCommand>(Arg.Any<CompileConfigurationOptions>())
			.Returns(_ => throw new EnvironmentResolutionException("Environment 'sandbox' not found."));
		ICompileOperationRegistry registry = new CompileOperationRegistry();
		CompileCreatioTool tool = new(ConsoleLogger.Instance, commandResolver, registry);

		try
		{
			// Act
			CommandExecutionResult result = await tool.CompileCreatio(new CompileCreatioArgs("sandbox", null));

			// Assert
			result.ExitCode.Should().Be(1,
				because: "an environment-resolution failure is an expected, caller-actionable error, not an unhandled exception");
			CompileOperationRecord tracked = registry.GetLatest("sandbox-tenant");
			tracked.Should().NotBeNull(
				because: "even a resolution failure must finalize the tracked operation, or compile-status would report it as running forever");
			tracked!.Status.Should().Be(CompileOperationStatus.Failed,
				because: "an environment-resolution failure finalizes the tracked operation as failed");
			tracked.FinishedUtc.Should().NotBeNull(
				because: "even a resolution failure must stamp a finish time so compile-status stops reporting it as running");
		}
		finally
		{
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects comma-separated package lists so the MCP contract remains limited to one package.")]
	public async Task CompileCreatio_Should_Reject_Comma_Separated_Package_Names()
	{
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CompileCreatioTool tool = new(ConsoleLogger.Instance, commandResolver, new CompileOperationRegistry());

		try
		{
			// Act
			CommandExecutionResult result = await tool.CompileCreatio(new CompileCreatioArgs("sandbox", "PkgA,PkgB"));

			// Assert
			result.ExitCode.Should().Be(1,
				because: "the MCP contract allows only one package name");
			result.Output.Should().Contain(message =>
				message.GetType() == typeof(ErrorMessage) &&
				Equals(message.Value, "`package-name` must contain exactly one package name. Comma-separated package lists are not supported by `compile-creatio`."),
				because: "the failure should explain why comma-separated package names are rejected");
			commandResolver.DidNotReceive().Resolve<CompilePackageCommand>(Arg.Any<CompilePackageOptions>());
			commandResolver.DidNotReceive().Resolve<CompileConfigurationCommand>(Arg.Any<CompileConfigurationOptions>());
		}
		finally
		{
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("The in-progress notice names the environment, the operation-id, and the compile-status poll target so an agent can act on it without retrying.")]
	public void BuildInProgressMessage_Should_Reference_Environment_OperationId_And_PollTarget()
	{
		// Arrange

		// Act
		string message = CompileCreatioTool.BuildInProgressMessage("sandbox", "op-123");

		// Assert
		message.Should().Contain("sandbox", because: "the agent must know which environment is still compiling");
		message.Should().Contain("op-123", because: "the agent needs the operation-id to poll the right operation");
		message.Should().Contain(CompileStatusTool.CompileStatusToolName,
			because: "the notice must point the agent at compile-status rather than retrying compile-creatio");
		message.Should().Contain("do NOT retry",
			because: "retrying compile-creatio while the tracked operation is still running would start a duplicate compile");
	}

	[Test]
	[Category("Unit")]
	[Description("When compilation exceeds the MCP response deadline, the tool returns exit-code 0 with an in-progress notice pointing at compile-status — the AC-#3 deadline -> FromInfo branch — instead of blocking or hard-failing the client.")]
	public async Task CompileCreatio_Should_Return_InProgressNotice_When_ResponseDeadlineExceeded()
	{
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns("sandbox-tenant");
		// The resolved compile blocks on the gate until the test releases it, so Task.Delay(deadline)
		// deterministically wins the WhenAny race (no timing dependence). The gate is released in finally so
		// the detached work completes promptly and does not hold the tenant lock past the test.
		ManualResetEventSlim executeGate = new(false);
		FakeCompileConfigurationCommand resolvedCommand = new() { ExecuteGate = executeGate };
		commandResolver.Resolve<CompileConfigurationCommand>(Arg.Any<CompileConfigurationOptions>())
			.Returns(resolvedCommand);
		ICompileOperationRegistry registry = new CompileOperationRegistry();
		CompileCreatioTool tool = new(ConsoleLogger.Instance, commandResolver, registry)
		{
			ResponseDeadlineOverride = TimeSpan.FromMilliseconds(50)
		};

		try
		{
			// Act
			CommandExecutionResult result = await tool.CompileCreatio(new CompileCreatioArgs("sandbox", null));

			// Assert
			result.ExitCode.Should().Be(0,
				because: "an over-deadline compile returns a non-error in-progress envelope so a hard-ceiling client does not fail the call");
			result.Output.Should().Contain(
				message => message.Value != null && message.Value.ToString()!.Contains(CompileStatusTool.CompileStatusToolName),
				because: "the in-progress notice must point the agent at compile-status to poll the compile that is still running server-side");
			CompileOperationRecord tracked = registry.GetLatest("sandbox-tenant");
			tracked.Should().NotBeNull(
				because: "the operation must be tracked before the deadline fires so compile-status can report it");
			tracked!.Status.Should().Be(CompileOperationStatus.Running,
				because: "the detached compile has not finished when the response deadline returns the in-progress notice");
		}
		finally
		{
			executeGate.Set(); // release the detached work so it finalizes and frees the tenant lock
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Exposes destructive MCP metadata for compilation so hosts can prompt for confirmation before the long-running runtime reload.")]
	public void CompileCreatio_Should_Expose_Expected_Mcp_Metadata()
	{
		// Arrange
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(CompileCreatioTool)
			.GetMethod(nameof(CompileCreatioTool.CompileCreatio))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Act

		// Assert
		attribute.Name.Should().Be(CompileCreatioTool.CompileCreatioToolName,
			because: "the metadata should reuse the production tool-name constant");
		attribute.ReadOnly.Should().BeFalse(
			because: "compilation changes target environment build state");
		attribute.Destructive.Should().BeTrue(
			because: "compilation forces a runtime reload that interrupts the active session, so hosts must treat it as destructive");
	}

	[Test]
	[Category("Unit")]
	[Description("Prompt guidance for compile-creatio references the exact tool name for both full and package-only compilation.")]
	public void CompileCreatioPrompt_Should_Reference_Exact_Tool_Name()
	{
		// Arrange

		// Act
		string fullPrompt = FsmAndCompilePrompt.CompileCreatio("sandbox");
		string packagePrompt = FsmAndCompilePrompt.CompileCreatio("sandbox", "MyPackage");

		// Assert
		fullPrompt.Should().Contain(CompileCreatioTool.CompileCreatioToolName,
			because: "the prompt should reference the exact MCP tool name for full compilation");
		fullPrompt.Should().Contain("--all",
			because: "the full compilation prompt should explain the corresponding CLI behavior");
		packagePrompt.Should().Contain("MyPackage",
			because: "the package compilation prompt should preserve the requested package name");
	}

	[Test]
	[Category("Unit")]
	[Description("A same-tenant compile requested while one is already in flight fails fast with a poll-and-wait notice — mirroring the Creatio core's reject-on-concurrent-compile — instead of resolving/starting a second compile or tracking a duplicate operation.")]
	public async Task CompileCreatio_Should_FailFast_When_Compile_Already_In_Flight_For_Same_Tenant()
	{
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns("sandbox-tenant");
		ICompileOperationRegistry registry = new CompileOperationRegistry();
		CompileCreatioTool tool = new(ConsoleLogger.Instance, commandResolver, registry);

		// Simulate a compile already running for this tenant by holding its reservation.
		McpToolExecutionLock.TryReserveCompile("sandbox-tenant").Should().BeTrue(
			because: "the first reservation for a tenant must succeed");

		try
		{
			// Act
			CommandExecutionResult result = await tool.CompileCreatio(new CompileCreatioArgs("sandbox", null));

			// Assert
			result.ExitCode.Should().Be(1,
				because: "a second concurrent same-tenant compile must not start — the core would reject it");
			result.Output.Should().Contain(
				message => message.Value != null && message.Value.ToString()!.Contains(CompileStatusTool.CompileStatusToolName),
				because: "the fast-fail must point the caller at compile-status to poll the running compile");
			commandResolver.DidNotReceive().Resolve<CompileConfigurationCommand>(Arg.Any<CompileConfigurationOptions>());
			registry.GetLatest("sandbox-tenant").Should().BeNull(
				because: "a rejected duplicate compile must not create a tracked operation record");
		}
		finally
		{
			McpToolExecutionLock.ReleaseCompile("sandbox-tenant");
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("After a compile reservation is released, a fresh same-tenant compile proceeds normally — the reservation does not leak.")]
	public async Task CompileCreatio_Should_Proceed_After_Reservation_Released()
	{
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns("sandbox-tenant");
		FakeCompileConfigurationCommand resolvedCommand = new();
		commandResolver.Resolve<CompileConfigurationCommand>(Arg.Any<CompileConfigurationOptions>())
			.Returns(resolvedCommand);
		ICompileOperationRegistry registry = new CompileOperationRegistry();
		CompileCreatioTool tool = new(ConsoleLogger.Instance, commandResolver, registry);

		McpToolExecutionLock.TryReserveCompile("sandbox-tenant").Should().BeTrue();
		McpToolExecutionLock.ReleaseCompile("sandbox-tenant");

		try
		{
			// Act
			CommandExecutionResult result = await tool.CompileCreatio(new CompileCreatioArgs("sandbox", null));

			// Assert
			result.ExitCode.Should().Be(0,
				because: "once the prior reservation is released the tenant is free to compile again");
			commandResolver.Received(1).Resolve<CompileConfigurationCommand>(Arg.Any<CompileConfigurationOptions>());
		}
		finally
		{
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("The already-in-progress notice states the compile was NOT started, points at compile-status, and reflects the core's reject (not queue) semantics.")]
	public void CompileAlreadyInProgressMessage_Should_Explain_Reject_And_PollTarget()
	{
		// Act
		string message = CompileCreatioTool.CompileAlreadyInProgressMessage("sandbox");

		// Assert
		message.Should().Contain("sandbox", because: "the caller must know which environment is already compiling");
		message.Should().Contain("already in progress",
			because: "the caller must understand a compile is already running");
		message.Should().Contain("not started",
			because: "the caller must know this duplicate request did not launch a compile");
		message.Should().Contain(CompileStatusTool.CompileStatusToolName,
			because: "the notice must point the caller at compile-status rather than retrying");
	}

	[Test]
	[Category("Unit")]
	[Description("The in-progress (deadline) notice reflects the core reject-not-queue semantics and warns against other environment-bound calls during compilation.")]
	public void BuildInProgressMessage_Should_Reflect_Reject_Semantics_And_Warn_Against_Concurrent_Ops()
	{
		// Act
		string message = CompileCreatioTool.BuildInProgressMessage("sandbox", "op-123");

		// Assert
		message.Should().Contain("rejected, not queued",
			because: "the core serializes compilation and rejects a concurrent compile — the notice must not promise queueing");
		message.Should().Contain("restart-by-environment-name",
			because: "the notice must warn against restarting the environment mid-compile");
	}

	private sealed class FakeCompileConfigurationCommand : CompileConfigurationCommand
	{
		public CompileConfigurationOptions? CapturedOptions { get; private set; }

		public int ExitCodeToReturn { get; init; } = 0;

		/// <summary>When set, <see cref="Execute"/> blocks on this gate so a test can force the response-deadline
		/// branch deterministically, then release the detached work in its finally.</summary>
		public ManualResetEventSlim? ExecuteGate { get; init; }

		public FakeCompileConfigurationCommand()
			: base(
				Substitute.For<IApplicationClient>(),
				new EnvironmentSettings(),
				Substitute.For<IServiceUrlBuilder>(),
				Substitute.For<ICompilationHistoryPoller>(),
				Substitute.For<ILogger>())
		{
		}

		public override int Execute(CompileConfigurationOptions options)
		{
			CapturedOptions = options;
			ExecuteGate?.Wait();
			return ExitCodeToReturn;
		}
	}

	private sealed class FakeCompilePackageCommand : CompilePackageCommand
	{
		public CompilePackageOptions? CapturedOptions { get; private set; }

		public FakeCompilePackageCommand()
			: base(Substitute.For<Clio.Package.IPackageBuilder>(), Substitute.For<ILogger>())
		{
		}

		public override int Execute(CompilePackageOptions options)
		{
			CapturedOptions = options;
			return 0;
		}
	}
}
