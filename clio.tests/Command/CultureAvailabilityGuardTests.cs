using System;
using Clio.Command;
using Clio.Command.Localization;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class CultureAvailabilityGuardTests : BaseClioModuleTests {

	private ICreatioCultureCatalog _catalog;
	private ILogger _logger;
	private ICultureAvailabilityGuard _guard;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_catalog = Substitute.For<ICreatioCultureCatalog>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddTransient(_ => _catalog);
		containerBuilder.AddTransient(_ => _logger);
	}

	public override void Setup() {
		base.Setup();
		_catalog.GetCultures().Returns([
			new CreatioCulture("en-US", true), new CreatioCulture("es-ES", false), new CreatioCulture("de-DE", true)
		]);
		_guard = Container.GetRequiredService<ICultureAvailabilityGuard>();
	}

	public override void TearDown() {
		_catalog.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("A culture that is not a SysCulture row is refused with the same message localize-page uses: the Languages section and the available cultures in server order.")]
	public void EnsureAvailable_ShouldThrowLanguagesMessage_WhenCultureIsAbsent() {
		// Arrange
		string[] requested = ["en-US", "fi-FI"];

		// Act
		Action act = () => _guard.EnsureAvailable(requested);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage(string.Format(CultureMessages.CultureAbsentMessageFormat, "fi-FI", "en-US, es-ES, de-DE"),
				because: "entity caption writes and localize-page must tell the caller the same thing about a missing culture");
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
	}

	[Test]
	[Description("An inactive culture passes and is reported once through the logger warning, which reaches both the CLI [WAR] line and the MCP log messages.")]
	public void EnsureAvailable_ShouldWarn_WhenCultureIsInactive() {
		// Arrange
		string[] requested = ["en-US", "es-ES"];

		// Act
		Action act = () => _guard.EnsureAvailable(requested);

		// Assert
		act.Should().NotThrow(because: "an inactive culture is a SysCulture row, so the value is stored");
		_logger.Received(1).WriteWarning(
			string.Format(CultureMessages.CultureInactiveWarningFormat, "es-ES"));
	}

	[Test]
	[Description("Culture names match case-insensitively and are trimmed, and duplicates cost one warning, so 'es-es' and ' es-ES ' are the same inactive culture.")]
	public void EnsureAvailable_ShouldMatchCaseInsensitively_WhenCultureSpellingDiffers() {
		// Arrange
		string[] requested = ["es-es", " es-ES ", "DE-de"];

		// Act
		Action act = () => _guard.EnsureAvailable(requested);

		// Assert
		act.Should().NotThrow(because: "SysCulture names are compared ignoring case");
		_logger.Received(1).WriteWarning(
			string.Format(CultureMessages.CultureInactiveWarningFormat, "es-ES"));
	}

	[Test]
	[Description("The SysCulture table is read once per check, however many cultures are requested.")]
	public void EnsureAvailable_ShouldReadCatalogOnce_WhenSeveralCulturesAreRequested() {
		// Arrange
		string[] requested = ["en-US", "de-DE", "es-ES"];

		// Act
		_guard.EnsureAvailable(requested);

		// Assert
		_catalog.Received(1).GetCultures();
	}

	[Test]
	[Description("No culture to check means no SysCulture read, so writes that change no caption make no extra request.")]
	public void EnsureAvailable_ShouldNotReadCatalog_WhenNoCultureIsRequested() {
		// Arrange
		string[] requested = ["", "  "];

		// Act
		Action act = () => _guard.EnsureAvailable(requested);

		// Assert
		act.Should().NotThrow(because: "there is nothing to validate");
		_catalog.DidNotReceive().GetCultures();
	}

	[Test]
	[Description("A failed SysCulture read propagates instead of being treated as 'no cultures to check' (AC-6).")]
	public void EnsureAvailable_ShouldPropagate_WhenCatalogReadFails() {
		// Arrange
		_catalog.GetCultures().Returns(_ => throw new InvalidOperationException("SysCulture read failed"));

		// Act
		Action act = () => _guard.EnsureAvailable(["en-US"]);

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("SysCulture read failed",
			because: "an unverifiable culture must fail the write, never pass it silently");
	}

	[Test]
	[Description("Resolve returns the canonical SysCulture row and, for an inactive culture, the warning text instead of logging it, so the caller can put it into its result.")]
	public void Resolve_ShouldReturnCanonicalCultureAndWarning_WhenCultureIsInactive() {
		// Act
		CultureResolution resolution = _guard.Resolve(" ES-es ");

		// Assert
		resolution.Culture.Should().Be(new CreatioCulture("es-ES", false), because: "the name is canonicalized to SysCulture.Name");
		resolution.Warning.Should().Be(CultureMessages.FormatCultureInactive("es-ES"),
			because: "the inactive-culture warning is handed to the caller");
		_logger.DidNotReceive().WriteWarning(Arg.Any<string>());
	}

	[Test]
	[Description("Resolve returns no warning for an active culture.")]
	public void Resolve_ShouldReturnNoWarning_WhenCultureIsActive() {
		// Act
		CultureResolution resolution = _guard.Resolve("de-DE");

		// Assert
		resolution.Culture.Name.Should().Be("de-DE", because: "the culture exists");
		resolution.Warning.Should().BeNull(because: "an active culture needs no warning");
	}

	[Test]
	[Description("Resolve refuses an absent culture with the shared Languages-section message.")]
	public void Resolve_ShouldThrowLanguagesMessage_WhenCultureIsAbsent() {
		// Act
		Action act = () => _guard.Resolve("fi-FI");

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage(CultureMessages.FormatCultureAbsent("fi-FI",
				[new CreatioCulture("en-US", true), new CreatioCulture("es-ES", false), new CreatioCulture("de-DE", true)]),
				because: "every culture-writing command reports a missing culture the same way");
	}
}

