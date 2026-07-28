using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClioRing.Models;
using ClioRing.Services;
using ClioRing.ViewModels;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace ClioRing.Tests;

/// <summary>
/// Unit tests for the hardened confirm flow of <see cref="RingViewModel"/>: the typed-confirmation
/// gate for irreversible actions and the listed-environment guard. The clio adapter is substituted
/// so no child process ever runs; the confirm is opened via the public selection command.
/// </summary>
[TestFixture]
[Category("Unit")]
public sealed class RingViewModelConfirmTests {
	private const string EnvName = "ve";

	private IClioAdapter _clio = null!;
	private RingViewModel _sut = null!;

	[SetUp]
	public void SetUp() {
		_clio = Substitute.For<IClioAdapter>();
		_sut = new RingViewModel(_clio, new ActionCatalogLoader(), new InMemoryEnvStateStore(), new NullActionCatalogWatcher());
	}

	[TearDown]
	public void TearDown() {
		_sut.StopWatching();
	}

	private static RingAction TypedConfirmUninstall() => new() {
		Id = "clio-uninstall-creatio",
		Title = "Uninstall Creatio",
		Kind = ActionKind.ClioCommand,
		ClioCommand = new ClioCommandSpec { Verb = "uninstall-creatio" },
		Parameters = new List<ParameterDescriptor> {
			new() { Name = "env", ParameterType = ParameterType.Env, Required = true }
		},
		Risk = Risk.Destructive,
		RequireTypedConfirm = true,
		ConfirmText = "Uninstall Creatio permanently removes '{env}'. Continue?"
	};

