using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClioLauncher.Models;
using ClioLauncher.Services;
using ClioLauncher.ViewModels;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace ClioLauncher.Tests;

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

	private static LauncherAction TypedConfirmUninstall() => new() {
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

	private async Task<RingItemViewModel> OpenConfirmForAsync(LauncherAction action) {
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
}
