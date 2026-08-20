using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;
using IFileSystem = Clio.Common.IFileSystem;

namespace Clio.Tests.Command.McpServer;

// NonParallelizable: this fixture and CompileCreatioToolTests both call
// McpToolExecutionLock.ResetConfigurationBuildReservationsForTests(), which does an UNKEYED Clear() on a
// process-global dictionary, and both hold a reservation across an await in the middle of a test. Under
// [assembly: Parallelizable(ParallelScope.Fixtures)] one fixture's TearDown can therefore clear the
// reservation the other is asserting on, with no bug present. The keys differ today (sandbox-tenant vs
// busy-tenant) so it cannot flake yet — it is one shared key away. It would flake FIRST under the
// mandated pre-commit filter, where the smaller fixture pool makes the two likelier to be co-scheduled,
// and a flaky red on the gate is how a gate stops being trusted.
[NonParallelizable]
[TestFixture]
[Property("Module", "McpServer")]
public sealed class InstallProcessBuilderToolTests {

	/// <summary>
	/// Time units a duration claim can be spelled with, as a regex alternation.
	/// </summary>
	/// <remarks>
	/// Bare <c>m</c> and <c>h</c> are deliberately absent: they collide with ordinary prose far more often
	/// than anyone writes "2 h", and <c>min</c>/<c>hour</c> cover the real spellings. The trailing
	/// <c>\b</c> at each use site is what keeps <c>s</c> from matching "30 schemas".
	/// </remarks>
	internal const string DurationUnits =
		"s|sec|secs|second|seconds|min|mins|minute|minutes|hr|hrs|hour|hours";

	/// <summary>
	/// A duration RANGE, e.g. <c>15-75 s</c>.
	/// </summary>
	internal const string DurationRangePattern =
		@"(?i)\d+\s*(-|–|to)\s*\d+\s*(" + DurationUnits + @")\b";

