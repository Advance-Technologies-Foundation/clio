using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Relay;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

// NonParallelizable, for the same reason CompileCreatioToolTests and InstallProcessBuilderToolTests are:
// McpToolExecutionLock is process-global static state, this fixture both CONFIGURES and RESETS it, and an
// unkeyed reset landing in the middle of a sibling fixture's test would fail a test with no bug present.
[NonParallelizable]
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ConfigurationBuildReservationDomainTests {

	// One target, reached from both sides — the whole point of the fixture. Both call sites derive it from
	// IToolCommandResolver.GetTargetKey, so a single literal is the honest stand-in for one environment.
	private const string Target = "https://exclusion.creatio.com";

	private const string OtherTarget = "https://other.creatio.com";

	[SetUp]
	public void ResetTheReservationDomain() =>
		McpToolExecutionLock.ResetConfigurationBuildReservationsForTests();

	[TearDown]
	public void DropTheBridgeAndTheReservations() =>
		// Own teardown rather than relying on the next fixture's setup: a bridge left configured points a
		// facade at a store nobody else resets, and the reservations are released on a detached
		// continuation that can outlive the test method.
		McpToolExecutionLock.ResetConfigurationBuildReservationsForTests();

	[Test]
	[Category("Unit")]
	[Description("An in-process tool is refused when the worker dispatcher already holds the configuration build for that target. This is the shipped split: compile-creatio is routed to a worker and reserves through the parent-owned ISharedResourceReservation, install-process-builder is deliberately withheld from the worker cohort and reserves through this facade. Keying both by the normalised target is necessary and NOT sufficient — with two stores the same key excludes nothing, and two overlapping configuration builds on one environment corrupt each other's package compilation state while both restart the application.")]
	public void TryReserveConfigurationBuild_ShouldRefuse_WhenTheDispatcherHoldsTheSameTarget() {
		// Arrange
		ISharedResourceReservation parentOwned = new SharedResourceReservation();
		McpToolExecutionLock.Configure(null, null, parentOwned);
		parentOwned.TryReserve(McpToolSharedFileResource.ConfigurationBuild, Target, out SharedResourceReservationToken _)
			.Should().BeTrue(
				because: "the arrangement is the dispatcher having reserved before it spawned the compile worker");

		// Act
		bool inProcess = McpToolExecutionLock.TryReserveConfigurationBuild(
			Target, out McpToolExecutionLock.BuildReservation _);

		// Assert
		inProcess.Should().BeFalse(
			because: "the in-process tool must land in the SAME store the dispatcher reserved in; a second "
				+ "dictionary behind the same key is exclusion that looks present and is not");
	}

	[Test]
	[Category("Unit")]
	[Description("The worker dispatcher is refused when an in-process tool already holds the configuration build for that target. Asserted in this direction too because a one-way bridge is not exclusion: install-process-builder runs in-process and starts first at least as often as compile-creatio does, and if the dispatcher cannot see that reservation it spawns a compile worker straight into a running configuration build.")]
	public void TryReserve_ShouldRefuse_WhenAnInProcessToolHoldsTheSameTarget() {
		// Arrange
		ISharedResourceReservation parentOwned = new SharedResourceReservation();
		McpToolExecutionLock.Configure(null, null, parentOwned);
		McpToolExecutionLock.TryReserveConfigurationBuild(Target, out McpToolExecutionLock.BuildReservation _)
			.Should().BeTrue(
				because: "the arrangement is an in-process install-process-builder having reserved first");

		// Act
		bool dispatcher = parentOwned.TryReserve(
			McpToolSharedFileResource.ConfigurationBuild, Target, out SharedResourceReservationToken _);

		// Assert
		dispatcher.Should().BeFalse(
			because: "the dispatcher consults the parent-owned store before it spawns, so an in-process "
				+ "holder has to be visible there or the worker starts a second build on a live one");
	}

	[Test]
	[Category("Unit")]
	[Description("A reservation on one target does not deny a different target. One store must not become one bucket: the exclusion is keyed by normalised target precisely so that it is server-wide for ONE environment and silent about every other, and a bridge that lost the key would turn a single compile into a global outage.")]
	public void TryReserveConfigurationBuild_ShouldReserve_WhenTheDispatcherHoldsADifferentTarget() {
		// Arrange
		ISharedResourceReservation parentOwned = new SharedResourceReservation();
		McpToolExecutionLock.Configure(null, null, parentOwned);
		parentOwned.TryReserve(McpToolSharedFileResource.ConfigurationBuild, OtherTarget, out SharedResourceReservationToken _)
			.Should().BeTrue(because: "the arrangement needs a live holder on the OTHER environment");

		// Act
		bool inProcess = McpToolExecutionLock.TryReserveConfigurationBuild(
			Target, out McpToolExecutionLock.BuildReservation _);

		// Assert
		inProcess.Should().BeTrue(
			because: "Creatio serialises configuration builds per SERVER, so a build on one environment says "
				+ "nothing about another and must not refuse it");
	}

	[Test]
	[Category("Unit")]
	[Description("Releasing across the bridge is ownership-aware: after the ceiling reclaims a stalled reservation there are two logical owners, and the ORIGINAL holder's release must be a no-op while the RECLAIMER's frees the target. An unconditional remove would let the original delete the reclaimer's live reservation, after which any third caller starts a configuration build alongside a running one — the guard switching itself off for that target after a single reclaim.")]
	public void ReleaseConfigurationBuild_ShouldOnlyFreeTheTarget_WhenGivenTheHoldersToken() {
		// Arrange
		ISharedResourceReservation reclaimsAtOnce = new SharedResourceReservation(TimeSpan.FromMilliseconds(1));
		McpToolExecutionLock.Configure(null, null, reclaimsAtOnce);
		McpToolExecutionLock.TryReserveConfigurationBuild(Target, out McpToolExecutionLock.BuildReservation stalled)
			.Should().BeTrue(because: "the first reservation on a free target must succeed");
		WaitPastCeiling(TimeSpan.FromMilliseconds(1));

		// Act
		bool reclaimed = McpToolExecutionLock.TryReserveConfigurationBuild(
			Target, out McpToolExecutionLock.BuildReservation owner);
		McpToolExecutionLock.ReleaseConfigurationBuild(Target, stalled);
		bool afterStaleRelease = McpToolExecutionLock.TryReserveConfigurationBuild(
			Target, out McpToolExecutionLock.BuildReservation _);
		McpToolExecutionLock.ReleaseConfigurationBuild(Target, owner);
		bool afterOwnerRelease = reclaimsAtOnce.TryReserve(
			McpToolSharedFileResource.ConfigurationBuild, Target, out SharedResourceReservationToken _);

		// Assert
		reclaimed.Should().BeTrue(
			because: "a reservation nobody will ever release must not wedge a target for the life of the "
				+ "server process — the install POST goes out with Timeout.Infinite, so its finally is the "
				+ "only release there is");
		afterStaleRelease.Should().BeFalse(
			because: "the stalled holder's token is no longer the holder, so its release must free nothing");
		afterOwnerRelease.Should().BeTrue(
			because: "the reclaimer's own token must free the target IN THE SHARED STORE, which is also what "
				+ "proves the release crossed the bridge rather than removing an entry nobody consults");
	}

	[Test]
	[Category("Unit")]
	[Description("The reclaim ceiling that applies when bridged is the parent's, and only the parent's. Asserted differentially against two stores over one elapsed window — the short-ceiling one reclaims, the 30-minute one refuses — so the test proves WHICH ceiling fired rather than that some ceiling did. Keyed by target alone, one stuck holder denies a whole environment, so the ceiling is the bound on that denial and must survive the move into one store.")]
	public void TryReserveConfigurationBuild_ShouldReclaimOnTheBridgesCeiling_AndNotOnItsOwn() {
		// Arrange
		ISharedResourceReservation reclaimsAtOnce = new SharedResourceReservation(TimeSpan.FromMilliseconds(1));
		ISharedResourceReservation neverReclaims = new SharedResourceReservation(TimeSpan.FromMinutes(30));
		McpToolExecutionLock.Configure(null, null, reclaimsAtOnce);
		McpToolExecutionLock.TryReserveConfigurationBuild(Target, out McpToolExecutionLock.BuildReservation _)
			.Should().BeTrue(because: "the short-ceiling store needs a holder to age");
		neverReclaims.TryReserve(McpToolSharedFileResource.ConfigurationBuild, Target, out SharedResourceReservationToken _)
			.Should().BeTrue(because: "the long-ceiling store needs a holder taken in the same window");
		WaitPastCeiling(TimeSpan.FromMilliseconds(1));

		// Act
		bool onShortCeiling = McpToolExecutionLock.TryReserveConfigurationBuild(
			Target, out McpToolExecutionLock.BuildReservation _);
		McpToolExecutionLock.ResetConfigurationBuildReservationsForTests();
		McpToolExecutionLock.Configure(null, null, neverReclaims);
		bool onLongCeiling = McpToolExecutionLock.TryReserveConfigurationBuild(
			Target, out McpToolExecutionLock.BuildReservation _);

		// Assert
		onShortCeiling.Should().BeTrue(
			because: "past the bridge's ceiling the slot is reclaimed, so a holder that can never release "
				+ "cannot outlive the work it was protecting");
		onLongCeiling.Should().BeFalse(
			because: "the SAME elapsed window is inside the 30-minute ceiling, which is what shows the "
				+ "reclaim decision belongs to the bridged store and not to a second ceiling of the facade's own");
	}

	[Test]
	[Category("Unit")]
	[Description("The ceiling the facade reports is the bridge's while one is configured and its own otherwise, because there must be exactly one ceiling in effect at a time. Two ceilings that disagree is the same defect as two stores: whichever one a reader consults, the other is silently governing something.")]
	public void ConfigurationBuildReservationCeiling_ShouldBeTheBridges_WhileOneIsConfigured() {
		// Arrange
		TimeSpan bridgeCeiling = TimeSpan.FromMinutes(7);
		ISharedResourceReservation parentOwned = new SharedResourceReservation(bridgeCeiling);
		TimeSpan unbridged = McpToolExecutionLock.ConfigurationBuildReservationCeilingForTests;

		// Act
		McpToolExecutionLock.Configure(null, null, parentOwned);
		TimeSpan bridged = McpToolExecutionLock.ConfigurationBuildReservationCeilingForTests;

		// Assert
		unbridged.Should().Be(TimeSpan.FromMinutes(30),
			because: "with no bridge the facade's own 30-minute ceiling is the one in effect, unchanged");
		bridged.Should().Be(bridgeCeiling,
			because: "once bridged the facade's dictionary is not consulted at all, so its ceiling governs "
				+ "nothing and reporting it would be reporting a number that cannot fire");
	}

	[Test]
	[Category("Unit")]
	[Description("With no reservation configured — plain CLI, unit tests, any non-MCP host — the static path keeps working exactly as it did: a target is reserved once, a second caller is refused, and the holder's release frees it. The fallback is the null bridge, so nothing about a host that never calls Configure changes.")]
	public void TryReserveConfigurationBuild_ShouldUseItsOwnStore_WhenNoReservationIsConfigured() {
		// Arrange
		ISharedResourceReservation neverConfigured = new SharedResourceReservation();

		// Act
		bool first = McpToolExecutionLock.TryReserveConfigurationBuild(
			Target, out McpToolExecutionLock.BuildReservation held);
		bool second = McpToolExecutionLock.TryReserveConfigurationBuild(
			Target, out McpToolExecutionLock.BuildReservation _);
		int heldElsewhere = neverConfigured.HeldCount;
		McpToolExecutionLock.ReleaseConfigurationBuild(Target, held);
		bool afterRelease = McpToolExecutionLock.TryReserveConfigurationBuild(
			Target, out McpToolExecutionLock.BuildReservation _);

		// Assert
		first.Should().BeTrue(because: "an unbridged host must still be able to reserve its own target");
		second.Should().BeFalse(
			because: "the in-process exclusion is the only guard an unbridged host has, so it must still "
				+ "refuse a second same-target build");
		heldElsewhere.Should().Be(0,
			because: "a reservation store that was never configured must not be reached; the fallback is "
				+ "the facade's own dictionary, not some ambient instance");
		afterRelease.Should().BeTrue(because: "the holder's release must free the target on the static path too");
	}

	[Test]
	[Category("Unit")]
	[Description("A blank tenant/target key still reserves rather than throwing once bridged, because the parent store rejects a blank key by contract while the facade has always folded one onto its shared fallback key. Environment-less and unresolvable calls reach this path, and turning a reservation lookup into an exception would fail the call at the guard instead of at the command.")]
	public void TryReserveConfigurationBuild_ShouldNotThrow_WhenTheKeyIsBlankAndBridged() {
		// Arrange
		ISharedResourceReservation parentOwned = new SharedResourceReservation();
		McpToolExecutionLock.Configure(null, null, parentOwned);

		// Act
		bool first = McpToolExecutionLock.TryReserveConfigurationBuild(
			null, out McpToolExecutionLock.BuildReservation _);
		bool second = McpToolExecutionLock.TryReserveConfigurationBuild(
			"   ", out McpToolExecutionLock.BuildReservation _);

		// Assert
		first.Should().BeTrue(
			because: "a blank key normalises to the shared fallback key exactly as it did before the bridge, "
				+ "so the guard answers instead of throwing");
		second.Should().BeFalse(
			because: "blank and whitespace fold onto the SAME fallback key, so the second call must be "
				+ "refused by the first — normalising on only one of the two paths would split the key again");
	}

	[Test]
	[Category("Unit")]
	[Description("AC-03 at the tool boundary: install-process-builder refuses, without resolving or running its command, while the worker dispatcher holds the configuration build for that environment. This is the shipped arrangement — compile-creatio routed to a worker, install-process-builder withheld from the cohort because the kill-safety audit lists it as leaving damage nothing repairs — so the exclusion between the two families has to hold across that boundary or it holds nowhere.")]
	public async Task InstallProcessBuilder_ShouldRefuse_WhenTheDispatcherHoldsTheConfigurationBuild() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.GetTenantKey(Arg.Any<EnvironmentOptions>()).Returns("dispatcher-tenant");
		commandResolver.GetTargetKey(Arg.Any<EnvironmentOptions>()).Returns(Target);
		InstallProcessBuilderTool tool = new(ConsoleLogger.Instance, commandResolver);
		ISharedResourceReservation parentOwned = new SharedResourceReservation();
		McpToolExecutionLock.Configure(null, null, parentOwned);
		parentOwned.TryReserve(McpToolSharedFileResource.ConfigurationBuild, Target, out SharedResourceReservationToken _)
			.Should().BeTrue(because: "the arrangement is a worker-routed compile already holding the target");

		try {
			// Act
			CommandExecutionResult result =
				await tool.InstallProcessBuilder(new InstallProcessBuilderArgs("sandbox"));

			// Assert
			result.ExitCode.Should().Be(1,
				because: "waiting fixes it, so it is a caller-actionable refusal rather than a clio failure");
			commandResolver.DidNotReceive().Resolve<InstallProcessBuilderCommand>(Arg.Any<EnvironmentOptions>());
			string refusal = string.Join(" ", result.Output.Select(message => message.Value?.ToString()));
			refusal.Should().Contain("already running",
				because: "the refusal must say why it refused, and a second install would rebuild and restart "
					+ "an instance the compile is already rebuilding");
		} finally {
			ConsoleLogger.Instance.ClearMessages();
		}
	}

	// Spins until the monotonic clock has advanced strictly past the ceiling. One-directional — it can only
	// wait LONGER on a slow agent, never shorter — so it cannot flake the way a fixed sleep sized against a
	// tick resolution can. Environment.TickCount64 is what the reservation stamps are measured in, and on
	// Windows it advances in ~15.6 ms steps, so reading it is the only honest way to know the window passed.
	private static void WaitPastCeiling(TimeSpan ceiling) {
		long start = Environment.TickCount64;
		while (Environment.TickCount64 - start <= (long)ceiling.TotalMilliseconds) {
			Thread.Sleep(1);
		}
	}
}
