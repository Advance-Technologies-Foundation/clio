using System;
using System.Linq;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Knowledge;
using Clio.Command.McpServer.Resources;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class KnowledgeGuidanceSurfaceTests {
	private ServiceProvider _container;
	private IFeatureToggleService _featureToggleService;
	private IKnowledgeBundleActivator _activator;
	private IKnowledgeBundleRuntime _runtime;
	private GuidanceGetTool _tool;
	private KnowledgeGuidanceResourceAdapter _adapter;

	[SetUp]
	public void SetUp() {
		_featureToggleService = Substitute.For<IFeatureToggleService>();
		_activator = Substitute.For<IKnowledgeBundleActivator>();
		_runtime = Substitute.For<IKnowledgeBundleRuntime>();
		ServiceCollection services = new();
		services.AddSingleton(_featureToggleService);
		services.AddSingleton(_activator);
		services.AddSingleton(_runtime);
		services.AddSingleton<IKnowledgeGuidanceSource, KnowledgeGuidanceSource>();
		services.AddSingleton<KnowledgeGuidanceResourceAdapter>();
		services.AddTransient<GuidanceGetTool>();
		_container = services.BuildServiceProvider();
		_tool = _container.GetRequiredService<GuidanceGetTool>();
		_adapter = _container.GetRequiredService<KnowledgeGuidanceResourceAdapter>();
	}

	[TearDown]
	public void TearDown() {
		_container.Dispose();
	}

	[Test]
	[Description("Routes the same active synthetic article through stable get-guidance and docs URI surfaces.")]
	public async Task GuidanceSurfaces_ShouldReturnSameSyntheticArticle_WhenBundleIsActive() {
		// Arrange
		const string name = "esq-filters";
		const string uri = "docs://mcp/guides/esq-filters";
		const string text = "Synthetic delivery fixture.\n";
		_runtime.Find(name).Returns(new KnowledgeArticleLookup(
			KnowledgeArticleLookupStatus.Active, new KnowledgeArticle(name, uri, text), 3));

		// Act
		GuidanceGetResponse toolResponse = await _tool.GetGuidance(new GuidanceGetArgs(name.ToUpperInvariant()));
		TextResourceContents resource = _adapter.Get(uri).Should().BeOfType<TextResourceContents>(
			because: "an active text article must remain a text MCP resource").Which;

		// Assert
		toolResponse.Success.Should().BeTrue(
			because: "the active external article must be routable by a case-insensitive stable name");
		toolResponse.Article!.Uri.Should().Be(uri,
			because: "get-guidance must preserve the stable external resource URI");
		toolResponse.Article.Text.Should().Be(text,
			because: "get-guidance must preserve the synthetic verified payload");
		resource.Uri.Should().Be(uri,
			because: "direct resource routing must preserve the same stable URI");
		resource.Text.Should().Be(text,
			because: "direct resource routing must serve the same synthetic payload");
		_activator.ReceivedCalls().Should().HaveCount(2,
			because: "both lazy surfaces must request activation before consulting the external runtime");
	}

	[Test]
	[Description("Returns typed unavailability on get-guidance and docs routing when the verified bundle is cold.")]
	public async Task GuidanceSurfaces_ShouldFailClosed_WhenBundleIsUnavailable() {
		// Arrange
		_runtime.Find("esq-filters").Returns(new KnowledgeArticleLookup(
			KnowledgeArticleLookupStatus.Unavailable, null, null));

		// Act
		GuidanceGetResponse response = await _tool.GetGuidance(new GuidanceGetArgs("esq-filters"));
		Action readResource = () => _adapter.Get("docs://mcp/guides/esq-filters");

		// Assert
		response.Success.Should().BeFalse(because: "cold external knowledge must never look successful");
		response.ErrorCode.Should().Be(KnowledgeGuidanceUnavailableException.ErrorCode,
			because: "tool clients must distinguish cold guidance from an unknown identifier");
		response.Article.Should().BeNull(because: "unavailable guidance must never become empty fallback content");
		McpProtocolException exception = readResource.Should().Throw<McpProtocolException>(
			because: "direct docs reads must expose a safe MCP wire error rather than a masked server exception").Which;
		exception.ErrorCode.Should().Be(McpErrorCode.InternalError,
			because: "a known but unavailable resource is an operational failure, not an unknown URI");
		exception.Message.Should().Contain(KnowledgeGuidanceUnavailableException.ErrorCode,
			because: "resource clients need the same typed unavailable code as get-guidance clients");
	}

	[Test]
	[Description("Treats a catalog-known external guide missing from an active runtime snapshot as unavailable, not unknown.")]
	public async Task GetGuidance_ShouldFailClosed_WhenActiveRuntimeIsMissingKnownExternalGuide() {
		// Arrange
		_runtime.Find("esq-filters").Returns(new KnowledgeArticleLookup(
			KnowledgeArticleLookupStatus.NotFound, null, 4));

		// Act
		GuidanceGetResponse response = await _tool.GetGuidance(new GuidanceGetArgs("esq-filters"));

		// Assert
		response.ErrorCode.Should().Be(KnowledgeGuidanceUnavailableException.ErrorCode,
			because: "a partial active bundle must fail closed instead of downgrading a known guide to unknown");
		response.Article.Should().BeNull(
			because: "a partial active bundle must never synthesize permissive fallback content");
	}

	[Test]
	[Description("Keeps a gated guidance route hidden until its owning executable feature is enabled.")]
	public async Task GetGuidance_ShouldRespectFeatureVisibility_WhenGuideIsFeatureGated() {
		// Arrange
		IKnowledgeGuidanceSource source = _container.GetRequiredService<IKnowledgeGuidanceSource>();

		// Act
		GuidanceGetResponse disabled = await _tool.GetGuidance(new GuidanceGetArgs("process-modeling"));
		_featureToggleService.IsEnabled(Arg.Any<Type>()).Returns(true);
		GuidanceGetResponse enabled = await _tool.GetGuidance(new GuidanceGetArgs("process-modeling"));

		// Assert
		disabled.ErrorCode.Should().Be("guidance-not-found",
			because: "disabled executable surfaces must not leak their gated guide through stable routing");
		enabled.Success.Should().BeTrue(
			because: "enabling the owning executable feature must restore its content-neutral route");
		source.GetNames().Should().Contain("process-modeling",
			because: "the enabled guide must be discoverable through the same catalog mechanics");
	}

	[Test]
	[Description("Keeps an unknown stable name distinct from unavailable external delivery.")]
	public async Task GetGuidance_ShouldReturnNotFound_WhenNameIsNotInCatalog() {
		// Arrange
		_runtime.ActiveSequence.Returns((ulong?)5);

		// Act
		GuidanceGetResponse response = await _tool.GetGuidance(new GuidanceGetArgs("synthetic-missing-guide"));

		// Assert
		response.Success.Should().BeFalse(because: "an unregistered identifier cannot return guidance");
		response.ErrorCode.Should().Be("guidance-not-found",
			because: "catalog absence must stay distinct from a cold external bundle");
		_activator.ReceivedCalls().Should().BeEmpty(
			because: "unknown catalog names must not trigger bundle discovery or download");
	}
}
