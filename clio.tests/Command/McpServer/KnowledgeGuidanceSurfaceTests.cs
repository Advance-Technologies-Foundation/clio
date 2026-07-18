using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Knowledge;
using Clio.Command.McpServer.Resources;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class KnowledgeGuidanceSurfaceTests {
	private IKnowledgeBundleActivator _activator;
	private IKnowledgeBundleRuntime _runtime;
	private IKnowledgeGuidanceSource _source;

	[SetUp]
	public void SetUp() {
		IFeatureToggleService toggles = Substitute.For<IFeatureToggleService>();
		_activator = Substitute.For<IKnowledgeBundleActivator>();
		_runtime = Substitute.For<IKnowledgeBundleRuntime>();
		_source = new KnowledgeGuidanceSource(toggles, _activator, _runtime);
	}

	[Test]
	[Description("Returns a stable typed unavailable result for an external guide when no verified bundle is active.")]
	public async Task GetGuidance_ShouldReturnTypedUnavailable_WhenExternalBundleIsCold() {
		// Arrange
		_runtime.Find("esq-filters").Returns(new KnowledgeArticleLookup(
			KnowledgeArticleLookupStatus.Unavailable, null, null));
		GuidanceGetTool tool = new(_source);

		// Act
		GuidanceGetResponse response = await tool.GetGuidance(new GuidanceGetArgs("esq-filters"));

		// Assert
		response.Success.Should().BeFalse(because: "cold external knowledge must never look successful");
		response.ErrorCode.Should().Be(KnowledgeGuidanceUnavailableException.ErrorCode,
			because: "agents must distinguish unavailable guidance from an unknown guide");
		response.Article.Should().BeNull(because: "unavailable guidance must never become permissive empty content");
		_activator.Received(1).EnsureActivated();
	}

	[Test]
	[Description("Fails the docs resource explicitly when no verified bundle is active and does not use embedded content.")]
	public void GetGuide_ShouldThrowTypedUnavailable_WhenExternalBundleIsCold() {
		// Arrange
		_runtime.Find("esq-filters").Returns(new KnowledgeArticleLookup(
			KnowledgeArticleLookupStatus.Unavailable, null, null));
		KnowledgeGuidanceResourceAdapter adapter = new(_source);
		EsqFiltersGuidanceResource resource = new(adapter);

		// Act
		System.Action act = () => resource.GetGuide();

		// Assert
		act.Should().Throw<KnowledgeGuidanceUnavailableException>(
			because: "the docs surface must fail closed instead of falling back to embedded ESQ text")
			.WithMessage("*guidance-unavailable*");
	}

	[Test]
	[Description("Keeps non-externalized hard safety guidance available when the external bundle is unavailable.")]
	public async Task GetGuidance_ShouldKeepCoreRulesAvailable_WhenExternalBundleIsCold() {
		// Arrange
		GuidanceGetTool tool = new(_source);

		// Act
		GuidanceGetResponse response = await tool.GetGuidance(new GuidanceGetArgs("core-rules"));

		// Assert
		response.Success.Should().BeTrue(because: "bundle availability must not disable Clio-owned safety guidance");
		response.Article!.Text.Should().Contain("Non-negotiable",
			because: "the existing executable safety boundary remains independently served");
		_activator.DidNotReceive().EnsureActivated();
	}
}
