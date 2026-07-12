using System;
using System.IO;
using System.Linq;
using ClioRing.Models;
using ClioRing.Services;
using FluentAssertions;
using NUnit.Framework;

namespace ClioRing.Tests;

/// <summary>
/// Unit tests asserting the shipped default catalog (<c>actions.json</c>, copied next to the test
/// binaries) loads and validates, and exposes the guided Uninstall Creatio flow as a primary main-ring
/// action (story 9). The uninstall is no longer a ClioCommand with an exact-name typed confirm — that is
/// superseded by the guided flow (local-env picker → simple Yes/No confirm → shared pipeline), so the
/// catalog entry carries no typed command block.
/// </summary>
[TestFixture]
[Category("Unit")]
public sealed class ActionCatalogUninstallCreatioTests {
	private static string ShippedCatalogPath =>
		Path.Combine(AppContext.BaseDirectory, ActionCatalogLoader.DefaultFileName);

	[Test]
	[Description("The shipped actions.json loads and validates without throwing.")]
	public void Load_ShouldReturnValidatedCatalog_WhenShippedActionsJsonIsUsed() {
		// Arrange — the concrete loader over the shipped default catalog.
		var loader = new ActionCatalogLoader();

		// Act — load and validate the shipped catalog.
		ActionCatalog catalog = loader.Load(ShippedCatalogPath);

		// Assert — a non-empty, fully-validated catalog is returned.
		catalog.Actions.Should().NotBeEmpty(because: "the shipped catalog defines the default ring actions");
	}

	[Test]
	[Description("The shipped catalog exposes Uninstall Creatio as a primary GuidedUninstall main-ring action with the trash icon.")]
	public void Load_ShouldContainGuidedUninstallCreatioAction_WhenShippedActionsJsonIsUsed() {
		// Arrange — the concrete loader over the shipped default catalog.
		var loader = new ActionCatalogLoader();

		// Act — load the catalog and locate the uninstall action.
		ActionCatalog catalog = loader.Load(ShippedCatalogPath);
		RingAction? uninstall = catalog.Actions.FirstOrDefault(a =>
			string.Equals(a.Id, "clio-uninstall-creatio", StringComparison.OrdinalIgnoreCase));

		// Assert — the action exists, opens the guided flow, and reuses the trash icon.
		uninstall.Should().NotBeNull(because: "Uninstall Creatio is a shipped primary main-ring action");
		uninstall!.Kind.Should().Be(ActionKind.GuidedUninstall,
			because: "story 9 makes Uninstall a guided flow the ring host opens, not a raw ClioCommand");
		uninstall.Icon.Should().Be("trash", because: "the guided Uninstall reuses the trash icon");
		uninstall.Title.Should().Be("Uninstall Creatio", because: "the ring action names the operation for the user");
	}

	[Test]
	[Description("The guided Uninstall action carries no typed command block — the flow, not the catalog, dispatches uninstall-creatio.")]
	public void Load_ShouldCarryNoTypedCommandBlockForUninstall_WhenShippedActionsJsonIsUsed() {
		// Arrange — the concrete loader over the shipped default catalog.
		var loader = new ActionCatalogLoader();

		// Act — load the catalog and locate the uninstall action.
		ActionCatalog catalog = loader.Load(ShippedCatalogPath);
		RingAction uninstall = catalog.Actions.First(a =>
			string.Equals(a.Id, "clio-uninstall-creatio", StringComparison.OrdinalIgnoreCase));

		// Assert — a guided action carries no ClioCommand/OpenUrl/OpenPath typed block.
		uninstall.ClioCommand.Should().BeNull(because: "a guided action does not run a raw clio verb from the catalog");
		uninstall.OpenUrl.Should().BeNull(because: "a guided action carries no OpenUrl block");
		uninstall.OpenPath.Should().BeNull(because: "a guided action carries no OpenPath block");
	}

	[Test]
	[Description("The guided Uninstall action no longer requires an exact-name typed confirm (superseded by the simple Yes/No flow).")]
	public void Load_ShouldNotRequireTypedConfirmForUninstall_WhenShippedActionsJsonIsUsed() {
		// Arrange — the concrete loader over the shipped default catalog.
		var loader = new ActionCatalogLoader();

		// Act — load the catalog and locate the uninstall action.
		ActionCatalog catalog = loader.Load(ShippedCatalogPath);
		RingAction uninstall = catalog.Actions.First(a =>
			string.Equals(a.Id, "clio-uninstall-creatio", StringComparison.OrdinalIgnoreCase));

		// Assert — the exact-name typed confirm is superseded; the guided flow owns the Yes/No confirm instead.
		uninstall.RequireTypedConfirm.Should().BeFalse(
			because: "story 9 replaces exact-name typing with a simple Yes/No confirm inside the guided flow");
	}

	[Test]
	[Description("The Manage Environments action requests JSON so clio returns its masked environment projection.")]
	public void Load_ShouldRequestMaskedJsonForManageEnvironments_WhenShippedActionsJsonIsUsed() {
		// Arrange
		var loader = new ActionCatalogLoader();

		// Act
		ActionCatalog catalog = loader.Load(ShippedCatalogPath);
		RingAction manageEnvironments = catalog.Actions.First(a =>
			string.Equals(a.Id, "clio-manage-envs", StringComparison.OrdinalIgnoreCase));

		// Assert
		manageEnvironments.ClioCommand.Should().NotBeNull(
			because: "Manage Environments is a clio command action");
		manageEnvironments.ClioCommand!.Verb.Should().Be("show-web-app-list",
			because: "the action reads the clio-owned environment catalog");
		manageEnvironments.ClioCommand.Args.Should().Equal(["--json"],
			because: "JSON output uses clio's masked projection and must not expose stored credentials");
	}
}
