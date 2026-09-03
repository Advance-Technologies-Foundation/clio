using System;
using Clio;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.MobilePageConverter;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class MobileSectionRegistrationProbeTests {

	private const string PageUId = "11111111-1111-1111-1111-111111111111";
	private const string SysModuleId = "22222222-2222-2222-2222-222222222222";
	private const string MobileClientTypeId = "33333333-3333-3333-3333-333333333333";
	private const string WebClientTypeId = "44444444-4444-4444-4444-444444444444";
	private const string MobileWorkplaceId = "55555555-5555-5555-5555-555555555555";
	private const string WebWorkplaceId = "66666666-6666-6666-6666-666666666666";
	private const string RegisteredMobilePageUId = "77777777-7777-7777-7777-777777777777";

	/// <summary>Builds a resolver whose OData GETs are answered by <paramref name="route"/> (keyed on the URL).</summary>
	private static IToolCommandResolver Resolver(Func<string, string> route) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ci => route(ci.Arg<string>()));

		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<string>()).Returns(ci => ci.Arg<string>());

		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		return resolver;
	}

	private static string Value(string innerJsonRows) => $"{{\"value\":[{innerJsonRows}]}}";

	private static string RouteFor(string url, string sysModuleRows, string mobileSchemaUId = null) {
		// Match on the entity path "odata/<Entity>?" — SysWorkplace's $select contains the substring
		// "SysApplicationClientTypeId", so a loose Contains would misroute it.
		if (url.Contains("odata/SysModuleInWorkplace?")) {
			return Value($"{{\"SysWorkplaceId\":\"{MobileWorkplaceId}\"}}");
		}
		if (url.Contains("odata/SysApplicationClientType?")) {
			return Value($"{{\"Id\":\"{MobileClientTypeId}\",\"Name\":\"Mobile\"}}");
		}
		if (url.Contains("odata/SysWorkplace?")) {
			return Value(
				$"{{\"Id\":\"{MobileWorkplaceId}\",\"Name\":\"Mobile workplace\",\"SysApplicationClientTypeId\":\"{MobileClientTypeId}\"}}," +
				$"{{\"Id\":\"{WebWorkplaceId}\",\"Name\":\"Main\",\"SysApplicationClientTypeId\":\"{WebClientTypeId}\"}}");
		}
		if (url.Contains("odata/SysModule?")) {
			return sysModuleRows;
		}
		return Value("");
	}

	[Test]
	[Description("When the page is a section without a mobile page, the probe reports it, the SysModule id, available mobile workplaces, and a set-MobileSectionSchemaUId action.")]
	public void Probe_SectionWithoutMobilePage_ReportsRegistrationFacts() {
		string moduleRow = Value(
			$"{{\"Id\":\"{SysModuleId}\",\"Code\":\"UsrTestObjApp\",\"Caption\":\"Test\",\"SectionSchemaUId\":\"{PageUId}\"}}");
		IToolCommandResolver resolver = Resolver(url => RouteFor(url, moduleRow));

		SectionRegistrationInfo info = MobileSectionRegistrationProbe.Probe(
			resolver, "env", null, null, null, PageUId, isFormPage: false);

		info.ProbeOk.Should().BeTrue();
		info.SourcePageIsSection.Should().BeTrue();
		info.SysModuleId.Should().Be(SysModuleId);
		info.SectionCode.Should().Be("UsrTestObjApp");
		info.MobileSectionRegistered.Should().BeFalse();
		info.AvailableMobileWorkplaces.Should().ContainSingle(w => w.Name == "Mobile workplace" && w.IsMobile);
		info.CurrentWorkplaces.Should().ContainSingle(w => w.Id == MobileWorkplaceId && w.ContainsSection);
		info.RegistrationActions.Should().Contain(a => a.Contains("MobileSectionSchemaUId"));
	}

	[Test]
	[Description("When MobileSectionSchemaUId is already set, the probe flags it as already registered and surfaces the value.")]
	public void Probe_SectionWithMobilePage_FlagsAlreadyRegistered() {
		string moduleRow = Value(
			$"{{\"Id\":\"{SysModuleId}\",\"Code\":\"UsrTestObjApp\",\"Caption\":\"Test\"," +
			$"\"SectionSchemaUId\":\"{PageUId}\",\"MobileSectionSchemaUId\":\"{RegisteredMobilePageUId}\"}}");
		IToolCommandResolver resolver = Resolver(url => RouteFor(url, moduleRow));

		SectionRegistrationInfo info = MobileSectionRegistrationProbe.Probe(
			resolver, "env", null, null, null, PageUId, isFormPage: false);

		info.SourcePageIsSection.Should().BeTrue();
		info.MobileSectionRegistered.Should().BeTrue();
		info.MobileSectionSchemaUId.Should().Be(RegisteredMobilePageUId);
		info.RegistrationActions.Should().Contain(a => a.Contains("already has a mobile page"));
	}

	[Test]
	[Description("When no SysModule row references the page, the probe reports it is not a section (probe still ok).")]
	public void Probe_PageNotASection_ReportsNotSection() {
		IToolCommandResolver resolver = Resolver(url => RouteFor(url, Value("")));

		SectionRegistrationInfo info = MobileSectionRegistrationProbe.Probe(
			resolver, "env", null, null, null, PageUId, isFormPage: false);

		info.ProbeOk.Should().BeTrue();
		info.SourcePageIsSection.Should().BeFalse();
		info.SysModuleId.Should().BeNull();
		info.Note.Should().Contain("not registered as a section");
	}

	[Test]
	[Description("A form page that is not a section still gets the manual default-mobile-edit-page (MobileRelatedPage) advice.")]
	public void Probe_FormPageNotSection_AddsManualEditPageAction() {
		IToolCommandResolver resolver = Resolver(url => RouteFor(url, Value("")));

		SectionRegistrationInfo info = MobileSectionRegistrationProbe.Probe(
			resolver, "env", null, null, null, PageUId, isFormPage: true);

		info.IsFormPage.Should().BeTrue();
		info.RegistrationActions.Should().Contain(a => a.Contains("MobileRelatedPage"));
	}

	[Test]
	[Description("When the environment cannot be queried, the probe degrades to probeOk=false with a note and never throws.")]
	public void Probe_WhenQueryFails_DegradesGracefully() {
		IToolCommandResolver resolver = Resolver(_ => throw new InvalidOperationException("network down"));

		SectionRegistrationInfo info = MobileSectionRegistrationProbe.Probe(
			resolver, "env", null, null, null, PageUId, isFormPage: false);

		info.ProbeOk.Should().BeFalse();
		info.Note.Should().Contain("Could not query the environment");
	}

	[Test]
	[Description("With no resolver or page UId the probe returns a non-throwing, not-probed result.")]
	public void Probe_WithoutInputs_ReturnsNotProbed() {
		SectionRegistrationInfo info = MobileSectionRegistrationProbe.Probe(
			commandResolver: null, "env", null, null, null, pageSchemaUId: null, isFormPage: false);

		info.ProbeOk.Should().BeFalse();
		info.SourcePageIsSection.Should().BeFalse();
	}

	[Test]
	[Description("A non-GUID page schema UId is rejected by the Guid.TryParse guard before any OData request is issued " +
		"— proving the injected filter never reaches the wire (the guard is the only thing keeping a non-GUID value " +
		"out of the unquoted OData $filter).")]
	public void Probe_ShouldReturnNotProbed_WhenPageSchemaUIdIsNotAGuid() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);

		SectionRegistrationInfo info = MobileSectionRegistrationProbe.Probe(
			resolver, "env", null, null, null, pageSchemaUId: "x or 1 eq 1", isFormPage: false);

		info.ProbeOk.Should().BeFalse();
		info.Note.Should().Contain("not a valid GUID");
		client.DidNotReceive().ExecuteGetRequest(
			Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	// ── ProbeByEntity (ENG-95730: legacy wizard settings are not SysModule pages; the section is found by entity) ──

	private const string EntityUId = "88888888-8888-8888-8888-888888888888";
	private const string ModuleEntityId = "99999999-9999-9999-9999-999999999999";

	/// <summary>Resolver whose OData GETs (root entity schema lookup included) are answered by <paramref name="odataRoute"/>.</summary>
	private static IToolCommandResolver EntityResolver(Func<string, string> odataRoute, string entityUId = EntityUId) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(ci => {
				string url = ci.Arg<string>();
				if (url.Contains("odata/SysSchema?")) {
					url.Should().Contain("ExtendParent eq false",
						because: "SysModuleEntity references the ROOT entity schema UId, so only the non-replacing row may be matched");
					return entityUId is null ? Value("") : Value($"{{\"UId\":\"{entityUId}\"}}");
				}
				return odataRoute(url);
			});
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		return resolver;
	}

	private static string EntityRouteFor(string url, string sysModuleRows, string moduleEntityRows = null) {
		if (url.Contains("odata/SysModuleEntity?")) {
			return moduleEntityRows ?? Value($"{{\"Id\":\"{ModuleEntityId}\"}}");
		}
		return RouteFor(url, sysModuleRows);
	}

	[Test]
	[Description("ProbeByEntity finds the section through SysModuleEntity -> SysModule for the entity, reports its registration facts and the mobile workplaces, and records the entity on the result.")]
	public void ProbeByEntity_ShouldReportSection_WhenEntityHasOneModule() {
		// Arrange
		string moduleRow = Value($"{{\"Id\":\"{SysModuleId}\",\"Code\":\"Order\",\"Caption\":\"Orders\"}}");
		IToolCommandResolver resolver = EntityResolver(url => EntityRouteFor(url, moduleRow));

		// Act
		SectionRegistrationInfo info = MobileSectionRegistrationProbe.ProbeByEntity(resolver, "env", null, null, null, "Order");

		// Assert
		info.ProbeOk.Should().BeTrue(because: "every query answered");
		info.SourcePageIsSection.Should().BeTrue(because: "a SysModule row exists for the entity");
		info.SysModuleId.Should().Be(SysModuleId, because: "the odata-update target is surfaced");
		info.SectionCode.Should().Be("Order", because: "the section code is surfaced");
		info.EntitySchemaName.Should().Be("Order", because: "the entity the lookup ran for is recorded");
		info.IsFormPage.Should().BeFalse(because: "a legacy list settings source is never a form page");
		info.MobileSectionRegistered.Should().BeFalse(because: "MobileSectionSchemaUId is empty");
		info.AvailableMobileWorkplaces.Should().ContainSingle(w => w.IsMobile, because: "the shared workplace tail runs for the entity probe too");
		info.RegistrationActions.Should().Contain(a => a.Contains("MobileSectionSchemaUId"), because: "the caller is told how to register the mobile page");
	}

	[Test]
	[Description("When the entity has several sections the first is probed and the note names all of them so the user decides which section receives the mobile page.")]
	public void ProbeByEntity_ShouldNoteAllSections_WhenEntityHasSeveralModules() {
		// Arrange
		string moduleRows = Value(
			$"{{\"Id\":\"{SysModuleId}\",\"Code\":\"Activity\",\"Caption\":\"Activities\"}}," +
			$"{{\"Id\":\"{RegisteredMobilePageUId}\",\"Code\":\"Calendar\",\"Caption\":\"Calendar\"}}");
		IToolCommandResolver resolver = EntityResolver(url => EntityRouteFor(url, moduleRows));

		// Act
		SectionRegistrationInfo info = MobileSectionRegistrationProbe.ProbeByEntity(resolver, "env", null, null, null, "Activity");

		// Assert
		info.ProbeOk.Should().BeTrue(because: "the probe completed");
		info.SysModuleId.Should().Be(SysModuleId, because: "the first section is reported");
		info.Note.Should().Contain("2 sections").And.Contain("Calendar", because: "the alternatives are named for the user's decision");
	}

	[Test]
	[Description("When no SysModuleEntity references the entity the probe reports that no section exists (probe still ok) with a register-manually action.")]
	public void ProbeByEntity_ShouldReportNoSection_WhenEntityHasNoModule() {
		// Arrange
		IToolCommandResolver resolver = EntityResolver(url => EntityRouteFor(url, Value(""), moduleEntityRows: Value("")));

		// Act
		SectionRegistrationInfo info = MobileSectionRegistrationProbe.ProbeByEntity(resolver, "env", null, null, null, "UsrThing");

		// Assert
		info.ProbeOk.Should().BeTrue(because: "the environment answered");
		info.SourcePageIsSection.Should().BeFalse(because: "no section is registered for the entity");
		info.Note.Should().Contain("No section", because: "the result explains what was (not) found");
		info.RegistrationActions.Should().ContainSingle(a => a.Contains("register one manually"), because: "the caller learns the section must be created first");
	}

	[Test]
	[Description("When no root entity schema of that name exists the probe degrades to probeOk=false naming the entity, and never throws.")]
	public void ProbeByEntity_ShouldDegrade_WhenEntityIsUnknown() {
		// Arrange
		IToolCommandResolver resolver = EntityResolver(url => EntityRouteFor(url, Value("")), entityUId: null);

		// Act
		SectionRegistrationInfo info = MobileSectionRegistrationProbe.ProbeByEntity(resolver, "env", null, null, null, "Nope");

		// Assert
		info.ProbeOk.Should().BeFalse(because: "the entity UId could not be resolved");
		info.Note.Should().Contain("Nope", because: "the note names the entity");
	}

	[Test]
	[Description("An entity name that is not a plain identifier is rejected before any OData request is issued, so it can never reach the string-literal filter.")]
	public void ProbeByEntity_ShouldReturnNotProbed_WhenEntityNameIsNotAnIdentifier() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);

		// Act
		SectionRegistrationInfo info = MobileSectionRegistrationProbe.ProbeByEntity(resolver, "env", null, null, null, "Order' or 1 eq 1");

		// Assert
		info.ProbeOk.Should().BeFalse(because: "a non-identifier is refused");
		info.Note.Should().Contain("not a valid entity schema name", because: "the note explains the refusal");
		client.DidNotReceive().ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Without a resolver or an entity name the entity probe returns a non-throwing, not-probed result.")]
	public void ProbeByEntity_ShouldReturnNotProbed_WhenInputsAreMissing() {
		// Act
		SectionRegistrationInfo info = MobileSectionRegistrationProbe.ProbeByEntity(null, "env", null, null, null, "Order");

		// Assert
		info.ProbeOk.Should().BeFalse(because: "nothing could be queried");
		info.EntitySchemaName.Should().Be("Order", because: "the entity is still recorded");
	}
}
