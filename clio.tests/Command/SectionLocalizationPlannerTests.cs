using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// ENG-90576 ADR D11: the plan that writes a section's other-culture values back around the platform's
/// ApplicationSection update (F11), and its write and readback steps.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class SectionLocalizationPlannerTests {
	private IApplicationSectionLocalizationClient _localizationClient = null!;
	private IApplicationClient _client = null!;
	private EnvironmentSettings _settings = null!;
	private SectionLocalizationPlanner _sut = null!;

	[SetUp]
	public void SetUp() {
		_localizationClient = Substitute.For<IApplicationSectionLocalizationClient>();
		_client = Substitute.For<IApplicationClient>();
		_settings = new EnvironmentSettings { Uri = "https://example.invalid" };
		_sut = new SectionLocalizationPlanner(_localizationClient);
	}

	[Test]
	[Description("A caption sent through the ApplicationSection update owns the profile-culture caption: the plan neither restores nor verifies the old one.")]
	public void BuildPlan_Should_SkipProfileCaption_WhenCaptionWentThroughSection() {
		// Arrange
		SectionLocalizationPlanInput input = Input(
			[new SectionLocalizationRow("de-DE", "Alt", null, null)],
			profileCulture: "de-DE", captionThroughSection: true, currentProfileCaption: "Neu");

		// Act
		SectionLocalizationPlan plan = _sut.BuildPlan(input);

		// Assert
		plan.ColumnValues.Should().NotContainKey("Caption",
			because: "writing the old caption back would overwrite the value the update has just stored");
		plan.ExpectedCells.Should().ContainSingle(cell => cell.CultureName == "de-DE" && !cell.Verify,
			because: "the ApplicationSection update owns that value, so the readback does not check it");
	}

	[Test]
	[Description("A description sent through the ApplicationSection update is not written back for the profile culture; other cultures are.")]
	public void BuildPlan_Should_SkipProfileDescription_WhenDescriptionWentThroughSection() {
		// Arrange
		SectionLocalizationPlanInput input = Input(
			[new SectionLocalizationRow("de-DE", null, "Alt", null), new SectionLocalizationRow("es-ES", null, "Viejo", null)],
			profileCulture: "de-DE", descriptionThroughSection: true);

		// Act
		SectionLocalizationPlan plan = _sut.BuildPlan(input);

		// Assert
		plan.ColumnValues["Description"].Should().BeEquivalentTo(
			new Dictionary<string, string> { ["es-ES"] = "Viejo" },
			because: "only the value the update did not write is restored");
		plan.RestoresDeletedValues.Should().BeTrue(because: "the Spanish description was deleted by the update");
	}

	[Test]
	[Description("Without an ApplicationSection update nothing was deleted: a caption in another culture is the only write and the plan does not claim to restore anything.")]
	public void BuildPlan_Should_WriteOnlyTarget_WhenSectionUpdateDidNotRun() {
		// Arrange
		SectionLocalizationPlanInput input = Input(
			[new SectionLocalizationRow("de-DE", "Bestellungen", null, null)],
			sectionUpdateRan: false, targetCulture: "es-ES", localizedCaption: "Pedidos");

		// Act
		SectionLocalizationPlan plan = _sut.BuildPlan(input);

		// Assert
		plan.HasWrites.Should().BeTrue(because: "the requested caption must be written");
		plan.RestoresDeletedValues.Should().BeFalse(because: "the localization write merges per culture and deletes nothing");
		plan.ColumnValues["Caption"].Should().BeEquivalentTo(
			new Dictionary<string, string> { ["es-ES"] = "Pedidos", ["en-US"] = "Orders" },
			because: "the kept German caption needs no write; the profile caption is required (F12)");
		plan.PreservedCultures.Should().Equal(["de-DE"], because: "the German caption is still verified as kept");
	}

	[Test]
	[Description("A caption targeted at the default culture is written but not verified in the localization rows, which do not hold it (F10).")]
	public void BuildPlan_Should_NotVerifyTarget_WhenTargetIsDefaultCulture() {
		// Arrange
		SectionLocalizationPlanInput input = Input(
			[], profileCulture: "uk-UA", sectionUpdateRan: false, targetCulture: "en-US", localizedCaption: "Orders",
			currentProfileCaption: "Замовлення");

		// Act
		SectionLocalizationPlan plan = _sut.BuildPlan(input);

		// Assert
		plan.ExpectedCells.Should().ContainSingle(cell => cell.IsTarget && !cell.Verify,
			because: "the default culture lives in SysModule itself");
	}

	[Test]
	[Description("A failed localization write that restores nothing is rethrown as is: no value was lost.")]
	public void Apply_Should_RethrowOriginalError_WhenNothingWasDeleted() {
		// Arrange
		SectionLocalizationPlan plan = _sut.BuildPlan(Input(
			[], sectionUpdateRan: false, targetCulture: "es-ES", localizedCaption: "Pedidos"));
		InvalidOperationException failure = new("UpdateLocalizationQuery failed: boom");
		_localizationClient.When(client => client.WriteLocalizations(_client, _settings, "section-id", plan.ColumnValues))
			.Do(_ => throw failure);

		// Act
		Action action = () => _sut.Apply(_client, _settings, "section-id", "pkg-uid", "UsrOrders", plan, []);

		// Assert
		action.Should().Throw<InvalidOperationException>(because: "the write failed")
			.Which.Should().BeSameAs(failure, because: "there is no snapshot to quote when nothing was deleted");
	}

	[Test]
	[Description("A plan without writes sends nothing and does not touch the package binding.")]
	public void Apply_Should_DoNothing_WhenPlanHasNoWrites() {
		// Arrange
		SectionLocalizationPlan plan = _sut.BuildPlan(Input([], sectionUpdateRan: false));

		// Act
		_sut.Apply(_client, _settings, "section-id", "pkg-uid", "UsrOrders", plan, []);

		// Assert
		_localizationClient.DidNotReceiveWithAnyArgs().WriteLocalizations(default!, default!, default!, default!);
		_localizationClient.DidNotReceiveWithAnyArgs().RefreshSectionPackageBinding(default!, default!, default!, default!);
	}

	[Test]
	[Description("A failed binding re-save is a warning whose server text is redacted and bounded, because the warning goes out in a success response.")]
	public void Apply_Should_RedactAndBoundBindingFailureWarning() {
		// Arrange
		SectionLocalizationPlan plan = _sut.BuildPlan(Input(
			[], sectionUpdateRan: false, targetCulture: "es-ES", localizedCaption: "Pedidos"));
		string serverText = "SaveSchema failed: password=hunter2 " + new string('x', 2000);
		_localizationClient.RefreshSectionPackageBinding(_client, _settings, "pkg-uid", "UsrOrders")
			.Returns(_ => throw new InvalidOperationException(serverText));
		List<string> warnings = [];

		// Act
		_sut.Apply(_client, _settings, "section-id", "pkg-uid", "UsrOrders", plan, warnings);

		// Assert
		warnings.Should().ContainSingle(because: "the stored write is still a success")
			.Which.Should().NotContain("hunter2", because: "credential values must not reach the agent")
			.And.Contain("SaveSchema failed", because: "the reason must still be readable");
		warnings[0].Length.Should().BeLessThan(1000, because: "a whole server page must not be copied into the warning");
	}

	[Test]
	[Description("ReadCell rejects a column that is not a localizable section column instead of returning another column's value.")]
	public void ReadCell_Should_Throw_WhenColumnIsUnknown() {
		// Arrange
		SectionLocalizationRow[] rows = [new("es-ES", "Pedidos", "Desc", "Header")];

		// Act
		Action action = () => SectionLocalizationPlanner.ReadCell(rows, "Name", "es-ES");

		// Assert
		action.Should().Throw<ArgumentOutOfRangeException>(because: "an unknown column is a programming error");
	}

	private static SectionLocalizationPlanInput Input(
		IReadOnlyList<SectionLocalizationRow> snapshot,
		string profileCulture = "en-US",
		bool sectionUpdateRan = true,
		string? targetCulture = null,
		string? localizedCaption = null,
		bool captionThroughSection = false,
		string currentProfileCaption = "Orders",
		bool descriptionThroughSection = false) =>
		new(snapshot, sectionUpdateRan, profileCulture, targetCulture ?? profileCulture, localizedCaption,
			captionThroughSection, currentProfileCaption, descriptionThroughSection);
}
