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
	[Description("A resource read of an identifier no installed library publishes reports the protocol's resource-not-found refusal rather than a server fault.")]
	public void Get_ShouldReportUnknownResource_WhenNoLibraryPublishesTheIdentifier() {
		// Arrange
		_source.FindByUri(ItemUri).Returns(new KnowledgeArticleLookup(
			KnowledgeArticleLookupStatus.NotFound,
			null,
			null));

		// Act
		Action act = () => _sut.Get(ItemUri);

		// Assert
		McpProtocolException thrown = act.Should().Throw<McpProtocolException>(
			because: "an absent identifier is the client naming something that is not there, which the protocol "
				+ "reports as a refusal instead of collapsing into the generic internal error a plain exception "
				+ "produces").Which;
		thrown.ErrorCode.Should().Be(McpErrorCode.ResourceNotFound,
			because: "a caller must be able to tell an absent resource from a server fault, and only the "
				+ "protocol's own not-found code carries that distinction");
		thrown.Message.Should().Contain(KnowledgeGuidanceNotFoundException.ErrorCode,
			because: "the resource path must classify absence exactly as get-guidance classifies it");
		thrown.Message.Should().Contain(ItemUri,
			because: "the refusal has to name the identifier that was not found for the caller to act on it");
		thrown.Message.Should().Contain("resources/list",
			because: "the read response carries no candidate list of its own, so it must point at the listing "
				+ "that does instead of at get-guidance's availableGuides field");
	}

	[Test]
	[Description("A feature-gated topic's URI is refused exactly like an identifier nobody publishes, so a read cannot reveal that hidden content exists.")]
	public void Get_ShouldRefuseGatedTopicIdenticallyToAbsentOne_WhenItsFeatureIsDisabled() {
		// Arrange
		const string gatedUri = "docs://knowledge/com.example.alpha/gated-guide";
		const string absentUri = "docs://knowledge/com.example.alpha/never-published";
		// A topic whose requiredFeatures are not all enabled is filtered inside the resolver, so it
		// reaches the adapter as NotFound — the same status an unpublished identifier produces.
		_source.FindByUri(gatedUri).Returns(new KnowledgeArticleLookup(
			KnowledgeArticleLookupStatus.NotFound,
			null,
			null));
		_source.FindByUri(absentUri).Returns(new KnowledgeArticleLookup(
			KnowledgeArticleLookupStatus.NotFound,
			null,
			null));

		// Act
		McpProtocolException gated = ((Action)(() => _sut.Get(gatedUri)))
			.Should().Throw<McpProtocolException>().Which;
		McpProtocolException absent = ((Action)(() => _sut.Get(absentUri)))
			.Should().Throw<McpProtocolException>().Which;

		// Assert
		gated.ErrorCode.Should().Be(absent.ErrorCode,
			because: "a different error class for a gated topic would let a caller probe which hidden "
				+ "topics exist by reading URIs one by one");
		gated.Message.Replace(gatedUri, absentUri, StringComparison.Ordinal).Should().Be(absent.Message,
			because: "beyond the identifier it echoes, the refusal must be word-for-word identical — naming "
				+ "the gate, or admitting one exists, defeats the gate");
		// The identifier is echoed verbatim, and a publisher is free to put any word in it, so the wording
		// check applies to what the refusal adds around it.
		gated.Message.Replace(gatedUri, string.Empty, StringComparison.Ordinal).Should()
			.NotContainAny(["feature", "gate", "disabled", "enabled", "withheld", "hidden", "permission"],
				because: "the refusal must not hint that the topic is being withheld rather than absent");
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
