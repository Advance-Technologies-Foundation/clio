namespace Clio.Tests.Command.McpServer.Tools.MobilePageConverter;

using System;
using System.Collections.Generic;
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
