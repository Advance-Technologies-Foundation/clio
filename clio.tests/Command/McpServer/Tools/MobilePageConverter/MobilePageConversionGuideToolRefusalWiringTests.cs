namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.MobilePageConverter;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

/// <summary>
/// Drives <see cref="MobilePageConversionGuideTool.GetMobilePageConversionGuide"/> end to end so the two
/// hard-stop refusals are asserted WHERE THEY ARE WIRED, not where they are defined.
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
/// The seam is <see cref="MobilePageConversionGuideTool.ReadPageUnderTenantLock"/>: overriding it decides
/// what every page read in the method returns — the source page, the mobile template, the web template —
/// which is exactly the surface the two refusals key on.
/// </para>
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class MobilePageConversionGuideToolRefusalWiringTests {

	private const string SourcePage = "UsrLead_FormPage";
	private const string WebTemplate = "PageWithTabsFreedomTemplate";
	private const string MobileTemplate = "BaseMobilePageTemplate";

	private const string MobileComponentCatalog = "mobile-components";

	private const string WebComponentCatalog = "web-components";

	private const string MobileRequestCatalog = "mobile-requests";

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

	[TestCase(MobileComponentCatalog, "MOBILE component catalog")]
	[TestCase(WebComponentCatalog, "WEB component catalog")]
	[TestCase(MobileRequestCatalog, "MOBILE request catalog")]
	[Description("A catalog whose load fails — a cold cache with no network — comes back as the tool's own failure result naming the catalog, not as an exception escaping the tool. All three loads sat between two try blocks, so this state produced a thrown MCP error instead of the structured failure every other unreachable-dependency path here returns.")]
	public async Task GetGuide_ShouldFail_WhenACatalogCannotBeLoaded(string failingCatalog, string expectedInError) {
		// Arrange
		var tool = new StubbedTool(unreadable: null, failingCatalog: failingCatalog);

		// Act
		MobilePageConversionGuideResponse response = await tool.GetMobilePageConversionGuide(Args());

		// Assert
		response.Success.Should().BeFalse(
			because: "a catalog the tool cannot read leaves it with no basis for any recommendation");
		response.Error.Should().Contain(expectedInError,
			because: "naming WHICH catalog failed is what tells the caller whether to retry, go online, or check the version");
		response.Guide.Should().BeNull(
			because: "a failure that still carried a guide would be acted on as though it were usable");
	}

	[Test]
	[Description("A rules entry that targets a mobile request the registry does not publish reaches the caller as rulesWarning. The check existed but was called from nowhere, so such an entry was reported only to a unit test on the helper and never to the caller — while on the page the converted binding becomes a request the app cannot dispatch and fails silently.")]
	public async Task GetGuide_ShouldReportRulesWarning_WhenARulesEntryTargetsAnUnpublishedMobileRequest() {
		// Arrange
		var tool = new StubbedTool(unreadable: null, rules: RulesFlavor.UnknownRequestTarget);

		// Act
		MobilePageConversionGuideResponse response = await tool.GetMobilePageConversionGuide(Args());

		// Assert
		response.Success.Should().BeTrue(
			because: "an unresolvable rules target is a caveat on the guide, not a reason to withhold it. Error: "
				+ response.Error);
		response.RulesWarning.Should().NotBeNull(
			because: "the offending entry has to be visible to whoever acts on the guide")
			.And.Contain("crt.LegacyOpenRequest -> crt.GhostRequest",
				because: "naming the web -> mobile pair is what makes the warning actionable");
	}

	[Test]
	[Description("The SHIPPED rules against the pinned live mobile request registry leave rulesWarning null. Without this control the test above would pass on a tool that warned unconditionally, and a warning present on every conversion is one nobody reads.")]
	public async Task GetGuide_ShouldLeaveRulesWarningNull_WhenTheRegistryPublishesEveryShippedRulesTarget() {
		// Arrange
		var tool = new StubbedTool(unreadable: null, rules: RulesFlavor.Shipped);

		// Act
		MobilePageConversionGuideResponse response = await tool.GetMobilePageConversionGuide(Args());

		// Assert
		response.Success.Should().BeTrue(
			because: "nothing about the shipped rules blocks a guide. Error: " + response.Error);
		response.RulesWarning.Should().BeNull(
			because: "every shipped rules entry names a request the live registry publishes, so there is nothing to warn about");
	}

	/// <remarks>
	/// An explicit <c>Version</c> short-circuits ResolveVersionAsync before it reaches the environment
	/// resolver, so these tests need no settings repository or version-probe stub — version resolution is a
	/// different concern with its own coverage, and mocking it here would only add a way for these tests to
	/// fail for a reason that is not the one they are about.
	/// </remarks>
	private static MobilePageConversionGuideArgs Args() =>
		new(SourcePage, TargetSchemaName: null, Version: "8.3.3", EnvironmentName: "unit-test-env");

	/// <summary>
	/// The tool with every page read answered in-memory. <paramref name="unreadable"/> names the ONE schema
	/// whose read fails; everything else succeeds, so each test isolates a single refusal.
	/// </summary>
	private sealed class StubbedTool : MobilePageConversionGuideTool {

		private readonly string _unreadable;

		private readonly bool _templateWithoutViewConfig;

		internal StubbedTool(string unreadable, bool templateWithoutViewConfig = false,
			string failingCatalog = null, RulesFlavor rules = RulesFlavor.Minimal)
			: base(Substitute.For<IToolCommandResolver>(), Substitute.For<ILogger>(),
				MobileCatalog(failingCatalog), WebCatalog(failingCatalog),
				MobileRequestCatalogStub(failingCatalog, rules), RulesCatalog(rules),
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

	private static PageGetResponse SourcePageResponse() => new() {
		Success = true,
		Page = new PageMetadataInfo { SchemaName = SourcePage, SchemaType = "web", ParentSchemaName = WebTemplate },
		Bundle = new PageBundleInfo {
			ViewConfig = System.Text.Json.Nodes.JsonNode.Parse("""
				[ { "name": "MainContainer", "type": "crt.FlexContainer", "items": [
					{ "name": "UsrName", "type": "crt.Input", "label": "Name" } ] } ]
				""")!.AsArray(),
			ViewModelConfig = new System.Text.Json.Nodes.JsonObject(),
			ModelConfig = new System.Text.Json.Nodes.JsonObject(),
			Resources = new PageResourceInfo()
		}
	};

	private static PageGetResponse TemplateResponse() => new() {
		Success = true,
		Page = new PageMetadataInfo { SchemaName = MobileTemplate, SchemaType = "web" },
		Bundle = new PageBundleInfo {
			ViewConfig = System.Text.Json.Nodes.JsonNode.Parse("""
				[ { "name": "MainContainer", "type": "crt.GridContainer", "items": [] } ]
				""")!.AsArray(),
			ViewModelConfig = new System.Text.Json.Nodes.JsonObject(),
			ModelConfig = new System.Text.Json.Nodes.JsonObject(),
			Resources = new PageResourceInfo()
		}
	};

	/// <summary>A read that SUCCEEDS and returns a bundle whose viewConfig is null.</summary>
	private static PageGetResponse TemplateWithoutViewConfigResponse() => new() {
		Success = true,
		Page = new PageMetadataInfo { SchemaName = MobileTemplate, SchemaType = "web" },
		Bundle = new PageBundleInfo {
			ViewConfig = null,
			ViewModelConfig = new System.Text.Json.Nodes.JsonObject(),
			ModelConfig = new System.Text.Json.Nodes.JsonObject(),
			Resources = new PageResourceInfo()
		}
	};

	private static IMobileComponentInfoCatalog MobileCatalog(string failingCatalog) {
		IMobileComponentInfoCatalog catalog = Substitute.For<IMobileComponentInfoCatalog>();
		catalog.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(failingCatalog == MobileComponentCatalog
				? Task.FromException<ComponentCatalogState>(LoadFailure(MobileComponentCatalog))
				: Task.FromResult(State(
					Entries(("crt.FlexContainer", true), ("crt.GridContainer", true), ("crt.Input", false)))));
		return catalog;
	}

	private static IComponentInfoCatalog WebCatalog(string failingCatalog) {
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();
		catalog.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(failingCatalog == WebComponentCatalog
				? Task.FromException<ComponentCatalogState>(LoadFailure(WebComponentCatalog))
				: Task.FromResult(State(Entries(("crt.FlexContainer", true), ("crt.Input", false)))));
		return catalog;
	}

	/// <summary>The failure a catalog load hits on a cold cache with no network.</summary>
	private static Exception LoadFailure(string catalogName) =>
		new InvalidOperationException($"simulated {catalogName} load failure");

	/// <summary>
	/// A real <see cref="RequestCatalogState"/>, not a bare substitute: the tool awaits
	/// <c>LoadAsync</c> and reads <c>ResolvedVersion</c> and <c>Entries</c> off the result, so a null
	/// would NRE before any refusal could be reached.
	/// </summary>
	private static IMobileRequestInfoCatalog MobileRequestCatalogStub(string failingCatalog, RulesFlavor rules) {
		IMobileRequestInfoCatalog catalog = Substitute.For<IMobileRequestInfoCatalog>();
		catalog.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(failingCatalog == MobileRequestCatalog
				? Task.FromException<RequestCatalogState>(LoadFailure(MobileRequestCatalog))
				: Task.FromResult(RequestState(PublishedRequestTypes(rules))));
		return catalog;
	}

	/// <summary>Which rules entries and mobile request registry the stub serves; they only make sense as a pair.</summary>
	private enum RulesFlavor {
		/// <summary>Templates only, over a two-entry registry — the shape the refusal tests need.</summary>
		Minimal,
		/// <summary>One rules entry targets a mobile request the registry does not publish.</summary>
		UnknownRequestTarget,
		/// <summary>The SHIPPED rules entries over the pinned live mobile request registry.</summary>
		Shipped
	}

	private static IWebToMobilePageConversionRulesCatalog RulesCatalog(RulesFlavor rules) {
		IWebToMobilePageConversionRulesCatalog catalog = Substitute.For<IWebToMobilePageConversionRulesCatalog>();
		catalog.GetRulesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(new WebToMobilePageConversionRules {
				Templates = [new TemplateMappingRule { Web = WebTemplate, Mobile = MobileTemplate }],
				Requests = RequestRules(rules)
			}));
		return catalog;
	}

	private static IReadOnlyList<RequestMappingRule> RequestRules(RulesFlavor rules) => rules switch {
		RulesFlavor.UnknownRequestTarget => [
			new RequestMappingRule { Web = "crt.SaveRecordRequest", Mobile = "crt.SaveRecordRequest" },
			new RequestMappingRule { Web = "crt.LegacyOpenRequest", Mobile = "crt.GhostRequest" }
		],
		RulesFlavor.Shipped => WebToMobilePageConversionRulesCatalog.LoadBundled().Requests,
		_ => []
	};

	/// <summary>
	/// The request types the stubbed registry publishes. The <see cref="RulesFlavor.Shipped"/> pair reads
	/// <c>MobileRequestRegistry.published-types.json</c> — the live CDN registry's type set captured for
	/// MEMBERSHIP CHECKS ONLY, carrying no descriptions, parameters or doc links — so the null-warning control
	/// asserts the shipped rules against the registry the runtime really ships and not against themselves.
	/// </summary>
	private static string[] PublishedRequestTypes(RulesFlavor rules) {
		if (rules != RulesFlavor.Shipped) {
			return ["crt.SaveRecordRequest", "crt.ClosePageRequest"];
		}
		string fixturePath = Path.Combine(
			TestContext.CurrentContext.TestDirectory,
			"Command/McpServer/Fixtures/MobileRequestRegistry.published-types.json");
		using FileStream stream = File.OpenRead(fixturePath);
		return [.. RequestInfoCatalog.LoadFromStream(stream).Entries.Select(entry => entry.RequestType)];
	}

	private static RequestCatalogState RequestState(params string[] requestTypes) {
		var entries = new List<RequestRegistryEntry>();
		var lookup = new Dictionary<string, RequestRegistryEntry>(StringComparer.OrdinalIgnoreCase);
		foreach (string requestType in requestTypes) {
			var entry = new RequestRegistryEntry { RequestType = requestType };
			entries.Add(entry);
			lookup[requestType] = entry;
		}
		return new RequestCatalogState(entries, lookup, "latest", ComponentRegistrySource.FileCache);
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
