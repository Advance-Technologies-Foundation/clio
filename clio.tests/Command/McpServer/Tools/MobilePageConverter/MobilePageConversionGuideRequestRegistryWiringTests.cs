using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.MobilePageConverter;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter;

/// <summary>
/// Pins that the mobile request registry the TOOL loads actually reaches the analysis. The support decision
/// itself is covered by <c>WebToMobileConversionServiceTests</c>, which calls the service directly with a
/// hand-built set — so it cannot see the one mistake that matters here: dropping
/// <c>mobileRequestTypes: mobileRequestTypes</c> from the tool's Analyze call. Every unit test would still
/// pass, and every request the registry alone supports would silently go back to being a dropped action.
/// </summary>
/// <remarks>
/// The subject is a request the bundled rules do NOT map. Under the deleted <c>MobileSupportedRequests</c>
/// constant such a request was unsupported; the registry is now the only thing that can keep it. The control
/// case runs the same page against a registry that does not publish it, so a tool that treated everything as
/// supported would fail there rather than pass both.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class MobilePageConversionGuideRequestRegistryWiringTests {

	private const string SourcePage = "UsrLead_FormPage";
	private const string WebTemplate = "WebTemplate";
	private const string MobileTemplate = "MobileTemplate";

	/// <summary>A request no bundled rules entry maps, so only the registry can make it supported.</summary>
	private const string RegistryOnlyRequest = "crt.PhoneLinkClickRequest";

	private const string ButtonName = "UsrCallButton";

	[Test]
	[Description("A request the rules do not map survives when the mobile request registry publishes it. This is the wiring the tool owns: the catalog it loads has to reach Analyze. Dropping the argument leaves every unit test green while the binding silently becomes a dropped action on the page.")]
	public async Task GetGuide_ShouldKeepTheBinding_WhenOnlyTheRequestRegistryPublishesTheRequest() {
		// Arrange
		var tool = new StubbedTool(publishedRequests: [RegistryOnlyRequest]);

		// Act
		MobilePageConversionGuideResponse response = await tool.GetMobilePageConversionGuide(Args());

		// Assert
		response.Success.Should().BeTrue(because: "the run must complete: " + response.Error);
		Values(response, ButtonName).Should().ContainKey("clicked",
			because: "the registry publishes the request, so the action is supported and its binding is carried "
				+ "over — which can only happen if the loaded registry reached the analysis");
	}

	[Test]
	[Description("The same page against a registry that does not publish the request loses the button entirely — its only action is dead, so the control goes with it. Without this control the test above would pass on a tool that treated every request as supported, which is the opposite failure and just as silent.")]
	public async Task GetGuide_ShouldDropTheBinding_WhenTheRequestRegistryDoesNotPublishTheRequest() {
		// Arrange
		var tool = new StubbedTool(publishedRequests: []);

		// Act
		MobilePageConversionGuideResponse response = await tool.GetMobilePageConversionGuide(Args());

		// Assert
		response.Success.Should().BeTrue(because: "an unsupported action is a pruned binding, not a failure: "
			+ response.Error);
		response.Guide!.ViewConfigDiff.Select(o => o.Name).Should().NotContain(ButtonName,
			because: "the button's only action is unsupported, so the button itself is a dead control and the "
				+ "converter drops it rather than inserting a button that does nothing");
	}

	private static MobilePageConversionGuideArgs Args() =>
		new(SourcePage, TargetSchemaName: null, Version: null, EnvironmentName: "unit-test-env");

	private static JsonObject Values(MobilePageConversionGuideResponse response, string name) {
		response.Guide.Should().NotBeNull(because: "every assertion that calls this reads the guide");
		return response.Guide!.ViewConfigDiff.Single(o => o.Name == name).Values!.AsObject();
	}

	/// <summary>The tool with every page read answered in-memory and a caller-supplied published request set.</summary>
	private sealed class StubbedTool : MobilePageConversionGuideTool {

		internal StubbedTool(IReadOnlyList<string> publishedRequests)
			: base(Substitute.For<IToolCommandResolver>(), Substitute.For<ILogger>(),
				MobileCatalog(), WebCatalog(), MobileRequestCatalog(publishedRequests), RulesCatalog(),
				VersionResolverFactory(), SettingsRepository()) { }

		internal override PageGetResponse ReadPageUnderTenantLock(PageGetOptions options) =>
			string.Equals(options.SchemaName, SourcePage, StringComparison.OrdinalIgnoreCase)
				? SourcePageResponse()
				: TemplateResponse();
	}

	private static ISettingsRepository SettingsRepository() {
		ISettingsRepository repository = Substitute.For<ISettingsRepository>();
		repository.GetEnvironment(Arg.Any<EnvironmentOptions>()).Returns(new EnvironmentSettings());
		return repository;
	}

	private static IPlatformVersionResolverFactory VersionResolverFactory() {
		IPlatformVersionResolver resolver = Substitute.For<IPlatformVersionResolver>();
		resolver.ResolveAsync(Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(new PlatformVersionResolution("latest", VersionResolutionSource.LatestFallback)));
		IPlatformVersionResolverFactory factory = Substitute.For<IPlatformVersionResolverFactory>();
		factory.Create(Arg.Any<EnvironmentSettings>()).Returns(resolver);
		return factory;
	}

	private static PageGetResponse SourcePageResponse() => new() {
		Success = true,
		Page = new PageMetadataInfo { SchemaName = SourcePage, SchemaType = "web", ParentSchemaName = WebTemplate },
		Bundle = new PageBundleInfo {
			ViewConfig = JsonNode.Parse($$"""
				[ { "name": "MainContainer", "type": "crt.FlexContainer", "items": [
					{ "name": "{{ButtonName}}", "type": "crt.Button", "caption": "Call",
					  "clicked": { "request": "{{RegistryOnlyRequest}}" } } ] } ]
				""")!.AsArray(),
			ViewModelConfig = new JsonObject(),
			ModelConfig = new JsonObject(),
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

	private static IMobileComponentInfoCatalog MobileCatalog() {
		IMobileComponentInfoCatalog catalog = Substitute.For<IMobileComponentInfoCatalog>();
		catalog.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(State(Entries(
				("crt.FlexContainer", new[] { "items" }),
				("crt.GridContainer", new[] { "items", "columns" }),
				("crt.Button", new[] { "caption", "clicked" })))));
		return catalog;
	}

	private static IComponentInfoCatalog WebCatalog() {
		IComponentInfoCatalog catalog = Substitute.For<IComponentInfoCatalog>();
		catalog.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(State(Entries(
				("crt.FlexContainer", new[] { "items" }),
				("crt.Button", new[] { "caption", "clicked" })))));
		return catalog;
	}

	/// <summary>The registry under test: exactly the request types the caller says it publishes.</summary>
	private static IMobileRequestInfoCatalog MobileRequestCatalog(IReadOnlyList<string> publishedRequests) {
		var entries = publishedRequests
			.Select(type => new RequestRegistryEntry { RequestType = type })
			.ToList();
		var lookup = new Dictionary<string, RequestRegistryEntry>(StringComparer.OrdinalIgnoreCase);
		foreach (RequestRegistryEntry entry in entries) {
			lookup[entry.RequestType] = entry;
		}
		IMobileRequestInfoCatalog catalog = Substitute.For<IMobileRequestInfoCatalog>();
		catalog.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(new RequestCatalogState(
				entries, lookup, "latest", ComponentRegistrySource.FileCache, null)));
		return catalog;
	}

	/// <summary>Templates only: the rules must not be what makes the subject request supported.</summary>
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
		return new ComponentCatalogState(entries, lookup, "latest", ComponentRegistrySource.FileCache, null);
	}

	private static IReadOnlyList<ComponentRegistryEntry> Entries(params (string Type, string[] Inputs)[] types) {
		var entries = new List<ComponentRegistryEntry>();
		foreach ((string type, string[] inputs) in types) {
			var declared = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
			foreach (string input in inputs) {
				declared[input] = JsonSerializer.SerializeToElement(new { type = "unknown" });
			}
			entries.Add(new ComponentRegistryEntry {
				ComponentType = type,
				Container = inputs.Contains("items"),
				Inputs = declared
			});
		}
		return entries;
	}
}
