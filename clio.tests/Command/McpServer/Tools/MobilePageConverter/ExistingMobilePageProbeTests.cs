using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.MobilePageConverter;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter;

/// <summary>
/// Exercises <see cref="ExistingMobilePageProbe.Probe"/> — the public entry point
/// <c>MobilePageConversionGuideTool</c> actually calls — rather than its internal
/// <c>ProbeSectionMobilePage</c>/<c>ProbeSourceEntityDefaultMobilePage</c> helpers directly: the
/// <c>MobileSectionRegistered</c>/<c>MobileSectionSchemaUId</c> guard, the <c>IsFormPage</c> guard, the
/// self-match exclusion, and both matches surfacing together are all orchestration <see cref="Probe"/>
/// alone owns and the two helpers cannot demonstrate on their own.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ExistingMobilePageProbeTests {

	private const string PackageUId = "11111111-1111-1111-1111-111111111111";
	private const string SectionSchemaUId = "22222222-2222-2222-2222-222222222222";
	private const string EntityDefaultPageSchemaUId = "33333333-3333-3333-3333-333333333333";
	private const string EntitySchemaUId = "44444444-4444-4444-4444-444444444444";
	private const string MobileRoot = "BaseMobilePageTemplate";

	/// <summary>
	/// A stand-in environment whose DataService SelectQuery answers come from <paramref name="select"/>,
	/// keyed on the serialized query so a test can answer the page and object lookups differently.
	/// </summary>
	private sealed record EnvironmentStub {

		public IToolCommandResolver Resolver { get; init; }

		public IApplicationClient Client { get; init; }

		public IAddonSchemaDesignerClient AddonClient { get; init; }
	}

	private static EnvironmentStub Environment(Func<string, string> select, string addonMetaData = null) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(callInfo => select(callInfo.ArgAt<string>(1)));

		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<string>()).Returns(callInfo => callInfo.Arg<string>());
		urlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns("/DataService/json/SyncReply/SelectQuery");

		IAddonSchemaDesignerClient addonClient = Substitute.For<IAddonSchemaDesignerClient>();
		addonClient.GetSchema(Arg.Any<AddonGetRequestDto>())
			.Returns(_ => new AddonSchemaDto { MetaData = addonMetaData ?? string.Empty });

		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		resolver.Resolve<IAddonSchemaDesignerClient>(Arg.Any<EnvironmentOptions>()).Returns(addonClient);

		return new EnvironmentStub { Resolver = resolver, Client = client, AddonClient = addonClient };
	}

	/// <summary>A successful SelectQuery envelope carrying <paramref name="rows"/> (raw JSON objects).</summary>
	private static string Rows(params string[] rows) => $"{{\"success\":true,\"rows\":[{string.Join(",", rows)}]}}";

	private static string PageRow(string name, string parentName, string uId) =>
		$"{{\"Name\":\"{name}\",\"UId\":\"{uId}\",\"ParentName\":{(parentName is null ? "null" : $"\"{parentName}\"")}}}";

	private static string EntityRow(string name, string uId = EntitySchemaUId) =>
		$"{{\"Name\":\"{name}\",\"UId\":\"{uId}\",\"ExtendParent\":false}}";

	/// <summary>Routes a serialized SelectQuery to the page-schema or object-schema answer.</summary>
	private static Func<string, string> Route(string pageRows, string entityRows) =>
		query => query.Contains("EntitySchemaManager") ? entityRows : pageRows;

	/// <summary>A modelConfig whose single (primary) data source binds the page to <paramref name="entityName"/>.</summary>
	private static JsonObject SourcePageBoundTo(string entityName) =>
		JsonNode.Parse($$"""
		{
		  "primaryDataSourceName": "PDS",
		  "dataSources": { "PDS": { "type": "crt.EntityDataSource",
		    "config": { "entitySchemaName": "{{entityName}}" } } }
		}
		""").AsObject();

	private static SectionRegistrationInfo RegisteredSection(string mobileSectionSchemaUId) => new() {
		MobileSectionRegistered = true, MobileSectionSchemaUId = mobileSectionSchemaUId, IsFormPage = false, ProbeOk = true
	};

	private static ExistingMobilePageProbeRequest Request(
		SectionRegistrationInfo sectionRegistration = null, bool isFormPage = false,
		JsonObject modelConfig = null, string pagePackageUId = PackageUId,
		string targetName = "Unrelated_MobileFormPage") =>
		new(sectionRegistration, isFormPage, modelConfig, pagePackageUId, targetName);

	private static List<ExistingMobilePageInfo> Probe(EnvironmentStub environment, ExistingMobilePageProbeRequest request) =>
		ExistingMobilePageProbe.Probe(environment.Resolver, "env", null, null, null, request);

	// ── Entity-default check (IsFormPage) ───────────────────────────────────────────────────────

	[Test]
	[Description("A form page's bound entity already has a default mobile edit page: Probe reports it for the reuse-vs-convert check.")]
	public void Probe_FormPageEntityHasDefault_ReportsExistingEntityPage() {
		// Arrange — entity name -> UId (EntitySchemaManager branch), mobile add-on declares an untyped default,
		// and the reverse UId -> name lookup (page branch) resolves that default page's name.
		EnvironmentStub environment = Environment(
			Route(Rows(PageRow("LeadProduct_MobileFormPage", MobileRoot, EntityDefaultPageSchemaUId)), Rows(EntityRow("LeadProduct"))),
			addonMetaData: $"{{\"Pages\":[{{\"PageSchemaUId\":\"{EntityDefaultPageSchemaUId}\",\"IsDefault\":true}}]}}");

		// Act
		List<ExistingMobilePageInfo> matches = Probe(environment, Request(isFormPage: true, modelConfig: SourcePageBoundTo("LeadProduct")));

		// Assert
		ExistingMobilePageInfo match = matches.Should()
			.ContainSingle(because: "the bound entity's MobileRelatedPage add-on declares an untyped default").Subject;
		match.SchemaName.Should().Be("LeadProduct_MobileFormPage");
		match.SchemaUId.Should().Be(EntityDefaultPageSchemaUId);
		match.Source.Should().Be(MobileActionTargetProbe.KindEntityDefaultMobilePage);
	}

	[Test]
	[Description("A form page's bound entity has no default mobile edit page: Probe reports nothing rather than guessing one.")]
	public void Probe_FormPageEntityHasNoDefault_ReportsNothing() {
		// Arrange
		EnvironmentStub environment = Environment(Route(Rows(), Rows(EntityRow("LeadProduct"))), addonMetaData: "{\"Pages\":[]}");

		// Act
		List<ExistingMobilePageInfo> matches = Probe(environment, Request(isFormPage: true, modelConfig: SourcePageBoundTo("LeadProduct")));

		// Assert
		matches.Should().BeEmpty(because: "the object has no default mobile page to reuse");
	}

	[Test]
	[Description("The bound entity's name cannot be resolved to a schema UId at all: Probe reports nothing and never reads the add-on.")]
	public void Probe_FormPageEntityUnresolvable_ReportsNothingWithoutReadingAddon() {
		// Arrange — no SysSchema row for the object at all.
		EnvironmentStub environment = Environment(Route(Rows(), Rows()));

		// Act
		List<ExistingMobilePageInfo> matches = Probe(environment, Request(isFormPage: true, modelConfig: SourcePageBoundTo("UnknownObject")));

		// Assert
		matches.Should().BeEmpty();
		environment.AddonClient.ReceivedCalls().Should().BeEmpty(because: "there is no entity UId to address the add-on read with");
	}

	[Test]
	[Description("A missing/unparseable page package UId means the add-on read can never be addressed: Probe reports nothing without touching the environment.")]
	public void Probe_UnparseablePagePackageUId_ReportsNothingWithoutReadingAnything() {
		// Arrange
		EnvironmentStub environment = Environment(Route(Rows(), Rows(EntityRow("LeadProduct"))));

		// Act
		List<ExistingMobilePageInfo> matches = Probe(environment,
			Request(isFormPage: true, modelConfig: SourcePageBoundTo("LeadProduct"), pagePackageUId: "not-a-guid"));

		// Assert
		matches.Should().BeEmpty();
		environment.Client.ReceivedCalls().Should().BeEmpty(because: "no read can be addressed without a package UId");
	}

	[Test]
	[Description("A THROW anywhere in the resolve-entity/read-addon/resolve-name chain degrades to no match rather than propagating — this probe never throws.")]
	public void Probe_FormPageThrowingRead_DegradesToNoMatches() {
		// Arrange
		EnvironmentStub environment = Environment(Route(Rows(), Rows(EntityRow("LeadProduct"))));
		environment.AddonClient.GetSchema(Arg.Any<AddonGetRequestDto>()).Returns(_ => throw new InvalidOperationException("boom"));

		// Act
		List<ExistingMobilePageInfo> matches = Probe(environment, Request(isFormPage: true, modelConfig: SourcePageBoundTo("LeadProduct")));

		// Assert
		matches.Should().BeEmpty();
	}

	[Test]
	[Description("A list/section source page (IsFormPage=false) never probes its bound entity's default mobile page, even though the environment would answer.")]
	public void Probe_NotFormPage_SkipsEntityCheckEntirely() {
		// Arrange — the stub would answer with a real default page, but nothing should ask it.
		EnvironmentStub environment = Environment(
			Route(Rows(PageRow("LeadProduct_MobileFormPage", MobileRoot, EntityDefaultPageSchemaUId)), Rows(EntityRow("LeadProduct"))),
			addonMetaData: $"{{\"Pages\":[{{\"PageSchemaUId\":\"{EntityDefaultPageSchemaUId}\",\"IsDefault\":true}}]}}");

		// Act
		List<ExistingMobilePageInfo> matches = Probe(environment, Request(isFormPage: false, modelConfig: SourcePageBoundTo("LeadProduct")));

		// Assert
		matches.Should().BeEmpty(because: "a list/section source page has no single bound entity edit page to check");
		environment.AddonClient.ReceivedCalls().Should().BeEmpty(because: "IsFormPage=false must skip the entity check entirely");
	}

	[Test]
	[Description("An entity's default mobile page that already carries the TARGET schema name is excluded: there is nothing to reuse-vs-convert for the page the conversion is itself producing.")]
	public void Probe_EntityDefaultIsTheConversionTarget_ExcludesItAsSelfMatch() {
		// Arrange
		EnvironmentStub environment = Environment(
			Route(Rows(PageRow("Lead_MobileFormPage", MobileRoot, EntityDefaultPageSchemaUId)), Rows(EntityRow("Lead"))),
			addonMetaData: $"{{\"Pages\":[{{\"PageSchemaUId\":\"{EntityDefaultPageSchemaUId}\",\"IsDefault\":true}}]}}");

		// Act
		List<ExistingMobilePageInfo> matches = Probe(environment,
			Request(isFormPage: true, modelConfig: SourcePageBoundTo("Lead"), targetName: "Lead_MobileFormPage"));

		// Assert
		matches.Should().BeEmpty(because: "the resolved default page IS the schema this run is about to create/update");
	}

	// ── Section check (SectionRegistration) ─────────────────────────────────────────────────────

	[Test]
	[Description("An already-registered section's mobile page UId resolves to its schema name for the reuse-vs-convert check.")]
	public void Probe_RegisteredSection_ReportsExistingSectionPage() {
		// Arrange — the reverse UId -> name lookup is the same non-EntitySchemaManager query family the
		// candidate batch (SchemaNameResolver.ResolveNames) uses, so it is routed through the page-rows side of Route.
		EnvironmentStub environment = Environment(Route(Rows(PageRow("UsrApp_MobileListPage", MobileRoot, SectionSchemaUId)), Rows()));

		// Act
		List<ExistingMobilePageInfo> matches = Probe(environment, Request(sectionRegistration: RegisteredSection(SectionSchemaUId)));

		// Assert
		ExistingMobilePageInfo match = matches.Should().ContainSingle().Subject;
		match.SchemaName.Should().Be("UsrApp_MobileListPage");
		match.SchemaUId.Should().Be(SectionSchemaUId);
		match.Source.Should().Be(ExistingMobilePageProbe.KindSection);
	}

	[Test]
	[Description("A registered section's mobile page UId does not resolve to any row: Probe reports nothing rather than fabricating a name.")]
	public void Probe_RegisteredSectionUnresolvableUId_ReportsNothing() {
		// Arrange
		EnvironmentStub environment = Environment(Route(Rows(), Rows()));

		// Act
		List<ExistingMobilePageInfo> matches = Probe(environment, Request(sectionRegistration: RegisteredSection(SectionSchemaUId)));

		// Assert
		matches.Should().BeEmpty();
	}

	[Test]
	[Description("A registered section whose mobile page UId is blank is rejected before any read is attempted.")]
	public void Probe_RegisteredSectionBlankUId_ReportsNothingWithoutReading() {
		// Arrange
		EnvironmentStub environment = Environment(Route(Rows(), Rows()));

		// Act
		List<ExistingMobilePageInfo> matches = Probe(environment, Request(sectionRegistration: RegisteredSection("  ")));

		// Assert
		matches.Should().BeEmpty();
		environment.Client.ReceivedCalls().Should().BeEmpty();
	}

	[Test]
	[Description("A source page that is NOT a registered mobile section is never probed for a section match, even though the environment would answer.")]
	public void Probe_SectionNotRegistered_SkipsSectionCheckEntirely() {
		// Arrange — the stub would answer with a real section page, but nothing should ask it.
		EnvironmentStub environment = Environment(Route(Rows(PageRow("UsrApp_MobileListPage", MobileRoot, SectionSchemaUId)), Rows()));
		ExistingMobilePageProbeRequest request = Request(sectionRegistration: new SectionRegistrationInfo {
			MobileSectionRegistered = false, MobileSectionSchemaUId = SectionSchemaUId, IsFormPage = false, ProbeOk = true
		});

		// Act
		List<ExistingMobilePageInfo> matches = Probe(environment, request);

		// Assert
		matches.Should().BeEmpty(because: "MobileSectionRegistered=false means there is no existing registration to reuse");
		environment.Client.ReceivedCalls().Should().BeEmpty(because: "an unregistered section is never even asked about");
	}

	[Test]
	[Description("A registered section whose mobile page already carries the TARGET schema name is excluded as a self-match.")]
	public void Probe_SectionMatchIsTheConversionTarget_ExcludesItAsSelfMatch() {
		// Arrange
		EnvironmentStub environment = Environment(Route(Rows(PageRow("UsrApp_MobileListPage", MobileRoot, SectionSchemaUId)), Rows()));

		// Act
		List<ExistingMobilePageInfo> matches = Probe(environment,
			Request(sectionRegistration: RegisteredSection(SectionSchemaUId), targetName: "UsrApp_MobileListPage"));

		// Assert
		matches.Should().BeEmpty(because: "the resolved section page IS the schema this run is about to create/update");
	}

	// ── Both checks together (registered-section form page) ────────────────────────────────────

	[Test]
	[Description("A registered-section FORM page reports BOTH an existing section match and an existing entity-default match together, each under its own source tag — AND the two checks share ONE resolved environment-client trio instead of each independently re-resolving it (the ProbeContext-reuse fix).")]
	public void Probe_RegisteredSectionFormPage_ReportsBothMatchesAndResolvesEnvironmentClientsOnce() {
		// Arrange — a section registration AND a form page bound to an object that ALSO has its own default
		// mobile page, each resolving to a DIFFERENT page UId so the test can tell the two reads apart.
		var byUIdQueries = new List<string>();
		EnvironmentStub environment = Environment(query => {
			if (query.Contains("EntitySchemaManager")) {
				return Rows(EntityRow("LeadProduct"));
			}
			byUIdQueries.Add(query);
			if (query.Contains(SectionSchemaUId)) {
				return Rows(PageRow("UsrApp_MobileListPage", MobileRoot, SectionSchemaUId));
			}
			if (query.Contains(EntityDefaultPageSchemaUId)) {
				return Rows(PageRow("LeadProduct_MobileFormPage", MobileRoot, EntityDefaultPageSchemaUId));
			}
			return Rows();
		}, addonMetaData: $"{{\"Pages\":[{{\"PageSchemaUId\":\"{EntityDefaultPageSchemaUId}\",\"IsDefault\":true}}]}}");
		ExistingMobilePageProbeRequest request = Request(
			sectionRegistration: RegisteredSection(SectionSchemaUId), isFormPage: true,
			modelConfig: SourcePageBoundTo("LeadProduct"));

		// Act
		List<ExistingMobilePageInfo> matches = Probe(environment, request);

		// Assert
		matches.Should().HaveCount(2,
			because: "a page can be both a registered section AND a form bound to an object with its own default page");
		matches.Should().Contain(m => m.Source == ExistingMobilePageProbe.KindSection && m.SchemaName == "UsrApp_MobileListPage");
		matches.Should().Contain(m =>
			m.Source == MobileActionTargetProbe.KindEntityDefaultMobilePage && m.SchemaName == "LeadProduct_MobileFormPage");
		byUIdQueries.Should().HaveCount(2,
			because: "the section and the entity default resolve two DIFFERENT page UIds, so each still issues its own read");
		environment.Resolver.Received(1).Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>());
		environment.Resolver.Received(1).Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>());
		environment.Resolver.Received(1).Resolve<IAddonSchemaDesignerClient>(Arg.Any<EnvironmentOptions>());
	}
}