	[Test]
	[Description("Manual environment refresh replaces the startup catalog so an environment registered after launch becomes selectable.")]
	public async Task RefreshEnvironmentsAsync_ShouldIncludeNewEnvironment_WhenClioCatalogChangesAfterLaunch() {
		// Arrange
		IReadOnlyList<ClioEnvironment> initial = new[] { LocalEnv("semse") };
		IReadOnlyList<ClioEnvironment> refreshed = new[] { LocalEnv("semse"), LocalEnv("semse-t1") };
		_clio.ListEnvironmentsAsync(Arg.Any<CancellationToken>()).Returns(initial, refreshed);
		await _sut.LoadEnvironmentsAsync();

		// Act
		await _sut.RefreshEnvironmentsCommand.ExecuteAsync(null);

		// Assert
		_sut.FilteredEnvironments.Should().Contain(environment => environment.Name == "semse-t1",
			because: "refresh must replace the launch-time snapshot with clio's current registered environments");
		await _clio.Received(2).ListEnvironmentsAsync(Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("Manual environment refresh removes an environment that was deleted from clio after launch.")]
	public async Task RefreshEnvironmentsAsync_ShouldRemoveDeletedEnvironment_WhenClioCatalogChangesAfterLaunch() {
		// Arrange
		IReadOnlyList<ClioEnvironment> initial = new[] { LocalEnv("semse"), LocalEnv("deleted") };
		IReadOnlyList<ClioEnvironment> refreshed = new[] { LocalEnv("semse") };
		_clio.ListEnvironmentsAsync(Arg.Any<CancellationToken>()).Returns(initial, refreshed);
		await _sut.LoadEnvironmentsAsync();

		// Act
		await _sut.RefreshEnvironmentsCommand.ExecuteAsync(null);

		// Assert
		_sut.FilteredEnvironments.Should().NotContain(environment => environment.Name == "deleted",
			because: "refresh must remove entries that no longer exist in clio's settings file");
		await _clio.Received(2).ListEnvironmentsAsync(Arg.Any<CancellationToken>());
	}

	private static ClioEnvironment LocalEnv(string name) => new(name, "http://localhost:5000", IsNetCore: true);

	private async Task<RingItemViewModel> OpenConfirmForAsync(RingAction action) {
		var item = RingItemViewModel.ForAction(action, _sut.SelectCommand);
		await _sut.SelectCommand.ExecuteAsync(item);
		return item;
	}

	[Test]
	[Description("Opening the confirm for a RequireTypedConfirm action captures the selected env as ConfirmExpected and requires typed confirmation.")]
	public async Task SelectCommand_ShouldRequireTypedConfirm_WhenActionOptsInAndEnvIsSelected() {
		// Arrange — one selectable env (auto-selected) and a typed-confirm destructive action.
		_sut.SetEnvironments(new[] { LocalEnv(EnvName) });

		// Act — open the confirm dialog through the public selection path.
		await OpenConfirmForAsync(TypedConfirmUninstall());

		// Assert — the confirm is pending and gated behind typing the exact selected env name.
		_sut.HasPendingConfirm.Should().BeTrue(because: "a destructive action opens the confirm dialog");
		_sut.RequiresTypedConfirm.Should().BeTrue(because: "the action opted into the stronger typed-confirm gate");
		_sut.ConfirmExpected.Should().Be(EnvName, because: "the expected phrase is the selected environment name captured on open");
		_sut.ConfirmInput.Should().BeEmpty(because: "the typed-confirm box starts empty for each confirm");
	}

	[Test]
	[Description("While armed, ConfirmCommand stays disabled until the typed input matches the selected env name exactly, then enables.")]
	public async Task ConfirmCommand_ShouldEnableOnlyOnExactMatch_WhenTypedConfirmIsRequired() {
		// Arrange — open + arm the typed-confirm dialog.
		_sut.SetEnvironments(new[] { LocalEnv(EnvName) });
		await OpenConfirmForAsync(TypedConfirmUninstall());
		_sut.ConfirmArmed = true; // simulate the arm-delay elapsing deterministically

		// Assert — armed but empty input keeps Run disabled.
		_sut.ConfirmCommand.CanExecute(null).Should().BeFalse(because: "an empty typed input must not enable an irreversible run");

		// Act + Assert — a wrong value keeps Run disabled.
		_sut.ConfirmInput = "not-the-env";
		_sut.ConfirmCommand.CanExecute(null).Should().BeFalse(because: "a mismatched typed input must keep Run disabled");

		// Act + Assert — the exact env name (trimmed) enables Run.
		_sut.ConfirmInput = $"  {EnvName}  ";
		_sut.ConfirmCommand.CanExecute(null).Should().BeTrue(because: "typing the exact env name (whitespace-trimmed) satisfies the gate");
	}

	[Test]
	[Description("Typed-confirm matching is case-sensitive: a different-case value keeps ConfirmCommand disabled.")]
	public async Task ConfirmCommand_ShouldStayDisabled_WhenTypedInputDiffersInCase() {
		// Arrange — open + arm the typed-confirm dialog.
		_sut.SetEnvironments(new[] { LocalEnv(EnvName) });
		await OpenConfirmForAsync(TypedConfirmUninstall());
		_sut.ConfirmArmed = true;

		// Act — type the env name in the wrong case.
		_sut.ConfirmInput = EnvName.ToUpperInvariant();

		// Assert — ordinal comparison rejects a case mismatch.
		_sut.ConfirmCommand.CanExecute(null).Should().BeFalse(because: "the typed confirmation is compared case-sensitively (Ordinal)");
	}

	[Test]
	[Description("A destructive env-required action does not open the confirm and does not run when no environment is selected.")]
	public async Task SelectCommand_ShouldNotOpenConfirm_WhenNoEnvironmentIsSelected() {
		// Arrange — no environments at all, so the hub shows "—" (no listed selection).
		_sut.SetEnvironments(System.Array.Empty<ClioEnvironment>());
		_sut.HasSelectedEnvironment.Should().BeFalse(because: "the arrangement leaves no environment selected");

		// Act — attempt to select the destructive env-required action.
		await OpenConfirmForAsync(TypedConfirmUninstall());

		// Assert — the guard blocks the confirm and surfaces a visible reason instead.
		_sut.HasPendingConfirm.Should().BeFalse(because: "an env-required action must not open confirm without a selected environment");
		_sut.PendingConfirmItem.Should().BeNull(because: "no confirm is pending when the listed-env guard trips");
		_sut.FocusCaption.Should().Be("Select an environment first.", because: "the guard tells the user why nothing happened");
	}

	[Test]
	[Description("A confirmed action with a fixed environment displays that immutable target rather than the current selection.")]
	public async Task SelectCommand_ShouldDisplayFixedEnvironment_WhenActionHasExplicitTarget() {
		// Arrange — the selected environment differs from the action's fixed target.
		_sut.SetEnvironments(new[] { LocalEnv("selected"), LocalEnv("fixed") });
		var action = new RingAction {
			Id = "fixed-target",
			Title = "Fixed target action",
			Kind = ActionKind.ClioCommand,
			ClioCommand = new ClioCommandSpec { Verb = "get-info", EnvName = "fixed" },
			Risk = Risk.Confirm,
			ConfirmText = "Run against '{env}'?"
		};

		// Act
		await OpenConfirmForAsync(action);

		// Assert
		_sut.ConfirmMessage.Should().Contain("fixed",
			because: "the confirmation must display the immutable environment carried by the action");
		_sut.ConfirmMessage.Should().NotContain("selected",
			because: "the current selection is unrelated when the action declares a fixed environment");
		_sut.ConfirmExpected.Should().Be("fixed",
			because: "typed confirmation, when enabled, must use the immutable fixed target");
	}

	[Test]
	[Description("Confirmation is cancelled when the displayed environment disappears before execution.")]
	public async Task ConfirmCommand_ShouldCancel_WhenConfirmedEnvironmentDisappears() {
		// Arrange — open confirmation for the original selected environment, then replace the catalog selection.
		_sut.SetEnvironments(new[] { LocalEnv("original") });
		_clio.ListEnvironmentsAsync(Arg.Any<CancellationToken>()).Returns(System.Array.Empty<ClioEnvironment>());
		await OpenConfirmForAsync(TypedConfirmUninstall());
		_sut.SetEnvironments(new[] { LocalEnv("replacement") });
		_sut.ConfirmArmed = true;
		_sut.ConfirmInput = "original";

		// Act
		await _sut.ConfirmCommand.ExecuteAsync(null);

		// Assert
		_sut.FocusCaption.Should().Be("Environment changed. Review and confirm again.",
			because: "a removed confirmed target requires a fresh selection and confirmation");
		await _clio.DidNotReceive().RunAsync(
			Arg.Any<ClioInvocation>(),
			Arg.Any<System.Action<ClioOutputLine>>(),
			Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("Confirmation executes the captured environment when clio's authoritative target signature is unchanged.")]
	public async Task ConfirmCommand_ShouldExecuteCapturedEnvironment_WhenAuthoritativeTargetIsUnchanged() {
		// Arrange
		ClioEnvironment original = LocalEnv("original");
		_sut.SetEnvironments(new[] { original });
		_clio.ListEnvironmentsAsync(Arg.Any<CancellationToken>()).Returns(new[] { original });
		_clio.RunAsync(Arg.Any<ClioInvocation>(), Arg.Any<System.Action<ClioOutputLine>>(), Arg.Any<CancellationToken>())
			.Returns(new ClioRunResult(0, string.Empty, string.Empty, Cancelled: false));
		await OpenConfirmForAsync(TypedConfirmUninstall());
		_sut.ConfirmArmed = true;
		_sut.ConfirmInput = "original";

		// Act
		await _sut.ConfirmCommand.ExecuteAsync(null);

		// Assert
		await _clio.Received(1).RunAsync(
			Arg.Is<ClioInvocation>(invocation => invocation != null && invocation.EnvName == "original"),
			Arg.Any<System.Action<ClioOutputLine>>(),
			Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("Confirmation is cancelled when the displayed environment name is retargeted to a different endpoint before execution.")]
	public async Task ConfirmCommand_ShouldCancel_WhenConfirmedEnvironmentEndpointChanges() {
		// Arrange — keep the UI cache unchanged while clio's authoritative catalog retargets the same name.
		_sut.SetEnvironments(new[] { new ClioEnvironment("original", "http://localhost:5000", IsNetCore: true) });
		_clio.ListEnvironmentsAsync(Arg.Any<CancellationToken>())
			.Returns(new[] { new ClioEnvironment("original", "http://localhost:5001", IsNetCore: true) });
		await OpenConfirmForAsync(TypedConfirmUninstall());
		_sut.ConfirmArmed = true;
		_sut.ConfirmInput = "original";

		// Act
		await _sut.ConfirmCommand.ExecuteAsync(null);

		// Assert
		_sut.HasPendingConfirm.Should().BeFalse(
			because: "a stale confirmation must close instead of remaining executable");
		_sut.FocusCaption.Should().Be("Environment changed. Review and confirm again.",
			because: "the user must be told to review the new endpoint before retrying");
		await _clio.DidNotReceive().RunAsync(
			Arg.Any<ClioInvocation>(),
			Arg.Any<System.Action<ClioOutputLine>>(),
			Arg.Any<CancellationToken>());
	}

	[Test]
	[Description("A confirmed environment-catalog action omits the selected environment target and refreshes environments after success.")]
	public async Task ConfirmCommand_ShouldRefreshEnvironmentsWithoutSelectedTarget_WhenCatalogActionSucceeds() {
		// Arrange — no selected environment; the command updates the catalog itself and then discovers its result.
		var action = new RingAction {
			Id = "clio-import-iis-environments",
			Title = "Import IIS Environments",
			Kind = ActionKind.ClioCommand,
			ClioCommand = new ClioCommandSpec { Verb = "reg", Args = new[] { "--add-from-iis" } },
			Risk = Risk.Confirm,
			ConfirmText = "Scan local IIS and update the local clio environment catalog?",
			RefreshEnvironmentsOnSuccess = true
		};
		_clio.RunAsync(Arg.Any<ClioInvocation>(), Arg.Any<System.Action<ClioOutputLine>>(), Arg.Any<CancellationToken>())
			.Returns(new ClioRunResult(0, string.Empty, string.Empty, Cancelled: false));
		_clio.ListEnvironmentsAsync(Arg.Any<CancellationToken>()).Returns(new[] { LocalEnv("imported") });
		await OpenConfirmForAsync(action);
		_sut.ConfirmMessage.Should().Be("Scan local IIS and update the local clio environment catalog?",
			because: "a catalog-wide action must not present the unrelated selected-environment target");

		// Act — approve the catalog-level confirmation.
		_sut.ConfirmArmed = true;
		await _sut.ConfirmCommand.ExecuteAsync(null);

		// Assert — no unrelated selected target is shown and the successful command refreshes the picker.
		_sut.ConfirmMessage.Should().BeEmpty(because: "the approved confirmation closes before execution");
		_sut.FilteredEnvironments.Should().Contain(environment => environment.Name == "imported",
			because: "successful catalog mutation must refresh environments even without a filesystem watcher");
		await _clio.Received(1).RunAsync(
			Arg.Is<ClioInvocation>(invocation => invocation != null
				&& invocation.Verb == "reg"
				&& invocation.Args.Count == 1
				&& invocation.Args[0] == "--add-from-iis"
				&& invocation.EnvName == null),
			Arg.Any<System.Action<ClioOutputLine>>(),
			Arg.Any<CancellationToken>());
		await _clio.Received(1).ListEnvironmentsAsync(Arg.Any<CancellationToken>());
	}
}
