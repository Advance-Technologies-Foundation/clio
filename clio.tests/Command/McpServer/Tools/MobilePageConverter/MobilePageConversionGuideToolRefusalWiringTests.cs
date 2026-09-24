namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter;

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.MobilePageConverter;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

/// <summary>
/// Drives <see cref="MobilePageConversionGuideTool.GetMobilePageConversionGuide"/> end to end so wiring
/// gaps are asserted WHERE THEY ARE WIRED, not where the pieces they call are defined: the two hard-stop
/// template refusals, and the existing-mobile-pages self-match exclusion (playbook step 2a).
/// </summary>
/// <remarks>
/// <para>
/// <c>MobilePageConversionGuideToolTests</c> calls <c>RejectUnobtainableMobileTemplate</c> and
/// <c>RejectUnobtainableWebTemplate</c> directly and passes <c>templateUnavailable:</c> as a literal, so it
/// proves the helpers compose the right message and nothing else. Deleting both
/// <c>if (rejection is not null) return rejection;</c> blocks at their call sites left all 13454 unit tests
/// green — the branch's headline behaviour change, refusing instead of shipping a guide that cannot be
/// trusted, was entirely unguarded.
/// </para>
/// <para>
/// The refusal seam is <see cref="MobilePageConversionGuideTool.ReadPageUnderTenantLock"/>: overriding it
/// decides what every page read in the method returns — the source page, the mobile template, the web
/// template — which is exactly the surface the two refusals key on.
/// </para>
/// <para>
/// The existing-mobile-pages tests prove the WIRING between the tool's derived/explicit target schema name
/// and <see cref="ExistingMobilePageProbeRequest.TargetName"/> — the self-match exclusion
/// <see cref="MobilePageConversionGuide.ExistingMobilePages"/> depends on. A unit test on
/// <see cref="MobilePageConversionGuideTool.DeriveMobileSchemaName"/> alone cannot catch a call-site bug
/// that swaps the computed value for <c>args.SchemaName</c> (or forgets to trim an explicit override) —
/// these tests drive the FULL entity-default-mobile-page probe chain (<c>SysSchema</c> by name, the
/// <c>MobileRelatedPage</c> add-on, <c>SysSchema</c> by UId) with a real registered default mobile page, so
/// a wrong target name changes the OBSERVABLE result: the match is reported instead of excluded, or vice
/// versa.
/// </para>
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class MobilePageConversionGuideToolRefusalWiringTests {

	private const string SourcePage = "UsrLead_FormPage";
	private const string WebTemplate = "PageWithTabsFreedomTemplate";
	private const string MobileTemplate = "BaseMobilePageTemplate";

	private const string EntitySourcePage = "Lead_FormPage";
	private const string BoundEntity = "Lead";
	private const string PackageUId = "11111111-1111-1111-1111-111111111111";
	private const string EntityUId = "44444444-4444-4444-4444-444444444444";
	private const string CandidatePageUId = "55555555-5555-5555-5555-555555555555";

	[Test]
	[Description("An unreadable MOBILE template refuses the whole tool. Asserted through the tool method rather than through RejectUnobtainableMobileTemplate, because the defect this guards is a missing `return` at the call site, which a direct call on the helper cannot see.")]
	public async Task GetGuide_ShouldRefuse_WhenTheMobileTemplateCannotBeRead() {
		// Arrange — the source page and its web template read fine; the mobile template does not.
		var tool = new StubbedTool(unreadable: MobileTemplate);

		// Act
		MobilePageConversionGuideResponse response = await tool.GetMobilePageConversionGuide(Args());

		// Assert
		response.Success.Should().BeFalse(
			because: "without the mobile template the guide cannot tell which elements it already provides, so "
				+ "it would instruct the caller to insert duplicates of native elements");
		response.Error.Should().Contain(MobileTemplate,
			because: "naming the schema is what makes the refusal actionable");
		response.Guide.Should().BeNull(
			because: "a refusal that still carried a guide would be acted on as though it were usable");
	}

	[Test]
	[Description("An unreadable WEB template refuses the whole tool, for the mirror reason: with no baseline the inherited chrome all converts to inserts and the same-name twin never fires. Same wiring gap, same seam.")]
	public async Task GetGuide_ShouldRefuse_WhenTheWebTemplateCannotBeRead() {
		// Arrange
		var tool = new StubbedTool(unreadable: WebTemplate);

		// Act
		MobilePageConversionGuideResponse response = await tool.GetMobilePageConversionGuide(Args());

		// Assert
		response.Success.Should().BeFalse(
			because: "with no web baseline nothing is subtracted as inherited chrome, so the guide duplicates it");
		response.Error.Should().Contain(WebTemplate,
			because: "naming the schema is what makes the refusal actionable");
	}

	[Test]
	[Description("The control case: with every template readable the tool returns a guide. Without it both tests above would pass on a tool that refused unconditionally, which is the same class of vacuity as the gap they close.")]
	public async Task GetGuide_ShouldReturnAGuide_WhenEveryTemplateIsReadable() {
		// Arrange
		var tool = new StubbedTool(unreadable: null);

		// Act
		MobilePageConversionGuideResponse response = await tool.GetMobilePageConversionGuide(Args());

		// Assert
		response.Success.Should().BeTrue(
			because: "every template read, so nothing is unavailable and no refusal applies. Error: " + response.Error);
		response.Guide.Should().NotBeNull(
			because: "the ordinary path must still produce the guide the refusals are the exception to");
	}

	[Test]
	[Description("A mobile template that READS but carries no viewConfig is the same failure as one that cannot be read, and must refuse too. The two probes disagreed: the web one gated on Bundle?.ViewConfig, the mobile one only on Bundle, so a successful read with a null viewConfig reported Unavailable:false while leaving parents, types and placements all empty — which is exactly the state the refusal exists for. With an empty TypesByName the same-name twin is never detected and the guide instructs the caller to insert a DUPLICATE of a native element, and RetargetTargetMissing fails open.")]
	public async Task GetGuide_ShouldRefuse_WhenTheMobileTemplateReadsButCarriesNoViewConfig() {
		// Arrange
		var tool = new StubbedTool(unreadable: null, templateWithoutViewConfig: true);

		// Act
		MobilePageConversionGuideResponse response = await tool.GetMobilePageConversionGuide(Args());

		// Assert
		response.Success.Should().BeFalse(
			because: "an empty template node set is indistinguishable in EFFECT from an unread template, and "
				+ "it is the effect the refusal is about");
		response.Error.Should().Contain(MobileTemplate,
			because: "the caller still needs to know which schema came back empty");
	}

	[Test]
	[Description("When --target-schema-name is omitted, the tool must probe existingMobilePages using the DERIVED mobile schema name (DeriveMobileSchemaName), not the source page's own name. The bound entity's registered default mobile page here IS that exact derived name ('Lead_MobileFormPage'), so correct wiring excludes it as a self-match; the review's named bug (swapping in args.SchemaName, 'Lead_FormPage') would leave it unexcluded and fail this assertion.")]
	public async Task GetGuide_OmittedTargetSchemaName_ExcludesSelfMatch_UsingDerivedName() {
		// Arrange
		var tool = new StubbedEntityProbeTool(existingMobilePageName: "Lead_MobileFormPage");

		// Act
		MobilePageConversionGuideResponse response = await tool.GetMobilePageConversionGuide(EntityArgs(targetSchemaName: null));

		// Assert
		response.Success.Should().BeTrue(because: "the run must complete: " + response.Error);
		response.Guide!.ExistingMobilePages.Should().BeEmpty(
			because: "the derived target name ('Lead_MobileFormPage') equals the entity's already-registered "
				+ "default mobile page, so it must be excluded as a self-match rather than offered for reuse");
	}

	[Test]
	[Description("An explicit --target-schema-name (with surrounding whitespace) must reach the probe TRIMMED: the entity's already-registered default mobile page is excluded as a self-match only when it equals the TRIMMED value, proving both that the explicit override reaches the probe and that it is trimmed before the comparison.")]
	public async Task GetGuide_ExplicitTargetSchemaName_ExcludesSelfMatch_UsingTrimmedValue() {
		// Arrange
		var tool = new StubbedEntityProbeTool(existingMobilePageName: "Custom_MobilePage");

		// Act
		MobilePageConversionGuideResponse response = await tool.GetMobilePageConversionGuide(
			EntityArgs(targetSchemaName: "  Custom_MobilePage  "));

		// Assert
		response.Success.Should().BeTrue(because: "the run must complete: " + response.Error);
		response.Guide!.ExistingMobilePages.Should().BeEmpty(
			because: "the explicit target-schema-name, TRIMMED, equals the entity's already-registered default "
				+ "mobile page, so it must be excluded as a self-match — an untrimmed comparison, or one that "
				+ "silently ignored the explicit override, would leave this match unexcluded");
	}

	[Test]
	[Description("Control case: when the resolved target name does NOT match the entity's existing default mobile page, the match is reported (not excluded) — without this, the two tests above could pass on a probe that always excludes everything, which is the same vacuity the refusal-wiring tests guard against for the other hard-stop gates.")]
	public async Task GetGuide_TargetNameDoesNotMatchExistingPage_ReportsTheMatch() {
		// Arrange — the registered page's name matches neither the derived name nor any explicit override used here.
		var tool = new StubbedEntityProbeTool(existingMobilePageName: "SomeUnrelated_MobilePage");

		// Act
		MobilePageConversionGuideResponse response = await tool.GetMobilePageConversionGuide(EntityArgs(targetSchemaName: null));

		// Assert
		response.Success.Should().BeTrue(because: "the run must complete: " + response.Error);
		response.Guide!.ExistingMobilePages.Should().ContainSingle(
			p => p.SchemaName == "SomeUnrelated_MobilePage",
			because: "an unrelated existing default mobile page is a genuine reuse candidate and must be "
				+ "reported — proving the exclusion asserted above is a real self-match check, not a probe that "
				+ "never reports anything");
	}

	/// <remarks>
	/// An explicit <c>Version</c> short-circuits ResolveVersionAsync before it reaches the environment
	/// resolver, so these tests need no settings repository or version-probe stub — version resolution is a
	/// different concern with its own coverage, and mocking it here would only add a way for these tests to
	/// fail for a reason that is not the one they are about.
	/// </remarks>
	private static MobilePageConversionGuideArgs Args() =>
		new(SourcePage, TargetSchemaName: null, Version: "8.3.3", EnvironmentName: "unit-test-env");

	private static MobilePageConversionGuideArgs EntityArgs(string targetSchemaName) =>
		new(EntitySourcePage, TargetSchemaName: targetSchemaName, Version: "8.3.3", EnvironmentName: "unit-test-env");

	/// <summary>A successful SelectQuery envelope carrying <paramref name="rows"/> (raw JSON objects).</summary>
	private static string Rows(params string[] rows) => $"{{\"success\":true,\"rows\":[{string.Join(",", rows)}]}}";

	/// <summary>
	/// The tool with every page read answered in-memory. <paramref name="unreadable"/> names the ONE schema
	/// whose read fails; everything else succeeds, so each test isolates a single refusal.
	/// </summary>
	private sealed class StubbedTool : MobilePageConversionGuideTool {

		private readonly string _unreadable;

		private readonly bool _templateWithoutViewConfig;

		internal StubbedTool(string unreadable, bool templateWithoutViewConfig = false)
			: base(Substitute.For<IToolCommandResolver>(), Substitute.For<ILogger>(),
				MobileCatalog(), WebCatalog(), RulesCatalog(),
				Substitute.For<IPlatformVersionResolverFactory>(), Substitute.For<ISettingsRepository>()) {
			_unreadable = unreadable;
			_templateWithoutViewConfig = templateWithoutViewConfig;
		}

		internal override PageGetResponse ReadPageUnderTenantLock(PageGetOptions options) {
			if (string.Equals(options.SchemaName, _unreadable, StringComparison.OrdinalIgnoreCase)) {
				return new PageGetResponse { Success = false, Error = "simulated read failure" };
			}
			if (string.Equals(options.SchemaName, SourcePage, StringComparison.OrdinalIgnoreCase)) {
				return SourcePageResponse();
			}
			return _templateWithoutViewConfig
				&& string.Equals(options.SchemaName, MobileTemplate, StringComparison.OrdinalIgnoreCase)
				? TemplateWithoutViewConfigResponse()
				: TemplateResponse();
		}
	}

	/// <summary>
	/// A tool whose entity-default-mobile-page probe chain (<c>SysSchema</c> by name, the
	/// <c>MobileRelatedPage</c> add-on, <c>SysSchema</c> by UId) is fully wired to answer with ONE existing
	/// default mobile page named <paramref name="existingMobilePageName"/> for the bound entity — everything
	/// else (templates, source page read) mirrors <see cref="StubbedTool"/>'s happy path.
	/// </summary>
	private sealed class StubbedEntityProbeTool : MobilePageConversionGuideTool {

		internal StubbedEntityProbeTool(string existingMobilePageName)
			: base(BuildEntityProbeResolver(existingMobilePageName), Substitute.For<ILogger>(),
				MobileCatalog(), WebCatalog(), RulesCatalog(),
				Substitute.For<IPlatformVersionResolverFactory>(), Substitute.For<ISettingsRepository>()) {
		}

		internal override PageGetResponse ReadPageUnderTenantLock(PageGetOptions options) =>
			string.Equals(options.SchemaName, EntitySourcePage, StringComparison.OrdinalIgnoreCase)
				? EntitySourcePageResponse()
				: TemplateResponse();
	}

	/// <summary>
	/// Routes every <c>SysSchema</c> read the entity probe chain issues: the entity-by-NAME lookup (its query
	/// carries the <c>EntitySchemaManager</c> manager filter) resolves <see cref="BoundEntity"/> to
	/// <see cref="EntityUId"/>; every other read is <see cref="SchemaNameResolver"/>'s by-UId lookup (no
	/// manager filter), answered with <paramref name="existingMobilePageName"/> regardless of which UId it
	/// asked for — this test only ever has one candidate UId in flight.
	/// </summary>
	private static IToolCommandResolver BuildEntityProbeResolver(string existingMobilePageName) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(callInfo => {
				string query = callInfo.ArgAt<string>(1);
				return query.Contains("EntitySchemaManager", StringComparison.Ordinal)
					? Rows(
						$$"""{"Name":"{{BoundEntity}}","UId":"{{EntityUId}}","PackageUId":"{{PackageUId}}","ExtendParent":false}""")
					: Rows($$"""{"UId":"{{CandidatePageUId}}","Name":"{{existingMobilePageName}}"}""");
			});

		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<string>()).Returns(callInfo => callInfo.Arg<string>());
		urlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns("/DataService/json/SyncReply/SelectQuery");

		IAddonSchemaDesignerClient addonClient = Substitute.For<IAddonSchemaDesignerClient>();
		addonClient.GetSchema(Arg.Any<AddonGetRequestDto>())
			.Returns(new AddonSchemaDto {
				MetaData = $$"""{"Pages":[{"PageSchemaUId":"{{CandidatePageUId}}","IsDefault":true}]}"""
			});

		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		resolver.Resolve<IAddonSchemaDesignerClient>(Arg.Any<EnvironmentOptions>()).Returns(addonClient);
		return resolver;
	}

	private static PageGetResponse SourcePageResponse() => new() {
		Success = true,
		Page = new PageMetadataInfo { SchemaName = SourcePage, SchemaType = "web", ParentSchemaName = WebTemplate },
		Bundle = new PageBundleInfo {
			ViewConfig = JsonNode.Parse("""
				[ { "name": "MainContainer", "type": "crt.FlexContainer", "items": [
					{ "name": "UsrName", "type": "crt.Input", "label": "Name" } ] } ]
				""")!.AsArray(),
			ViewModelConfig = new JsonObject(),
			ModelConfig = new JsonObject(),
			Resources = new PageResourceInfo()
		}
	};

	/// <summary>
	/// The entity-bound source page: ends with "FormPage" (so <c>IsFormPage</c> is true without needing the
	/// template list), bound to <see cref="BoundEntity"/> as its primary data source, and carries no request
	/// bindings — so <c>MobileActionTargetProbe</c> finds no occurrences and never touches the environment,
	/// leaving this test's assertions entirely about the entity-default-mobile-page reuse check.
	/// </summary>
	private static PageGetResponse EntitySourcePageResponse() => new() {
		Success = true,
		Page = new PageMetadataInfo {
			SchemaName = EntitySourcePage, SchemaType = "web", ParentSchemaName = WebTemplate, PackageUId = PackageUId
		},
		Bundle = new PageBundleInfo {
			ViewConfig = JsonNode.Parse("""
				[ { "name": "MainContainer", "type": "crt.FlexContainer", "items": [
					{ "name": "UsrName", "type": "crt.Input", "label": "Name" } ] } ]
				""")!.AsArray(),
			ViewModelConfig = new JsonObject(),
			ModelConfig = JsonNode.Parse($$"""
				{
				  "primaryDataSourceName": "PDS",
				  "dataSources": { "PDS": { "type": "crt.EntityDataSource",
				    "config": { "entitySchemaName": "{{BoundEntity}}" } } }
				}
				""")!.AsObject(),
			Resources = new PageResourceInfo()
		}
	};

	private static PageGetResponse TemplateResponse() => new() {
		Success = true,
		Page = new PageMetadataInfo { SchemaName = MobileTemplate, SchemaType = "web" },
		Bundle = new PageBundleInfo {
			ViewConfig = JsonNode.Parse("""
				[ { "name": "MainContainer", "type": "crt.GridContainer", "items": [] } ]
				""")!.AsArray(),
			ViewModelConfig = new JsonObject(),
			ModelConfig = new JsonObject(),
			Resources = new PageResourceInfo()
		}
	};

	/// <summary>A read that SUCCEEDS and returns a bundle whose viewConfig is null.</summary>
	private static PageGetResponse TemplateWithoutViewConfigResponse() => new() {
		Success = true,
		Page = new PageMetadataInfo { SchemaName = MobileTemplate, SchemaType = "web" },
		Bundle = new PageBundleInfo {
			ViewConfig = null,
			ViewModelConfig = new JsonObject(),
			ModelConfig = new JsonObject(),
			Resources = new PageResourceInfo()
		}
	};

	private static IMobileComponentInfoCatalog MobileCatalog() {
		IMobileComponentInfoCatalog catalog = Substitute.For<IMobileComponentInfoCatalog>();
		catalog.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(State(
				Entries(("crt.FlexContainer", true), ("crt.GridContainer", true), ("crt.Input", false)))));
		return catalog;
	}

	private static IComponentInfoCatalog WebCatalog() {
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();
		catalog.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(State(Entries(("crt.FlexContainer", true), ("crt.Input", false)))));
		return catalog;
	}

	private static IWebToMobilePageConversionRulesCatalog RulesCatalog() {
		IWebToMobilePageConversionRulesCatalog catalog = Substitute.For<IWebToMobilePageConversionRulesCatalog>();
		catalog.GetRulesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(new WebToMobilePageConversionRules {
				Templates = [new TemplateMappingRule { Web = WebTemplate, Mobile = MobileTemplate }]
			}));
		return catalog;
	}

	private static ComponentCatalogState State(IReadOnlyList<ComponentRegistryEntry> entries) {
		var lookup = new Dictionary<string, ComponentRegistryEntry>(StringComparer.OrdinalIgnoreCase);
		foreach (ComponentRegistryEntry entry in entries) {
			lookup[entry.ComponentType] = entry;
		}
		return new ComponentCatalogState(entries, lookup, "latest", ComponentRegistrySource.FileCache);
	}

	private static IReadOnlyList<ComponentRegistryEntry> Entries(params (string Type, bool Container)[] types) {
		var entries = new List<ComponentRegistryEntry>();
		foreach ((string type, bool container) in types) {
			entries.Add(new ComponentRegistryEntry { ComponentType = type, Container = container });
		}
		return entries;
	}
}
