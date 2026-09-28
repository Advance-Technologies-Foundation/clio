using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.Localization;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// ENG-90576 story 4 (ADR D11): culture-aware section caption writes and the preservation of other cultures
/// around the platform's ApplicationSection update, which deletes them (F11).
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ApplicationSectionUpdateLocalizationTests {
	private const string SectionRow =
		"""{"Id":"section-id","ApplicationId":"app-id","Caption":"Orders","Code":"UsrOrders","Description":"Order workspace","EntitySchemaName":"UsrOrder","PackageId":"pkg-uid","SectionSchemaUId":"section-schema-uid","LogoId":"icon-old","IconBackground":"#111111","ClientTypeId":null}""";

	private IApplicationClientFactory _applicationClientFactory = null!;
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Structure", "NUnit1032:An IDisposable field/property should be Disposed in a TearDown method",
		Justification = "The system under test owns and disposes the factory-returned substitute.")]
	private IOwnedApplicationClient _applicationClient = null!;
	private IApplicationInfoService _applicationInfoService = null!;
	private ICaptionCultureResolver _captionCultureResolver = null!;
	private IApplicationSectionLocalizationClient _localizationClient = null!;
	private ICreatioCultureCatalog _cultureCatalog = null!;
	private EnvironmentSettings _environmentSettings = null!;
	private ApplicationSectionUpdateService _sut = null!;
	private List<string> _sectionUpdateBodies = null!;

	[SetUp]
	public void SetUp() {
		_applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		_applicationClient = Substitute.For<IOwnedApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_applicationInfoService = Substitute.For<IApplicationInfoService>();
		_captionCultureResolver = Substitute.For<ICaptionCultureResolver>();
		_localizationClient = Substitute.For<IApplicationSectionLocalizationClient>();
		_cultureCatalog = Substitute.For<ICreatioCultureCatalog>();
		_environmentSettings = new EnvironmentSettings { Uri = "https://example.invalid", IsNetCore = true };
		_sectionUpdateBodies = [];
		_applicationClientFactory.CreateOwnedEnvironmentClient(_environmentSettings).Returns(_applicationClient);
		serviceUrlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>(), Arg.Any<EnvironmentSettings>())
			.Returns(callInfo => $"https://example.invalid/{callInfo.ArgAt<ServiceUrlBuilder.KnownRoute>(0)}");
		_captionCultureResolver.Resolve(_environmentSettings, null).Returns("en-US");
		ICreatioCultureCatalogFactory cultureCatalogFactory = Substitute.For<ICreatioCultureCatalogFactory>();
		cultureCatalogFactory.Create(_applicationClient, _environmentSettings).Returns(_cultureCatalog);
		_applicationInfoService.GetApplicationInfo(_environmentSettings, null, "UsrOrdersApp").Returns(
			new ApplicationInfoResult("pkg-uid", "UsrOrdersApp", [], [], "app-id", "Orders App", "UsrOrdersApp", "8.3.0"));
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal)
					&& body.Contains("\"Code\"", StringComparison.Ordinal)))
			.Returns($$"""{"success":true,"rows":[{{SectionRow}}]}""");
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"columnValues\"", StringComparison.Ordinal)))
			.Returns(callInfo => {
				_sectionUpdateBodies.Add(callInfo.ArgAt<string>(1));
				return """{"success":true}""";
			});
		_localizationClient.RefreshSectionPackageBinding(
				Arg.Any<IApplicationClient>(), Arg.Any<EnvironmentSettings>(), Arg.Any<string>(), Arg.Any<string>())
			.Returns(true);
		_sut = new ApplicationSectionUpdateService(
			Substitute.For<ISettingsRepository>(),
			_applicationClientFactory,
			serviceUrlBuilder,
			_applicationInfoService,
			_captionCultureResolver,
			_localizationClient,
			new SectionLocalizationPlanner(_localizationClient),
			cultureCatalogFactory);
	}

	[Test]
	[Description("TC-U-40: a caption in a non-profile culture is written only through the localization table, with the current profile-culture caption the platform requires; the ApplicationSection update is not sent.")]
	public void UpdateSection_Should_WriteCaptionThroughLocalization_WhenCultureIsNotProfileCulture() {
		// Arrange
		GivenCulture("es-ES", active: true);
		CaptureWrites();
		GivenLocalizationReads([], [new SectionLocalizationRow("es-ES", "Pedidos", null, null)]);

		// Act
		ApplicationSectionUpdateResult result = _sut.UpdateSection(
			_environmentSettings,
			new ApplicationSectionUpdateRequest("UsrOrdersApp", "UsrOrders", Caption: "Pedidos", CaptionCulture: "es-ES"));

		// Assert
		_sectionUpdateBodies.Should().BeEmpty(
			because: "the ApplicationSection update writes the profile culture only and deletes the other cultures");
		_writes.Should().ContainSingle(because: "one localization write carries the target culture");
		_writes[0]["Caption"].Should().BeEquivalentTo(
			new Dictionary<string, string> { ["en-US"] = "Orders", ["es-ES"] = "Pedidos" },
			because: "the platform rejects a Caption write without the connected user's culture (F12)");
		result.CaptionCulture.Should().Be("es-ES", because: "the result names the culture the caption was written in");
		result.CaptionCultureValue.Should().Be("Pedidos", because: "the result returns the stored value read back");
		result.Section.Caption.Should().Be("Orders", because: "the profile-culture caption is unchanged");
		_localizationClient.Received(1).RefreshSectionPackageBinding(
			_applicationClient, _environmentSettings, "pkg-uid", "UsrOrders");
	}

	[Test]
	[Description("TC-U-41: an icon-only update writes back the section's non-default cultures that the platform deletes on every ApplicationSection update (F11).")]
	public void UpdateSection_Should_RestoreOtherCultures_AfterIconOnlyUpdate() {
		// Arrange
		SectionLocalizationRow spanish = new("es-ES", "Pedidos", "Espacio de pedidos", null);
		CaptureWrites();
		GivenLocalizationReads([spanish], [spanish]);

		// Act
		ApplicationSectionUpdateResult result = _sut.UpdateSection(
			_environmentSettings,
			new ApplicationSectionUpdateRequest("UsrOrdersApp", "UsrOrders", IconBackground: "#247EE5"));

		// Assert
		_sectionUpdateBodies.Should().ContainSingle(because: "the icon still goes through the ApplicationSection update");
		_writes.Should().ContainSingle(because: "the deleted cultures are written back in one localization write");
		_writes[0]["Caption"].Should().BeEquivalentTo(
			new Dictionary<string, string> { ["es-ES"] = "Pedidos", ["en-US"] = "Orders" },
			because: "the Spanish title is restored together with the required profile-culture title");
		_writes[0]["Description"].Should().BeEquivalentTo(
			new Dictionary<string, string> { ["es-ES"] = "Espacio de pedidos" },
			because: "the Spanish description is restored too; the platform deletes whole rows");
		result.PreservedCultures.Should().Equal(["es-ES"], because: "the result lists the kept cultures");
		result.CaptionCulture.Should().BeNull(because: "no caption was sent");
		_cultureCatalog.DidNotReceiveWithAnyArgs().Find(default!);
	}

	[Test]
	[Description("TC-U-42: a culture that is not a SysCulture row fails before any request, naming the Languages section and the available cultures.")]
	public void UpdateSection_Should_Fail_WhenCultureIsAbsentFromSysCulture() {
		// Arrange
		_cultureCatalog.Find("fi-FI").Returns(new CultureLookupResult(
			null!, [new CreatioCulture("en-US", true), new CreatioCulture("es-ES", false)]));

		// Act
		Action action = () => _sut.UpdateSection(
			_environmentSettings,
			new ApplicationSectionUpdateRequest("UsrOrdersApp", "UsrOrders", Caption: "Tilaukset", CaptionCulture: "fi-FI"));

		// Assert
		action.Should().Throw<InvalidOperationException>(
				because: "the platform would drop the value and still report success (F12)")
			.Which.Message.Should().Contain("Languages section",
				because: "the error must tell the agent where to add the culture")
			.And.Contain("Available: en-US, es-ES", because: "the error must list the cultures that exist");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
		_localizationClient.DidNotReceiveWithAnyArgs().WriteLocalizations(default!, default!, default!, default!);
	}

	[Test]
	[Description("TC-U-43: an inactive culture is written and the result carries the inactive warning.")]
	public void UpdateSection_Should_WarnAndWrite_WhenCultureIsInactive() {
		// Arrange
		GivenCulture("es-ES", active: false);
		CaptureWrites();
		GivenLocalizationReads([], [new SectionLocalizationRow("es-ES", "Pedidos", null, null)]);

		// Act
		ApplicationSectionUpdateResult result = _sut.UpdateSection(
			_environmentSettings,
			new ApplicationSectionUpdateRequest("UsrOrdersApp", "UsrOrders", Caption: "Pedidos", CaptionCulture: "es-ES"));

		// Assert
		_writes.Should().ContainSingle(because: "the platform stores values for inactive cultures (F4)");
		result.Warnings.Should().ContainSingle(warning => warning.Contains("inactive", StringComparison.Ordinal),
			because: "users cannot select the culture until it is activated");
	}

	[Test]
	[Description("TC-U-44: caption-culture without caption fails validation before any request.")]
	public void UpdateSection_Should_Reject_CaptionCultureWithoutCaption() {
		// Arrange
		ApplicationSectionUpdateRequest request = new(
			"UsrOrdersApp", "UsrOrders", IconBackground: "#247EE5", CaptionCulture: "es-ES");

		// Act
		Action action = () => _sut.UpdateSection(_environmentSettings, request);

		// Assert
		action.Should().Throw<ArgumentException>(because: "caption-culture only says which culture the caption is in")
			.WithMessage("caption-culture requires caption.", because: "the error must name the missing argument");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
	}

	[Test]
	[Description("TC-U-45: when the read-back target value does not match the requested caption the command fails and names the culture.")]
	public void UpdateSection_Should_Fail_WhenTargetCultureIsNotStored() {
		// Arrange
		GivenCulture("es-ES", active: true);
		CaptureWrites();
		GivenLocalizationReads([], []);

		// Act
		Action action = () => _sut.UpdateSection(
			_environmentSettings,
			new ApplicationSectionUpdateRequest("UsrOrdersApp", "UsrOrders", Caption: "Pedidos", CaptionCulture: "es-ES"));

		// Assert
		action.Should().Throw<InvalidOperationException>(
				because: "a success answer is not proof that the value was stored")
			.Which.Message.Should().Contain("Caption [es-ES]", because: "the error must name the missing value");
	}

	[Test]
	[Description("TC-U-46: a missing package data binding or a failed binding re-save is reported as a warning; the stored translation is still a success.")]
	public void UpdateSection_Should_Warn_WhenPackageBindingIsMissingOrFails() {
		// Arrange
		GivenCulture("es-ES", active: true);
		CaptureWrites();
		GivenLocalizationReads(
			[], [new SectionLocalizationRow("es-ES", "Pedidos", null, null)],
			[], [new SectionLocalizationRow("es-ES", "Pedidos", null, null)]);
		_localizationClient.RefreshSectionPackageBinding(
				Arg.Any<IApplicationClient>(), Arg.Any<EnvironmentSettings>(), Arg.Any<string>(), Arg.Any<string>())
			.Returns(false, true);
		ApplicationSectionUpdateRequest request = new("UsrOrdersApp", "UsrOrders", Caption: "Pedidos", CaptionCulture: "es-ES");

		// Act
		ApplicationSectionUpdateResult missing = _sut.UpdateSection(_environmentSettings, request);
		_localizationClient.RefreshSectionPackageBinding(
				Arg.Any<IApplicationClient>(), Arg.Any<EnvironmentSettings>(), Arg.Any<string>(), Arg.Any<string>())
			.Throws(new InvalidOperationException("SaveSchema failed: locked"));
		ApplicationSectionUpdateResult failed = _sut.UpdateSection(_environmentSettings, request);

		// Assert
		missing.Warnings.Should().ContainSingle(warning => warning.Contains("SysModule_UsrOrders' was not found", StringComparison.Ordinal),
			because: "the agent must know the translation is not part of the package data");
		failed.Warnings.Should().ContainSingle(warning => warning.Contains("SaveSchema failed: locked", StringComparison.Ordinal),
			because: "the re-save failure reason must reach the agent without failing the stored write");
	}

	[Test]
	[Description("TC-U-47: the culture is written under the SysCulture spelling returned by the catalog (de-de -> de-DE).")]
	public void UpdateSection_Should_UseCatalogSpelling_ForCultureKey() {
		// Arrange
		_cultureCatalog.Find("de-de").Returns(new CultureLookupResult(
			new CreatioCulture("de-DE", true), [new CreatioCulture("de-DE", true)]));
		CaptureWrites();
		GivenLocalizationReads([], [new SectionLocalizationRow("de-DE", "Bestellungen", null, null)]);

		// Act
		ApplicationSectionUpdateResult result = _sut.UpdateSection(
			_environmentSettings,
			new ApplicationSectionUpdateRequest("UsrOrdersApp", "UsrOrders", Caption: "Bestellungen", CaptionCulture: "de-de"));

		// Assert
		_writes[0]["Caption"].Keys.Should().Contain("de-DE", because: "the value is keyed by the canonical culture name")
			.And.NotContain("de-de", because: "a lower-case key would not match the readback");
		result.CaptionCulture.Should().Be("de-DE", because: "the result reports the canonical culture name");
	}

	[Test]
	[Description("A caption written in en-US under a non-en-US profile is stored in SysModule itself, which the localization readback cannot see: the call succeeds with a warning instead of failing the readback.")]
	public void UpdateSection_Should_Warn_WhenDefaultCultureIsWrittenUnderAnotherProfile() {
		// Arrange
		_captionCultureResolver.Resolve(_environmentSettings, null).Returns("uk-UA");
		GivenCulture("en-US", active: true);
		CaptureWrites();
		SectionLocalizationRow ukrainian = new("uk-UA", "Замовлення", null, null);
		GivenLocalizationReads([ukrainian], [ukrainian]);

		// Act
		ApplicationSectionUpdateResult result = _sut.UpdateSection(
			_environmentSettings,
			new ApplicationSectionUpdateRequest("UsrOrdersApp", "UsrOrders", Caption: "Orders", CaptionCulture: "en-US"));

		// Assert
		_writes.Should().ContainSingle(because: "the English title is written through the localization write");
		_writes[0]["Caption"].Should().ContainKey("en-US", because: "the target culture is written")
			.And.ContainKey("uk-UA", because: "the platform requires the connected user's culture on a Caption write");
		result.CaptionCultureValue.Should().Be("Orders", because: "the requested value is reported when it cannot be read back");
		result.Warnings.Should().ContainSingle(warning => warning.Contains("could not be read back", StringComparison.Ordinal),
			because: "the agent must verify the default-culture title in Creatio");
	}

	[Test]
	[Description("An explicit caption-culture is looked up in SysCulture only: a name .NET rejects (xx-XX) still gets the message that names the Languages section and lists the environment's cultures.")]
	public void UpdateSection_Should_ReportLanguagesSection_WhenOverrideCultureIsUnknownToDotNet() {
		// Arrange
		_cultureCatalog.Find("xx-XX").Returns(new CultureLookupResult(
			null!, [new CreatioCulture("en-US", true), new CreatioCulture("de-DE", true)]));

		// Act
		Action action = () => _sut.UpdateSection(
			_environmentSettings,
			new ApplicationSectionUpdateRequest("UsrOrdersApp", "UsrOrders", Caption: "X", CaptionCulture: " xx-XX "));

		// Assert
		action.Should().Throw<InvalidOperationException>(
				because: "SysCulture is the one source of truth for an explicit culture")
			.Which.Message.Should().Contain("Culture 'xx-XX' is not available",
				because: "the trimmed name the caller sent must be named")
			.And.Contain("Languages section", because: "the agent must learn where to add the culture")
			.And.Contain("Available: en-US, de-DE", because: "the agent must see the cultures that exist");
		_captionCultureResolver.DidNotReceive().Resolve(_environmentSettings, Arg.Is<string>(value => value != null));
		_localizationClient.DidNotReceiveWithAnyArgs().WriteLocalizations(default!, default!, default!, default!);
	}

	[Test]
	[Description("When the write-back fails after the ApplicationSection update already deleted the other cultures (F11), the error carries every snapshot value so the caller can re-send them.")]
	public void UpdateSection_Should_QuoteSnapshot_WhenRestoreWriteFails() {
		// Arrange
		SectionLocalizationRow spanish = new("es-ES", "Pedidos", "Espacio de pedidos", "Cabecera");
		SectionLocalizationRow german = new("de-DE", "Bestellungen", null, null);
		GivenLocalizationReads([spanish, german]);
		_localizationClient.When(client => client.WriteLocalizations(
				Arg.Any<IApplicationClient>(), Arg.Any<EnvironmentSettings>(), Arg.Any<string>(),
				Arg.Any<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>()))
			.Do(_ => throw new InvalidOperationException("UpdateLocalizationQuery failed: timeout"));

		// Act
		Action action = () => _sut.UpdateSection(
			_environmentSettings,
			new ApplicationSectionUpdateRequest("UsrOrdersApp", "UsrOrders", IconBackground: "#247EE5"));

		// Assert
		string message = action.Should().Throw<InvalidOperationException>(
				because: "the translations are gone from the environment and the call must not report success")
			.Which.Message;
		message.Should().Contain("UpdateLocalizationQuery failed: timeout", because: "the server reason must be kept")
			.And.Contain("es-ES: Caption='Pedidos', Description='Espacio de pedidos', ModuleHeader='Cabecera'",
				because: "the snapshot is the only remaining copy of the Spanish values")
			.And.Contain("de-DE: Caption='Bestellungen'", because: "every deleted culture must be listed");
		_sectionUpdateBodies.Should().ContainSingle(because: "the failure happened after the ApplicationSection update");
	}

	[Test]
	[Description("An icon-only update under a de-DE profile restores and verifies the de-DE caption: no caption went through the ApplicationSection update, so that update did not write it.")]
	public void UpdateSection_Should_RestoreProfileCultureCaption_AfterIconOnlyUpdateUnderNonDefaultProfile() {
		// Arrange
		_captionCultureResolver.Resolve(_environmentSettings, null).Returns("de-DE");
		SectionLocalizationRow german = new("de-DE", "Bestellungen", null, null);
		CaptureWrites();
		GivenLocalizationReads([german], [german]);

		// Act
		ApplicationSectionUpdateResult result = _sut.UpdateSection(
			_environmentSettings,
			new ApplicationSectionUpdateRequest("UsrOrdersApp", "UsrOrders", IconBackground: "#247EE5"));

		// Assert
		_writes.Should().ContainSingle(because: "the deleted profile-culture row is written back");
		_writes[0]["Caption"].Should().BeEquivalentTo(
			new Dictionary<string, string> { ["de-DE"] = "Bestellungen" },
			because: "the de-DE caption is restored from the snapshot, once");
		result.PreservedCultures.Should().Equal(["de-DE"], because: "the restored caption is verified and reported as kept");
	}

	[Test]
	[Description("After an icon-only update the readback fails the call when a snapshot culture is missing.")]
	public void UpdateSection_Should_Fail_WhenRestoredCultureIsMissingAfterIconOnlyUpdate() {
		// Arrange
		SectionLocalizationRow spanish = new("es-ES", "Pedidos", null, null);
		CaptureWrites();
		GivenLocalizationReads([spanish], []);

		// Act
		Action action = () => _sut.UpdateSection(
			_environmentSettings,
			new ApplicationSectionUpdateRequest("UsrOrdersApp", "UsrOrders", IconBackground: "#247EE5"));

		// Assert
		action.Should().Throw<InvalidOperationException>(
				because: "a success answer of the write is not proof the translation came back")
			.Which.Message.Should().Contain("Caption [es-ES]", because: "the error must name the missing value");
	}

	private readonly List<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> _writes = [];

	private void CaptureWrites() {
		_writes.Clear();
		_localizationClient.When(client => client.WriteLocalizations(
				Arg.Any<IApplicationClient>(), Arg.Any<EnvironmentSettings>(), Arg.Any<string>(),
				Arg.Any<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>()))
			.Do(callInfo => _writes.Add(callInfo.ArgAt<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>(3)));
	}

	private void GivenCulture(string name, bool active) {
		_cultureCatalog.Find(name).Returns(new CultureLookupResult(
			new CreatioCulture(name, active), [new CreatioCulture("en-US", true), new CreatioCulture(name, active)]));
	}

	private void GivenLocalizationReads(params IReadOnlyList<SectionLocalizationRow>[] reads) {
		IReadOnlyList<SectionLocalizationRow> first = reads[0];
		IReadOnlyList<SectionLocalizationRow>[] rest = reads.Skip(1).ToArray();
		_localizationClient.ReadLocalizations(Arg.Any<IApplicationClient>(), Arg.Any<EnvironmentSettings>(), "section-id")
			.Returns(first, rest);
	}
}
