using System;
using System.Collections.Generic;
using System.Linq;
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

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class MobileActionTargetProbeTests {

	private const string PackageUId = "11111111-1111-1111-1111-111111111111";
	private const string DesignPackageUId = "22222222-2222-2222-2222-222222222222";
	private const string PageSchemaUId = "33333333-3333-3333-3333-333333333333";
	private const string EntitySchemaUId = "44444444-4444-4444-4444-444444444444";
	private const string DefaultMobilePageUId = "55555555-5555-5555-5555-555555555555";

	private const string MobileRoot = "BaseMobilePageTemplate";
	private const string WebRoot = "BasePageFreedomTemplate";

	/// <summary>
	/// A stand-in environment whose DataService SelectQuery answers come from <paramref name="select"/>,
	/// keyed on the serialized query so a test can answer the page and object lookups differently.
	/// </summary>
	private sealed record EnvironmentStub {

		public IToolCommandResolver Resolver { get; init; }

		public IApplicationClient Client { get; init; }

		public IPageDesignerHierarchyClient HierarchyClient { get; init; }

		public IAddonSchemaDesignerClient AddonClient { get; init; }
	}

	private static EnvironmentStub Environment(
		Func<string, string> select,
		IReadOnlyList<PageDesignerHierarchySchema> hierarchy = null,
		string addonMetaData = null,
		bool templateCatalogThrows = false) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(callInfo => select(callInfo.ArgAt<string>(1)));

		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<string>()).Returns(callInfo => callInfo.Arg<string>());
		urlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns("/DataService/json/SyncReply/SelectQuery");

		ISchemaTemplateCatalog templateCatalog = Substitute.For<ISchemaTemplateCatalog>();
		if (templateCatalogThrows) {
			templateCatalog.GetTemplates(Arg.Any<PageSchemaType?>())
				.Returns(_ => throw new InvalidOperationException("template catalog unavailable"));
		} else {
			templateCatalog.GetTemplates(PageSchemaType.Mobile)
				.Returns([new PageTemplateInfo { Name = MobileRoot }]);
			templateCatalog.GetTemplates(PageSchemaType.Web)
				.Returns([new PageTemplateInfo { Name = WebRoot }]);
		}

		IPageDesignerHierarchyClient hierarchyClient = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchyClient.GetParentSchemas(Arg.Any<string>(), Arg.Any<string>())
			.Returns(hierarchy ?? []);

		IAddonSchemaDesignerClient addonClient = Substitute.For<IAddonSchemaDesignerClient>();
		addonClient.GetSchema(Arg.Any<AddonGetRequestDto>())
			.Returns(new AddonSchemaDto { MetaData = addonMetaData ?? string.Empty });

		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		resolver.Resolve<ISchemaTemplateCatalog>(Arg.Any<EnvironmentOptions>()).Returns(templateCatalog);
		resolver.Resolve<IPageDesignerHierarchyClient>(Arg.Any<EnvironmentOptions>()).Returns(hierarchyClient);
		resolver.Resolve<IAddonSchemaDesignerClient>(Arg.Any<EnvironmentOptions>()).Returns(addonClient);

		return new EnvironmentStub {
			Resolver = resolver, Client = client, HierarchyClient = hierarchyClient, AddonClient = addonClient
		};
	}

	/// <summary>A successful SelectQuery envelope carrying <paramref name="rows"/> (raw JSON objects).</summary>
	private static string Rows(params string[] rows) => $"{{\"success\":true,\"rows\":[{string.Join(",", rows)}]}}";

	private static string PageRow(string name, string parentName, string uId = PageSchemaUId) =>
		$"{{\"Name\":\"{name}\",\"UId\":\"{uId}\",\"ParentName\":{(parentName is null ? "null" : $"\"{parentName}\"")}}}";

	private static string EntityRow(string name, bool extendParent = false, string uId = EntitySchemaUId) =>
		$"{{\"Name\":\"{name}\",\"UId\":\"{uId}\",\"ExtendParent\":{(extendParent ? "true" : "false")}}}";

	/// <summary>Routes a serialized SelectQuery to the page-schema or object-schema answer.</summary>
	private static Func<string, string> Route(string pageRows, string entityRows) =>
		query => query.Contains("EntitySchemaManager") ? entityRows : pageRows;

	private static WebToMobilePageConversionRules RulesWithTargets(
		string openPageKind = MobileActionTargetProbe.KindMobilePage,
		string createRecordKind = MobileActionTargetProbe.KindEntityDefaultMobilePage,
		string openPageParam = "schemaName") =>
		new() {
			Templates = [new TemplateMappingRule { Web = WebRoot, Mobile = MobileRoot }],
			Requests = [
				new RequestMappingRule {
					Web = "crt.OpenPageRequest", Mobile = "crt.OpenPageRequest",
					TargetParam = openPageParam, TargetKind = openPageKind
				},
				new RequestMappingRule {
					Web = "crt.CreateRecordRequest", Mobile = "crt.CreateRecordRequest",
					TargetParam = "entityName", TargetKind = createRecordKind
				},
				new RequestMappingRule { Web = "crt.SaveRecordRequest", Mobile = "crt.SaveRecordRequest" }
			]
		};

	/// <summary>A one-button page whose <c>clicked</c> fires <paramref name="request"/> at <paramref name="target"/>.</summary>
	private static JsonArray ViewConfig(string request, string paramName, string target, string elementName = "TargetButton") =>
		JsonNode.Parse($$"""
		[
		  {
		    "type": "crt.FlexContainer",
		    "name": "MainContainer",
		    "items": [
		      {
		        "type": "crt.Button",
		        "name": "{{elementName}}",
		        "clicked": { "request": "{{request}}", "params": { "{{paramName}}": "{{target}}" } }
		      }
		    ]
		  }
		]
		""").AsArray();

	private static ActionTargetState StateOf(MobileActionTargetProbeResult result, string kind, string target) =>
		result.TargetsByKey.TryGetValue(MobileActionTargetProbe.TargetKey(kind, target), out ActionTargetResolution resolution)
			? resolution.State
			: ActionTargetState.Unknown;

	private static MobileActionTargetProbeResult Probe(
		EnvironmentStub environment, JsonArray viewConfig, WebToMobilePageConversionRules rules = null,
		JsonObject modelConfig = null) =>
		MobileActionTargetProbe.Probe(
			environment.Resolver, "env", null, null, null,
			viewConfig, rules ?? RulesWithTargets(), modelConfig, PackageUId, DesignPackageUId);

	/// <summary>A modelConfig whose single data source binds the page to <paramref name="entityName"/>.</summary>
	private static JsonObject SourcePageBoundTo(string entityName) =>
		JsonNode.Parse($$"""
		{ "dataSources": { "PDS": { "type": "crt.EntityDataSource",
		    "config": { "entitySchemaName": "{{entityName}}" } } } }
		""").AsObject();

	// ── Collection (pure, no environment) ───────────────────────────────────────────────────────

	[Test]
	[Description("A binding whose request declares a targetParam is collected with its element, binding, request and literal target.")]
	public void CollectActionTargets_TargetedRequest_RecordsTheOccurrence() {
		// Arrange
		JsonArray viewConfig = ViewConfig("crt.OpenPageRequest", "schemaName", "SomePage");

		// Act
		IReadOnlyList<ActionTargetOccurrence> found = CollectWith(viewConfig);

		// Assert
		found.Should().ContainSingle(because: "the page fires exactly one target-carrying request");
		found[0].ElementName.Should().Be("TargetButton",
			because: "the caller must be able to find the control on the source page");
		found[0].Binding.Should().Be("clicked", because: "the binding name identifies which action is dead");
		found[0].WebRequest.Should().Be("crt.OpenPageRequest", because: "the request type is reported verbatim");
		found[0].Kind.Should().Be(MobileActionTargetProbe.KindMobilePage,
			because: "the kind comes from the rule that declared the target");
		found[0].Target.Should().Be("SomePage", because: "the literal params value is the target");
	}

	[Test]
	[Description("A request the rules declare no targetParam for is never collected, so it is never target-checked.")]
	public void CollectActionTargets_RequestWithoutTargetParam_IsIgnored() {
		// Arrange
		JsonArray viewConfig = ViewConfig("crt.SaveRecordRequest", "schemaName", "SomePage");

		// Act
		IReadOnlyList<ActionTargetOccurrence> found = CollectWith(viewConfig);

		// Assert
		found.Should().BeEmpty(because: "which requests carry a target is data, and this rule declares none");
	}

	[Test]
	[Description("An unrecognized targetKind is treated as 'no check', so a newer rules file cannot make an older clio report a target it cannot verify.")]
	public void CollectActionTargets_UnrecognizedTargetKind_IsIgnored() {
		// Arrange
		WebToMobilePageConversionRules rules = RulesWithTargets(openPageKind: "some-future-kind");
		JsonArray viewConfig = ViewConfig("crt.OpenPageRequest", "schemaName", "SomePage");

		// Act
		IReadOnlyList<ActionTargetOccurrence> found = CollectWith(viewConfig, rules);

		// Assert
		found.Should().BeEmpty(because: "an unknown kind must degrade to silence, never to a finding");
	}

	[Test]
	[Description("A target bound to a page attribute ($Something) is skipped: its value is only known at runtime and can never be verified.")]
	public void CollectActionTargets_AttributeBoundTarget_IsIgnored() {
		// Arrange
		JsonArray viewConfig = ViewConfig("crt.OpenPageRequest", "schemaName", "$PageToOpen");

		// Act
		IReadOnlyList<ActionTargetOccurrence> found = CollectWith(viewConfig);

		// Assert
		found.Should().BeEmpty(because: "a runtime-resolved target must never be reported as absent");
	}

	[Test]
	[Description("Bindings nested in a non-items child array (menuItems) are collected, so a menu item's dead target is reported too.")]
	public void CollectActionTargets_MenuItemBinding_IsCollected() {
		// Arrange
		JsonArray viewConfig = JsonNode.Parse("""
		[
		  {
		    "type": "crt.Button",
		    "name": "ActionsButton",
		    "menuItems": [
		      {
		        "type": "crt.MenuItem",
		        "name": "OpenLegacyItem",
		        "clicked": { "request": "crt.OpenPageRequest", "params": { "schemaName": "LegacyPage" } }
		      }
		    ]
		  }
		]
		""").AsArray();

		// Act
		IReadOnlyList<ActionTargetOccurrence> found = CollectWith(viewConfig);

		// Assert
		found.Should().ContainSingle(because: "menuItems carry real actions and the ticket names menu items explicitly");
		found[0].ElementName.Should().Be("OpenLegacyItem",
			because: "the menu item owns the binding, not its host button");
	}

	[Test]
	[Description("Two bindings naming the same target are collected separately but resolve as ONE distinct target — the dedupe contract.")]
	public void Probe_RepeatedTarget_IssuesOneSelectQuery() {
		// Arrange
		JsonArray viewConfig = JsonNode.Parse("""
		[
		  {
		    "type": "crt.FlexContainer",
		    "name": "MainContainer",
		    "items": [
		      { "type": "crt.Button", "name": "FirstButton",
		        "clicked": { "request": "crt.OpenPageRequest", "params": { "schemaName": "SharedPage" } } },
		      { "type": "crt.Button", "name": "SecondButton",
		        "clicked": { "request": "crt.OpenPageRequest", "params": { "schemaName": "SharedPage" } } }
		    ]
		  }
		]
		""").AsArray();
		EnvironmentStub environment = Environment(Route(Rows(PageRow("SharedPage", MobileRoot)), Rows()));

		// Act
		MobileActionTargetProbeResult result = Probe(environment, viewConfig);

		// Assert
		result.Occurrences.Should().HaveCount(2, because: "both controls must be reportable individually");
		result.TargetsByKey.Should().ContainSingle(
			because: "one distinct target is resolved once no matter how many bindings name it");
		environment.Client.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(IApplicationClient.ExecutePostRequest))
			.Should().Be(1, because: "deduplication must collapse repeated targets into a single round trip");
	}

	// ── Page targets ───────────────────────────────────────────────────────────────────────────

	[Test]
	[Description("A page whose parent is a mobile template root resolves as present on mobile.")]
	public void Probe_PageUnderMobileTemplate_ResolvesTarget() {
		// Arrange
		EnvironmentStub environment = Environment(Route(Rows(PageRow("Usr_MobileFormPage", MobileRoot)), Rows()));

		// Act
		MobileActionTargetProbeResult result = Probe(
			environment, ViewConfig("crt.OpenPageRequest", "schemaName", "Usr_MobileFormPage"));

		// Assert
		result.ProbeOk.Should().BeTrue(because: "the environment answered");
		StateOf(result, MobileActionTargetProbe.KindMobilePage, "Usr_MobileFormPage")
			.Should().Be(ActionTargetState.Resolved, because: "its parent is a known mobile template root");
	}

	[Test]
	[Description("A page whose every layer descends from a web template is verified absent on mobile.")]
	public void Probe_PageUnderWebTemplate_ReportsMissing() {
		// Arrange
		EnvironmentStub environment = Environment(Route(Rows(PageRow("Usr_FormPage", WebRoot)), Rows()));

		// Act
		MobileActionTargetProbeResult result = Probe(
			environment, ViewConfig("crt.OpenPageRequest", "schemaName", "Usr_FormPage"));

		// Assert
		StateOf(result, MobileActionTargetProbe.KindMobilePage, "Usr_FormPage")
			.Should().Be(ActionTargetState.Missing, because: "a web page is not something the mobile app can open");
	}

	[Test]
	[Description("A schema name with no client-unit row at all is verified absent, independent of any template allowlist.")]
	public void Probe_PageWithNoSchemaRow_ReportsMissing() {
		// Arrange
		EnvironmentStub environment = Environment(Route(Rows(), Rows()));

		// Act
		MobileActionTargetProbeResult result = Probe(
			environment, ViewConfig("crt.OpenPageRequest", "schemaName", "GonePage"));

		// Assert
		StateOf(result, MobileActionTargetProbe.KindMobilePage, "GonePage")
			.Should().Be(ActionTargetState.Missing, because: "the schema does not exist, which is unambiguous");
	}

	[Test]
	[Description("A page under an unrecognized parent escalates to the designer hierarchy, whose numeric schema type settles it.")]
	public void Probe_PageUnderCustomBase_EscalatesToHierarchy() {
		// Arrange
		EnvironmentStub environment = Environment(
			Route(Rows(PageRow("Usr_CustomPage", "UsrCustomMobileBase")), Rows()),
			hierarchy: [new PageDesignerHierarchySchema { UId = PageSchemaUId, Name = "Usr_CustomPage", SchemaType = 10 }]);

		// Act
		MobileActionTargetProbeResult result = Probe(
			environment, ViewConfig("crt.OpenPageRequest", "schemaName", "Usr_CustomPage"));

		// Assert
		StateOf(result, MobileActionTargetProbe.KindMobilePage, "Usr_CustomPage")
			.Should().Be(ActionTargetState.Resolved, because: "schemaType 10 is the authoritative mobile answer");
		environment.HierarchyClient.Received(1).GetParentSchemas(PageSchemaUId, DesignPackageUId);
	}

	[Test]
	[Description("When the escalation returns nothing, the target stays unknown rather than being reported absent.")]
	public void Probe_EscalationWithoutHierarchy_ReportsUnknown() {
		// Arrange
		EnvironmentStub environment = Environment(
			Route(Rows(PageRow("Usr_CustomPage", "UsrCustomMobileBase")), Rows()));

		// Act
		MobileActionTargetProbeResult result = Probe(
			environment, ViewConfig("crt.OpenPageRequest", "schemaName", "Usr_CustomPage"));

		// Assert
		StateOf(result, MobileActionTargetProbe.KindMobilePage, "Usr_CustomPage")
			.Should().Be(ActionTargetState.Unknown, because: "an unanswered escalation is not evidence of absence");
	}

	[Test]
	[Description("A page whose parent is unreadable but whose template catalog is unavailable still classifies off the bundled mobile roots.")]
	public void Probe_TemplateCatalogUnavailable_StillClassifiesBundledRoots() {
		// Arrange
		EnvironmentStub environment = Environment(
			Route(Rows(PageRow("Usr_MobileFormPage", "BaseMobileListTemplate")), Rows()),
			templateCatalogThrows: true);

		// Act
		MobileActionTargetProbeResult result = Probe(
			environment, ViewConfig("crt.OpenPageRequest", "schemaName", "Usr_MobileFormPage"));

		// Assert
		result.ProbeOk.Should().BeTrue(because: "the catalog tier is best-effort and must not fail the probe");
		StateOf(result, MobileActionTargetProbe.KindMobilePage, "Usr_MobileFormPage")
			.Should().Be(ActionTargetState.Resolved, because: "the bundled mobile roots still place an OOTB shape");
	}

	// ── Object (default mobile edit page) targets ──────────────────────────────────────────────

	[Test]
	[Description("An object whose MobileRelatedPage add-on declares an untyped default page resolves as present on mobile.")]
	public void Probe_EntityWithDefaultMobilePage_ResolvesTarget() {
		// Arrange
		EnvironmentStub environment = Environment(
			Route(Rows(), Rows(EntityRow("Opportunity"))),
			addonMetaData: $"{{\"Pages\":[{{\"PageSchemaUId\":\"{DefaultMobilePageUId}\",\"IsDefault\":true}}]}}");

		// Act
		MobileActionTargetProbeResult result = Probe(
			environment, ViewConfig("crt.CreateRecordRequest", "entityName", "Opportunity"));

		// Assert
		StateOf(result, MobileActionTargetProbe.KindEntityDefaultMobilePage, "Opportunity")
			.Should().Be(ActionTargetState.Resolved, because: "the mobile app has a page to open for the object");
	}

	[Test]
	[Description("An object whose MobileRelatedPage add-on declares no default page is verified absent.")]
	public void Probe_EntityWithoutDefaultMobilePage_ReportsMissing() {
		// Arrange
		EnvironmentStub environment = Environment(
			Route(Rows(), Rows(EntityRow("Opportunity"))),
			addonMetaData: "{\"Pages\":[]}");

		// Act
		MobileActionTargetProbeResult result = Probe(
			environment, ViewConfig("crt.CreateRecordRequest", "entityName", "Opportunity"));

		// Assert
		StateOf(result, MobileActionTargetProbe.KindEntityDefaultMobilePage, "Opportunity")
			.Should().Be(ActionTargetState.Missing, because: "a create action has no mobile page to open");
	}

	[Test]
	[Description("The object add-on is addressed by its BASE row, so a replacing layer is never used instead.")]
	public void Probe_EntityWithReplacingLayer_AddressesTheBaseRow() {
		// Arrange
		EnvironmentStub environment = Environment(
			Route(Rows(), Rows(
				EntityRow("Opportunity", extendParent: true, uId: "99999999-9999-9999-9999-999999999999"),
				EntityRow("Opportunity"))),
			addonMetaData: "{\"Pages\":[]}");

		// Act
		Probe(environment, ViewConfig("crt.CreateRecordRequest", "entityName", "Opportunity"));

		// Assert — the add-on must be read for the BASE schema: a replacing layer is not a different object,
		// and reading one would classify the wrong physical schema's page set.
		environment.AddonClient.Received(1).GetSchema(
			Arg.Is<AddonGetRequestDto>(request =>
				request.TargetSchemaUId == Guid.Parse(EntitySchemaUId)
				&& request.AddonName == "MobileRelatedPage"
				&& request.TargetSchemaManagerName == "EntitySchemaManager"));
	}

	[Test]
	[Description("An object with rows but no base row stays unknown: it cannot be addressed reliably, so absence is not concluded.")]
	public void Probe_EntityWithoutBaseRow_ReportsUnknown() {
		// Arrange
		EnvironmentStub environment = Environment(
			Route(Rows(), Rows(EntityRow("Opportunity", extendParent: true))));

		// Act
		MobileActionTargetProbeResult result = Probe(
			environment, ViewConfig("crt.CreateRecordRequest", "entityName", "Opportunity"));

		// Assert
		StateOf(result, MobileActionTargetProbe.KindEntityDefaultMobilePage, "Opportunity")
			.Should().Be(ActionTargetState.Unknown, because: "clio refuses to guess which physical schema to read");
	}

	[Test]
	[Description("An object with no rows at all is verified absent — the action targets something that does not exist.")]
	public void Probe_EntityWithNoRows_ReportsMissing() {
		// Arrange
		EnvironmentStub environment = Environment(Route(Rows(), Rows()));

		// Act
		MobileActionTargetProbeResult result = Probe(
			environment, ViewConfig("crt.CreateRecordRequest", "entityName", "GoneObject"));

		// Assert
		StateOf(result, MobileActionTargetProbe.KindEntityDefaultMobilePage, "GoneObject")
			.Should().Be(ActionTargetState.Missing, because: "the object itself is not there");
	}

	[TestCase("", ActionTargetState.Unknown, TestName = "ClassifyRelatedPageMetadata_Blank_IsUnknown")]
	[TestCase("{}", ActionTargetState.Missing, TestName = "ClassifyRelatedPageMetadata_NoPagesKey_IsMissing")]
	[TestCase("not json", ActionTargetState.Unknown, TestName = "ClassifyRelatedPageMetadata_Unparseable_IsUnknown")]
	[TestCase("[]", ActionTargetState.Unknown, TestName = "ClassifyRelatedPageMetadata_NonObjectRoot_IsUnknown")]
	[TestCase("{\"Pages\":[{\"IsDefault\":true,\"TypeColumnValue\":\"abc\"}]}", ActionTargetState.Missing,
		TestName = "ClassifyRelatedPageMetadata_OnlyTypedDefault_IsMissing")]
	[TestCase("{\"Pages\":[{\"IsDefault\":true}]}", ActionTargetState.Resolved,
		TestName = "ClassifyRelatedPageMetadata_UntypedDefault_IsResolved")]
	[Description("Add-on metadata classifies by whether it declares an UNTYPED default page; only an unreadable body is unknown.")]
	public void ClassifyRelatedPageMetadata_ClassifiesByDefaultPage(string metaData, ActionTargetState expected) {
		// Arrange & Act
		ActionTargetState state = MobileActionTargetProbe.ClassifyRelatedPageMetadata(metaData);

		// Assert
		state.Should().Be(expected,
			because: "an empty page SET is a real absence, but a blank or unreadable body proves nothing — it is "
				+ "equally the shape a request the server did not understand returns");
	}

	[Test]
	[Description("The object the page itself is bound to is not reported at all: creating its default mobile page IS this conversion's closing step, so neither 'missing' nor 'please verify' is a question worth asking.")]
	public void Probe_TargetIsTheSourcePagesOwnObject_IsNotReported() {
		// Arrange — an object with no MobileRelatedPage add-on at all, which is the pre-conversion state.
		EnvironmentStub environment = Environment(Route(Rows(), Rows(EntityRow("Lead"))), addonMetaData: "{\"Pages\":[]}");

		// Act
		MobileActionTargetProbeResult result = Probe(
			environment, ViewConfig("crt.CreateRecordRequest", "entityName", "Lead"),
			modelConfig: SourcePageBoundTo("Lead"));

		// Assert
		result.Occurrences.Should().BeEmpty(
			because: "flagging the create-Lead action while the same guide instructs the user to create Lead's "
				+ "mobile edit page would have one response contradict the other");
		StateOf(result, MobileActionTargetProbe.KindEntityDefaultMobilePage, "Lead")
			.Should().Be(ActionTargetState.Unknown,
				because: "an unreported target must also stay unresolved, so nothing downstream can conclude it "
					+ "is absent");
		environment.AddonClient.DidNotReceive().GetSchema(Arg.Any<AddonGetRequestDto>());
	}

	[Test]
	[Description("An object the page is NOT bound to is still checked, so the source-object exemption does not disable the feature.")]
	public void Probe_TargetIsAnotherObject_IsStillChecked() {
		// Arrange
		EnvironmentStub environment = Environment(
			Route(Rows(), Rows(EntityRow("Opportunity"))), addonMetaData: "{\"Pages\":[]}");

		// Act
		MobileActionTargetProbeResult result = Probe(
			environment, ViewConfig("crt.CreateRecordRequest", "entityName", "Opportunity"),
			modelConfig: SourcePageBoundTo("Lead"));

		// Assert
		StateOf(result, MobileActionTargetProbe.KindEntityDefaultMobilePage, "Opportunity")
			.Should().Be(ActionTargetState.Missing,
				because: "an unrelated object with an empty page set genuinely has nothing for mobile to open");
	}

	[Test]
	[Description("A leading space does not smuggle an attribute-bound target past the $ guard.")]
	public void CollectActionTargets_AttributeBoundTargetWithLeadingSpace_IsIgnored() {
		// Arrange
		JsonArray viewConfig = ViewConfig("crt.OpenPageRequest", "schemaName", " $PageToOpen");

		// Act
		IReadOnlyList<ActionTargetOccurrence> found = CollectWith(viewConfig);

		// Assert
		found.Should().BeEmpty(
			because: "trimming after the guard would look up a schema named '$PageToOpen', find none, and "
				+ "report a working control as navigating nowhere");
	}

	[Test]
	[Description("A bool persisted as the string \"true\" counts as the default page, matching the reader that writes this add-on.")]
	public void Probe_EntityWithStringifiedDefaultFlag_ResolvesTarget() {
		// Arrange
		EnvironmentStub environment = Environment(
			Route(Rows(), Rows(EntityRow("Opportunity"))),
			addonMetaData: $"{{\"Pages\":[{{\"PageSchemaUId\":\"{DefaultMobilePageUId}\",\"IsDefault\":\"true\"}}]}}");

		// Act
		MobileActionTargetProbeResult result = Probe(
			environment, ViewConfig("crt.CreateRecordRequest", "entityName", "Opportunity"));

		// Assert
		StateOf(result, MobileActionTargetProbe.KindEntityDefaultMobilePage, "Opportunity")
			.Should().Be(ActionTargetState.Resolved,
				because: "get-related-page-addon reads the same record as the default, and the two surfaces "
					+ "must not disagree on the delete-the-control side");
	}

	[Test]
	[Description("A non-string TypeColumnValue still marks the entry as TYPED, so it is not mistaken for the untyped default.")]
	public void Probe_EntityWithNumericTypeColumnValue_ReportsMissing() {
		// Arrange
		EnvironmentStub environment = Environment(
			Route(Rows(), Rows(EntityRow("Opportunity"))),
			addonMetaData: "{\"Pages\":[{\"IsDefault\":true,\"TypeColumnValue\":42}]}");

		// Act
		MobileActionTargetProbeResult result = Probe(
			environment, ViewConfig("crt.CreateRecordRequest", "entityName", "Opportunity"));

		// Assert
		StateOf(result, MobileActionTargetProbe.KindEntityDefaultMobilePage, "Opportunity")
			.Should().Be(ActionTargetState.Missing,
				because: "a record-type-specific page is not the page a plain create action opens");
	}

	// ── Truncated reads must never look like absence ───────────────────────────────────────────

	[Test]
	[Description("When the page read comes back exactly at the row cap, an absent name is unknown rather than missing: its row may simply have been cut off.")]
	public void Probe_PageReadHitsTheRowCap_ReportsUnknownInsteadOfMissing() {
		// Arrange: one requested name buys four rows of headroom; returning four means more may exist.
		EnvironmentStub environment = Environment(Route(
			Rows(
				PageRow("Other_MobileFormPage", MobileRoot, "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
				PageRow("Other_MobileFormPage", MobileRoot, "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
				PageRow("Other_MobileFormPage", MobileRoot, "cccccccc-cccc-cccc-cccc-cccccccccccc"),
				PageRow("Other_MobileFormPage", MobileRoot, "dddddddd-dddd-dddd-dddd-dddddddddddd")),
			Rows()));

		// Act
		MobileActionTargetProbeResult result = Probe(
			environment, ViewConfig("crt.OpenPageRequest", "schemaName", "MaybeCutOffPage"));

		// Assert
		StateOf(result, MobileActionTargetProbe.KindMobilePage, "MaybeCutOffPage")
			.Should().Be(ActionTargetState.Unknown,
				because: "a row that may have been truncated away is not evidence the page does not exist");
	}

	[Test]
	[Description("A truncated page read also refuses the web-only verdict: a mobile-rooted layer may be among the rows that were cut.")]
	public void Probe_PageReadHitsTheRowCap_DoesNotConcludeWebOnly() {
		// Arrange
		EnvironmentStub environment = Environment(Route(
			Rows(
				PageRow("Usr_FormPage", WebRoot, "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
				PageRow("Usr_FormPage", WebRoot, "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
				PageRow("Usr_FormPage", WebRoot, "cccccccc-cccc-cccc-cccc-cccccccccccc"),
				PageRow("Usr_FormPage", WebRoot, "dddddddd-dddd-dddd-dddd-dddddddddddd")),
			Rows()));

		// Act
		MobileActionTargetProbeResult result = Probe(
			environment, ViewConfig("crt.OpenPageRequest", "schemaName", "Usr_FormPage"));

		// Assert
		StateOf(result, MobileActionTargetProbe.KindMobilePage, "Usr_FormPage")
			.Should().NotBe(ActionTargetState.Missing,
				because: "concluding web-only from a partial row set would delete a control on a guess");
	}

	[Test]
	[Description("When the object read comes back exactly at the row cap, an absent object is unknown rather than missing.")]
	public void Probe_EntityReadHitsTheRowCap_ReportsUnknownInsteadOfMissing() {
		// Arrange
		EnvironmentStub environment = Environment(Route(
			Rows(),
			Rows(
				EntityRow("Other", uId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
				EntityRow("Other", uId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
				EntityRow("Other", uId: "cccccccc-cccc-cccc-cccc-cccccccccccc"),
				EntityRow("Other", uId: "dddddddd-dddd-dddd-dddd-dddddddddddd"))));

		// Act
		MobileActionTargetProbeResult result = Probe(
			environment, ViewConfig("crt.CreateRecordRequest", "entityName", "MaybeCutOffObject"));

		// Assert
		StateOf(result, MobileActionTargetProbe.KindEntityDefaultMobilePage, "MaybeCutOffObject")
			.Should().Be(ActionTargetState.Unknown,
				because: "a truncated object read cannot prove the object is absent");
	}

	// ── Fail-open ──────────────────────────────────────────────────────────────────────────────

	[Test]
	[Description("A DataService failure envelope degrades the whole probe: no resolutions, ProbeOk false, a note, and no exception.")]
	public void Probe_SelectQueryFails_DegradesWithoutThrowing() {
		// Arrange
		EnvironmentStub environment = Environment(_ => "{\"success\":false,\"errorInfo\":{\"message\":\"denied\"}}");

		// Act
		MobileActionTargetProbeResult result = Probe(
			environment, ViewConfig("crt.OpenPageRequest", "schemaName", "SomePage"));

		// Assert
		result.ProbeOk.Should().BeFalse(because: "nothing was verified");
		result.TargetsByKey.Should().BeEmpty(because: "an absent resolution reads as unknown, which never removes UI");
		result.Note.Should().NotBeNullOrWhiteSpace(because: "the caller must be able to say why nothing was checked");
	}

	[Test]
	[Description("A proxy/auth error PAGE is not mistaken for an empty result set — the transport returns it as an ordinary body.")]
	public void Probe_NonJsonResponseBody_DegradesInsteadOfReportingMissing() {
		// Arrange
		EnvironmentStub environment = Environment(_ => "<html><body>401 Unauthorized</body></html>");

		// Act
		MobileActionTargetProbeResult result = Probe(
			environment, ViewConfig("crt.OpenPageRequest", "schemaName", "SomePage"));

		// Assert
		result.ProbeOk.Should().BeFalse(
			because: "reading an error page as 'no rows' would report every target as absent");
		result.TargetsByKey.Should().BeEmpty(because: "no verdict may be derived from an unreadable response");
	}

	[Test]
	[Description("Without an environment client nothing is verified, but the occurrences are still reported.")]
	public void Probe_WithoutResolver_ReportsNotProbed() {
		// Arrange
		JsonArray viewConfig = ViewConfig("crt.OpenPageRequest", "schemaName", "SomePage");

		// Act
		MobileActionTargetProbeResult result = MobileActionTargetProbe.Probe(
			null, "env", null, null, null, viewConfig, RulesWithTargets(), null, PackageUId, DesignPackageUId);

		// Assert
		result.ProbeOk.Should().BeFalse(because: "there was no environment to ask");
		result.Occurrences.Should().ContainSingle(because: "the page analysis is offline and still valid");
	}

	[Test]
	[Description("Rules that declare no navigation targets report 'not probed', so an empty finding list is never read as all-clear.")]
	public void Probe_RulesDeclareNoTargets_ReportsNotProbed() {
		// Arrange
		var rules = new WebToMobilePageConversionRules {
			Requests = [new RequestMappingRule { Web = "crt.OpenPageRequest", Mobile = "crt.OpenPageRequest" }]
		};
		EnvironmentStub environment = Environment(Route(Rows(), Rows()));

		// Act
		MobileActionTargetProbeResult result = Probe(
			environment, ViewConfig("crt.OpenPageRequest", "schemaName", "SomePage"), rules);

		// Assert
		result.ProbeOk.Should().BeFalse(because: "the rules define no check, so nothing was verified");
		result.Note.Should().Contain("no request navigation targets",
			because: "the reason must distinguish 'nothing declared' from 'environment unreachable'");
	}

	[Test]
	[Description("A page with no target-carrying bindings reports a successful, empty check rather than a degraded one.")]
	public void Probe_PageWithoutTargetedBindings_ReportsProbedAndEmpty() {
		// Arrange
		EnvironmentStub environment = Environment(Route(Rows(), Rows()));

		// Act
		MobileActionTargetProbeResult result = Probe(
			environment, ViewConfig("crt.SaveRecordRequest", "schemaName", "SomePage"));

		// Assert
		result.ProbeOk.Should().BeTrue(because: "the check ran and found nothing to verify");
		result.Occurrences.Should().BeEmpty(because: "no binding on the page navigates anywhere");
	}

	[Test]
	[Description("Without a usable package UId the object add-on cannot be addressed, so its targets stay unknown rather than absent.")]
	public void Probe_WithoutPackageUId_ReportsEntityTargetsUnknown() {
		// Arrange
		EnvironmentStub environment = Environment(Route(Rows(), Rows(EntityRow("Opportunity"))));

		// Act
		MobileActionTargetProbeResult result = MobileActionTargetProbe.Probe(
			environment.Resolver, "env", null, null, null,
			ViewConfig("crt.CreateRecordRequest", "entityName", "Opportunity"),
			RulesWithTargets(), modelConfig: null, pagePackageUId: null, designPackageUId: DesignPackageUId);

		// Assert
		StateOf(result, MobileActionTargetProbe.KindEntityDefaultMobilePage, "Opportunity")
			.Should().Be(ActionTargetState.Unknown, because: "the gap is in clio's inputs, not in the environment");
	}

	[Test]
	[Description("A null view config yields no occurrences and no environment reads at all.")]
	public void Probe_NullViewConfig_ReadsNothing() {
		// Arrange
		EnvironmentStub environment = Environment(Route(Rows(), Rows()));

		// Act
		MobileActionTargetProbeResult result = Probe(environment, viewConfig: null);

		// Assert
		result.Occurrences.Should().BeEmpty(because: "there is no page body to read bindings from");
		environment.Client.ReceivedCalls().Should().BeEmpty(because: "nothing to resolve means nothing to query");
	}

	private static IReadOnlyList<ActionTargetOccurrence> CollectWith(
		JsonArray viewConfig, WebToMobilePageConversionRules rules = null) {
		MobileActionTargetProbeResult result = MobileActionTargetProbe.Probe(
			null, "env", null, null, null, viewConfig, rules ?? RulesWithTargets(), null,
			PackageUId, DesignPackageUId);
		return result.Occurrences;
	}
}
