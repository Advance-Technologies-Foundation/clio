using System;
using Clio.Command.McpServer.Knowledge;
using Clio.Command.McpServer.Resources;
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
public sealed class KnowledgeGuidanceResourceAdapterTests {
	private const string ItemUri = "docs://knowledge/com.example.alpha/shared-guide";
	private const string CollisionDiagnostic =
		"Libraries 'com.example.alpha' and 'com.example.beta' both publish item 'shared-guide'.";
	private IKnowledgeGuidanceSource _source = null!;
	private ServiceProvider _services = null!;
	private IKnowledgeGuidanceResourceAdapter _sut = null!;

	[SetUp]
	public void SetUp() {
		_source = Substitute.For<IKnowledgeGuidanceSource>();
		ServiceCollection services = new();
		services.AddSingleton(_source);
		services.AddSingleton<IKnowledgeGuidanceResourceAdapter, KnowledgeGuidanceResourceAdapter>();
		_services = services.BuildServiceProvider();
		_sut = _services.GetRequiredService<IKnowledgeGuidanceResourceAdapter>();
	}

	[TearDown]
	public void TearDown() {
		_source.ClearReceivedCalls();
		_services.Dispose();
	}

	[Test]
	[Description("A resource read of an identifier several installed libraries claim reports the ambiguity and names the collision.")]
	public void Get_ShouldReportAmbiguity_WhenSeveralLibrariesClaimTheIdentifier() {
		// Arrange
		_source.FindByUri(ItemUri).Returns(new KnowledgeArticleLookup(
			KnowledgeArticleLookupStatus.Ambiguous,
			null,
			null,
			null,
			CollisionDiagnostic));

		// Act
		Action act = () => _sut.Get(ItemUri);

		// Assert
		McpProtocolException thrown = act.Should().Throw<McpProtocolException>(
			because: "an unresolvable identifier is a protocol-level refusal, not an unhandled server fault").Which;
		thrown.Message.Should().Contain(KnowledgeGuidanceAmbiguousException.ErrorCode,
			because: "the resource path must classify the failure exactly as get-guidance classifies it");
		thrown.Message.Should().Contain(CollisionDiagnostic,
			because: "the resolver diagnostic naming the colliding libraries is what makes the refusal actionable");
		thrown.ErrorCode.Should().Be(McpErrorCode.InternalError,
			because: "the colliding libraries are server-side state, so the client's identifier is not at fault");
	}

	[Test]
	[Description("A resource read of an identifier no installed library publishes is still reported as unknown.")]
	public void Get_ShouldReportUnknownResource_WhenNoLibraryPublishesTheIdentifier() {
		// Arrange
		_source.FindByUri(ItemUri).Returns(new KnowledgeArticleLookup(
			KnowledgeArticleLookupStatus.NotFound,
			null,
			null));

		// Act
		Action act = () => _sut.Get(ItemUri);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "an absent identifier must keep failing the read")
			.Which.Message.Should().Contain("Unknown guidance resource",
				because: "separating ambiguity from absence must not reclassify a genuinely missing item");
	}

	[Test]
	[Description("A resource read of a resolvable identifier returns the verified article contents unchanged.")]
	public void Get_ShouldReturnArticleContents_WhenLookupResolves() {
		// Arrange
		_source.FindByUri(ItemUri).Returns(new KnowledgeArticleLookup(
			KnowledgeArticleLookupStatus.Active,
			new KnowledgeArticle("shared-guide", ItemUri, "Trusted text.", MediaType: "text/markdown"),
			1));

		// Act
		ResourceContents contents = _sut.Get(ItemUri);

		// Assert
		contents.Should().BeOfType<TextResourceContents>(
			because: "verified knowledge is served as text resource contents")
			.Which.Text.Should().Be("Trusted text.",
				because: "the resolved article body must reach the client unmodified");
		contents.Uri.Should().Be(ItemUri,
			because: "the served resource must identify itself with its canonical URI");
	}
}