	/// <summary>
	/// A single duration figure, e.g. <c>~30 s</c>, <c>45 sec</c>, <c>2 min</c>.
	/// </summary>
	internal const string DurationFigurePattern = @"(?i)\d+\s*(" + DurationUnits + @")\b";

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable install-process-builder MCP tool name so clients, guidance and the curated contract share one identifier.")]
	public void InstallProcessBuilder_Should_Advertise_Stable_Tool_Name() {
		// Act
		string toolName = InstallProcessBuilderTool.InstallProcessBuilderToolName;

		// Assert
		toolName.Should().Be("install-process-builder",
			because: "the process-designer tools' refusal hint names this exact verb, so renaming it would "
				+ "point users and agents at a tool that does not exist");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves InstallProcessBuilderCommand for the requested environment and returns the real command exit code.")]
	public async Task InstallProcessBuilder_Should_Resolve_Command_For_Environment_And_Return_Exit_Code() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		FakeInstallProcessBuilderCommand resolvedCommand = new(exitCode: 0);
		commandResolver.Resolve<InstallProcessBuilderCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(resolvedCommand);
		InstallProcessBuilderTool tool = new(ConsoleLogger.Instance, commandResolver);

		try {
			// Act
			CommandExecutionResult result =
				await tool.InstallProcessBuilder(new InstallProcessBuilderArgs("sandbox"));

			// Assert
			result.ExitCode.Should().Be(0,
				because: "the MCP tool should return the real command exit code, including the failure the "
					+ "command raises when the service does not answer after installing");
			commandResolver.Received(1).Resolve<InstallProcessBuilderCommand>(Arg.Is<EnvironmentOptions>(
				options => options.Environment == "sandbox"));
			resolvedCommand.CapturedOptions.Should().NotBeNull(
				because: "the resolved command should receive the forwarded options");
			resolvedCommand.CapturedOptions!.Environment.Should().Be("sandbox",
				because: "the environment-name argument should map into InstallProcessBuilderOptions");
			resolvedCommand.CapturedOptions.Uri.Should().BeNull(
				because: "environment-name is the tool's only argument, so the mapping must leave every other "
					+ "environment-identity field unset and let the registered environment supply it — an "
					+ "MCP-supplied URI would silently retarget the install");
			resolvedCommand.CapturedOptions.Force.Should().BeFalse(
				because: "--force overrides the downgrade refusal, and rolling a shared environment back is a "
					+ "human decision. The mapping must leave it at its default; an agent-reachable override "
					+ "would let a business-task agent roll the package back for everyone on that environment");
		} finally {
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Exposes destructive, idempotent MCP metadata and a remediation-oriented description naming the package and the tools it unblocks.")]
	public void InstallProcessBuilder_Should_Expose_Expected_Mcp_Metadata() {
		// Arrange
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(InstallProcessBuilderTool)
			.GetMethod(nameof(InstallProcessBuilderTool.InstallProcessBuilder))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();
		System.ComponentModel.DescriptionAttribute description =
			(System.ComponentModel.DescriptionAttribute)typeof(InstallProcessBuilderTool)
				.GetMethod(nameof(InstallProcessBuilderTool.InstallProcessBuilder))!
				.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
				.Single();

		// Assert
		attribute.Name.Should().Be(InstallProcessBuilderTool.InstallProcessBuilderToolName,
			because: "the metadata should reuse the production tool-name constant");
		attribute.ReadOnly.Should().BeFalse(
			because: "installing the package changes the target environment's package state");
		attribute.Destructive.Should().BeTrue(
			because: "the flag is what clio's core-rules guidance ties 'confirm the target environment with "
				+ "the user first' to, and this tool runs a configuration build on a live instance and "
				+ "restarts it, with recovery from a failed compile being an explicit RestoreFromBackup "
				+ "rather than a rollback. It is additive in what it ADDS, which argued for false at first, "
				+ "but compile-creatio and restart-by-environment-name are both true and this tool causes "
				+ "the effects of both. install-gate's false is not the precedent: it ships a prebuilt "
				+ "assembly and never makes the target rebuild");
		attribute.Idempotent.Should().BeTrue(
			because: "a SEQUENTIAL re-run converges on the same end state - it either installs again, costing "
				+ "one configuration build and changing nothing else, or hits the same refusal it hit before, "
				+ "which changes nothing at all. Both refusals are stable under repetition, which is what the "
				+ "hint promises. It says nothing about CONCURRENT re-entry, refused outright by the "
				+ "configuration-build reservation");
		description.Description.Should().Contain(BundledPackages.ProcessBuilderPackageName,
			because: "the description should name the package the tool installs");
		description.Description.Should().Contain("create-business-process",
			because: "the description should name a process-designer tool whose refusal motivates this one, "
				+ "so an agent can connect the refusal to the remedy");
		description.Description.Should().Contain("Ping",
			because: "the description must disclose HOW the outcome is checked, not just that it is: the tool "
				+ "asks the package's own service whether it is serving, which is why a successful install call "
				+ "can still fail — and naming the operation lets a caller reproduce the check by hand");
		description.Description.Should().NotMatchRegex(DurationRangePattern,
			because: "a duration range must never appear here. It was measured on two stands and does NOT "
				+ "generalise — elapsed time is a property of the TARGET (configuration size, host, load), not "
				+ "of clio. And on this surface a range does not stay an estimate: an agent read '~15-75 s' out "
				+ "of this description and repeated it to a user as a promise. Say the call is slow and that the "
				+ "duration depends on the environment; never quote one");
		description.Description.Should().NotMatchRegex(DurationFigurePattern,
			because: "a single figure is the same promise as a range, only harder to spot");
		description.Description.Should().Contain("liveness, not identity",
			because: "the check's LIMIT is part of its contract: on an upgrade a stale assembly that still "
				+ "answers passes, so an agent must not read a successful install of a new version as proof the "
				+ "new code is running");
		description.Description.Should().Contain("list-packages",
			because: "an agent must be told to act on the refusal rather than compare versions itself, AND that "
				+ "the recorded version list-packages reports is not the same question: it moves when the "
				+ "archive is accepted, whether or not the target compiled it");
	}

	[Test]
	[Category("Unit")]
	[Description("The OPTIONS class must not be feature-gated either: MCP registration keys off the tool type, but the CLI verb keys off the options type, so gating that alone would silently remove the verb while every refusal still named it.")]
	public void InstallProcessBuilderOptions_Should_Not_Be_FeatureGated() {
		// Arrange & Act
		object[] toggles = typeof(InstallProcessBuilderOptions)
			.GetCustomAttributes(typeof(FeatureToggleAttribute), inherit: true);

		// Assert
		toggles.Should().BeEmpty(
			because: "a gated options type is filtered out of the verb parse array, so 'clio "
				+ "install-process-builder' would report an unknown verb - while the five [RequiresPackage] "
				+ "hints keep telling users to run it. The tool-type test below does not cover this: the two "
				+ "surfaces read different attributes");
	}

	[Test]
	[Category("Unit")]
	[Description("The tool must not be feature-gated, or the remediation the process-designer tools point at would be unreachable exactly when it is needed.")]
	public void InstallProcessBuilderTool_Should_Not_Be_FeatureGated() {
		// Arrange & Act
		object[] toggles = typeof(InstallProcessBuilderTool)
			.GetCustomAttributes(typeof(FeatureToggleAttribute), inherit: true);

		// Assert
		toggles.Should().BeEmpty(
			because: "a gated primitive is filtered out of MCP registration, so gating the installer would "
				+ "hide it while the gated process-designer tools keep telling callers to run it");
	}

	[Test]
	[Category("Unit")]
	[Description("When the install exceeds the MCP response deadline the tool returns exit code 0 with an in-progress note that neither claims the outcome is verified nor tells the caller to call the installer again.")]
	public async Task InstallProcessBuilder_Should_Return_NonVerdict_InProgressNotice_When_ResponseDeadlineExceeded() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns("sandbox-tenant");
		// The resolved install blocks on the gate until the test releases it, so the deadline deterministically
		// wins the race — no timing dependence. Released in finally so the detached work finishes promptly and
		// frees the configuration-build reservation.
		ManualResetEventSlim executeGate = new(false);
		FakeInstallProcessBuilderCommand resolvedCommand = new(exitCode: 0) { ExecuteGate = executeGate };
		commandResolver.Resolve<InstallProcessBuilderCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(resolvedCommand);
		InstallProcessBuilderTool tool = new(ConsoleLogger.Instance, commandResolver) {
			ResponseDeadlineOverride = TimeSpan.FromMilliseconds(50)
		};

		try {
			// Act
			CommandExecutionResult result =
				await tool.InstallProcessBuilder(new InstallProcessBuilderArgs("sandbox"));

			// Assert
			result.ExitCode.Should().Be(0,
				because: "a still-running install is not a failure, and reporting one would send an agent into "
					+ "remediation for a healthy configuration build");
			string notice = string.Join(" ", result.Output.Select(message => message.Value?.ToString()));
			notice.Should().Contain("NOT a verdict",
				because: "unlike restart-by-environment-name (whose write already returned) or compile-creatio "
					+ "(whose operation is recorded and pollable), NOTHING is established at this point — the "
					+ "install may still fail, so the exit code 0 must not read as success");
			notice.Should().MatchRegex(@"(?i)do not call\s+install-process-builder\s+again",
				because: "the notice used to tell the caller to re-run the installer to confirm, which would "
					+ "start a second install, build and restart on an instance already being rebuilt");
		} finally {
			executeGate.Set(); // release the detached work so it finalizes and frees the reservation
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Refuses a second install while a configuration build is already in flight on the same tenant, without resolving or running the command.")]
	public async Task InstallProcessBuilder_Should_Refuse_When_ConfigurationBuild_AlreadyInFlight() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns("busy-tenant");
		// Keyed by TARGET, not tenant — the configuration build is server-wide, so it excludes across
		// principals and shares one key with the worker-routed compile path.
		commandResolver.GetTargetKey(Arg.Any<EnvironmentOptions>()).Returns("busy-target");
		InstallProcessBuilderTool tool = new(ConsoleLogger.Instance, commandResolver);
		McpToolExecutionLock.TryReserveConfigurationBuild("busy-target", out McpToolExecutionLock.BuildReservation heldReservation).Should().BeTrue(
			because: "the test needs to hold the reservation the tool will find taken");

		try {
			// Act
			CommandExecutionResult result =
				await tool.InstallProcessBuilder(new InstallProcessBuilderArgs("sandbox"));

			// Assert
			result.ExitCode.Should().Be(1,
				because: "waiting fixes it, so it is a caller-actionable refusal rather than a clio failure (-1)");
			commandResolver.DidNotReceive().Resolve<InstallProcessBuilderCommand>(Arg.Any<EnvironmentOptions>());
			string refusal = string.Join(" ", result.Output.Select(message => message.Value?.ToString()));
			refusal.Should().Contain("already running",
				because: "the refusal must say WHY it refused; without the broad per-tenant monitor there is "
					+ "nothing to queue behind, and queueing was never right — a second install would rebuild "
					+ "and restart an instance already being rebuilt");
		} finally {
			McpToolExecutionLock.ReleaseConfigurationBuild("busy-tenant", heldReservation);
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("A reservation older than the ceiling is reclaimed, the reclaimed slot still excludes the next caller, and the ORIGINAL holder's release does not free it. All three matter together. The reservation is released by the finally of the work delegate, which past the MCP response deadline runs DETACHED — so that finally is the only release, and the install POST it wraps goes out with Timeout.Infinite. A target that accepts the request and then never answers would otherwise wedge the tenant for the life of the server process, refusing every later install and compile with no in-band recovery. But reclaiming creates a second owner, and an unconditional release let the original delete the reclaimer's live reservation — which switched the guard off entirely for that tenant, arriving at 'a second build on top of a live one' by way of the fix for the wedge. Nothing is cancelled and a build still running past the ceiling keeps running: the ceiling only stops a reservation nobody will ever release from outliving the work it protected.")]
	public void TryReserveConfigurationBuild_ShouldReclaimAndKeepExcluding_WhenOlderThanTheCeiling() {
		// Arrange
		const string tenant = "ceiling-tenant";
		McpToolExecutionLock.ResetConfigurationBuildReservationsForTests();
		McpToolExecutionLock.TryReserveConfigurationBuild(tenant, out McpToolExecutionLock.BuildReservation stalled).Should().BeTrue(
			because: "the first reservation for a tenant must succeed");
		McpToolExecutionLock.BackdateConfigurationBuildReservationForTests(
				tenant,
				McpToolExecutionLock.ConfigurationBuildReservationCeilingForTests + TimeSpan.FromMinutes(1))
			.Should().BeTrue(because: "the reservation just taken must still be there to age");

		try {
			// Act
			bool reclaimed = McpToolExecutionLock.TryReserveConfigurationBuild(tenant, out McpToolExecutionLock.BuildReservation owner);
			bool thirdCallerBeforeRelease = McpToolExecutionLock.TryReserveConfigurationBuild(tenant, out McpToolExecutionLock.BuildReservation _);
			McpToolExecutionLock.ReleaseConfigurationBuild(tenant, stalled);
			bool thirdCallerAfterStaleRelease =
				McpToolExecutionLock.TryReserveConfigurationBuild(tenant, out McpToolExecutionLock.BuildReservation _);

			// Assert
			reclaimed.Should().BeTrue(
				because: "a reservation nobody can release must not wedge the tenant permanently — otherwise one "
					+ "stalled target costs every later install AND compile on it until the process restarts");
			thirdCallerBeforeRelease.Should().BeFalse(
				because: "reclaiming must RE-take the reservation, not merely drop it: the freshly reclaimed slot "
					+ "has to exclude the next caller exactly as the original did");
			thirdCallerAfterStaleRelease.Should().BeFalse(
				because: "the stalled holder's release must be a NO-OP once its slot was reclaimed. Without the "
					+ "ownership token it removed the reclaimer's live reservation, and the next caller then "
					+ "started a configuration build alongside a running one — the guard disabling itself after "
					+ "a single reclaim");
			owner.Should().NotBe(stalled,
				because: "the reclaimer holds a distinct token, which is what makes the stale release decidable");
		} finally {
			McpToolExecutionLock.ResetConfigurationBuildReservationsForTests();
		}
	}

	[Test]
	[Category("Unit")]
	[Description("Writes the post-deadline failure to stderr when the detached install fails after the caller was already answered, because the exit code has no response left to travel on.")]
	public async Task InstallProcessBuilder_Should_ReportToStdErr_When_TheInstallFailsPastTheDeadline() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		TextWriter originalError = Console.Error;
		SignallingStringWriter capturedError = new();
		Console.SetError(capturedError);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns("failing-tenant");
		// exitCode 1, unlike every other case in this fixture: this is the ONLY arrangement that reaches
		// ReportPostDeadlineFailure, whose guard is `ExitCode != 0 && callerAlreadyAnswered`.
		// NOTE: capturedError is a SignallingStringWriter, not a plain StringWriter — see the class remark.
		ManualResetEventSlim executeGate = new(false);
		FakeInstallProcessBuilderCommand resolvedCommand = new(exitCode: 1) { ExecuteGate = executeGate };
		commandResolver.Resolve<InstallProcessBuilderCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(resolvedCommand);
		InstallProcessBuilderTool tool = new(ConsoleLogger.Instance, commandResolver) {
			ResponseDeadlineOverride = TimeSpan.FromMilliseconds(50)
		};

		try {
			// Act
			await tool.InstallProcessBuilder(new InstallProcessBuilderArgs("sandbox"));
			executeGate.Set();
			// The report happens on the detached continuation, so wait for it rather than for a duration.
			capturedError.LineWritten.Wait(TimeSpan.FromSeconds(10)).Should().BeTrue(
				because: "the post-deadline report is the only observable effect left, so the test has to see the "
					+ "detached continuation write it before asserting on the buffer");

			// Assert
			string stderr = capturedError.Snapshot();
			stderr.Should().Contain("FAILED",
				because: "past the response deadline the caller has already been told the install is still "
					+ "running, so the exit code has nowhere to travel. stderr is the only channel left, and "
					+ "without it a failed install is indistinguishable from a slow one — the whole reason this "
					+ "reporter exists. Every other test in this fixture uses exitCode 0, so this branch had no "
					+ "coverage at all: inverting its guard, or dropping the callerAlreadyAnswered write, would "
					+ "have made a post-deadline failure completely silent with the suite still green");
			stderr.Should().Contain("exit code 1",
				because: "the exit code is the only detail distinguishing which failure occurred, and the "
					+ "caller's own transcript no longer carries it");
			stderr.Should().Contain(InstallProcessBuilderTool.InstallProcessBuilderToolName,
				because: "on a shared stderr the line must say which tool wrote it");
		} finally {
			executeGate.Set();
			Console.SetError(originalError);
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	[TearDown]
	public void ClearConfigurationBuildReservations() {
		// The reservation is released on the DETACHED continuation, which can outlive a deadline test, so a
		// leaked one would fast-fail the next test in this fixture.
		McpToolExecutionLock.ResetConfigurationBuildReservationsForTests();
	}

	private sealed class FakeInstallProcessBuilderCommand : InstallProcessBuilderCommand {
		private readonly int _exitCode;

		public FakeInstallProcessBuilderCommand(int exitCode)
			: base(
				new EnvironmentSettings(),
				Substitute.For<IPackageInstaller>(),
				Substitute.For<IBundledPackageCatalog>(),
				Substitute.For<IPackageInstallOutcomeVerifier>(),
				Substitute.For<IServerReadinessWaiter>(),
				Substitute.For<IRequiredPackageChecker>(),
				Substitute.For<ILogger>()) {
			_exitCode = exitCode;
		}

		public InstallProcessBuilderOptions? CapturedOptions { get; private set; }

		/// <summary>
		/// When set, Execute blocks on it, so a deadline test can win the race deterministically.
		/// </summary>
		public ManualResetEventSlim? ExecuteGate { get; init; }

		public override int Execute(InstallProcessBuilderOptions options) {
			CapturedOptions = options;
			ExecuteGate?.Wait();
			return _exitCode;
		}
	}

	/// <summary>
	/// stderr capture for the post-deadline report, which is written from the DETACHED continuation on a pool
	/// thread while the test thread reads the buffer. A <see cref="StringWriter"/> is a StringBuilder, and a
	/// StringBuilder is not thread-safe: polling it with <c>ToString()</c> while the continuation appended threw
	/// <c>ArgumentOutOfRangeException (chunkLength)</c> out of the poll loop in CI, failing the test for a reason
	/// that had nothing to do with the behaviour under test. Both sides go through the same lock, and
	/// <see cref="LineWritten"/> replaces the polling so the wait is deterministic (the same pattern as
	/// <c>McpReadResponseDeadlineTests</c>).
	/// </summary>
	private sealed class SignallingStringWriter : StringWriter {
		private readonly object _sync = new();

		/// <summary>Set once the reporter has written its line.</summary>
		public ManualResetEventSlim LineWritten { get; } = new(false);

		public override void Write(char value) {
			lock (_sync) {
				base.Write(value);
			}
		}

		public override void Write(char[] buffer, int index, int count) {
			lock (_sync) {
				base.Write(buffer, index, count);
			}
		}

		public override void Write(string? value) {
			lock (_sync) {
				base.Write(value);
			}
		}

		public override void WriteLine(string? value) {
			lock (_sync) {
				base.WriteLine(value);
			}

			LineWritten.Set();
		}

		/// <summary>Reads the buffer under the write lock; never call <c>ToString()</c> on this writer instead.</summary>
		public string Snapshot() {
			lock (_sync) {
				return base.ToString();
			}
		}

		protected override void Dispose(bool disposing) {
			if (disposing) {
				LineWritten.Dispose();
			}

			base.Dispose(disposing);
		}
	}

	[Test]
	[Category("Unit")]
	[Description("The args record is the entire agent-reachable surface of this tool, so it must expose exactly one member. --force is deliberately withheld from MCP, and the only thing standing between that decision and an agent is this record - adding a property 'for parity' would expose the override with no other test turning red.")]
	public void InstallProcessBuilderArgs_ShouldExposeOnlyTheEnvironmentName() {
		// Arrange & Act
		string[] properties = typeof(InstallProcessBuilderArgs)
			.GetProperties()
			.Select(property => property.Name)
			.ToArray();

		// Assert
		properties.Should().BeEquivalentTo([nameof(InstallProcessBuilderArgs.EnvironmentName)],
			because: "every member here becomes an argument an agent can send; the downgrade override must "
				+ "stay unreachable from this surface");
	}

}
