using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Clio.Command.McpServer.Knowledge;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class GuidanceGetToolTests {
	private ServiceProvider _container;
	private IKnowledgeGuidanceSource _source;
	private GuidanceGetTool _tool;

	[SetUp]
	public void SetUp() {
		_source = Substitute.For<IKnowledgeGuidanceSource>();
		_source.GetNames().Returns(["synthetic-guide"]);
		ServiceCollection services = new();
		services.AddSingleton(_source);
		services.AddTransient<GuidanceGetTool>();
		_container = services.BuildServiceProvider();
		_tool = _container.GetRequiredService<GuidanceGetTool>();
	}

	[TearDown]
	public void TearDown() {
		_container.Dispose();
	}

	[Test]
	[Description("Advertises a stable, read-only MCP tool contract for guidance lookup.")]
	public void GetGuidance_ShouldAdvertiseStableReadOnlyContract_WhenReflected() {
		// Arrange
		MethodInfo method = typeof(GuidanceGetTool).GetMethod(nameof(GuidanceGetTool.GetGuidance))!;

		// Act
		McpServerToolAttribute attribute = method.GetCustomAttributes<McpServerToolAttribute>().Single();

		// Assert
		attribute.Name.Should().Be(GuidanceGetTool.ToolName,
			because: "clients route guidance lookup through the stable production constant");
		attribute.ReadOnly.Should().BeTrue(
			because: "retrieving guidance must never be classified as a mutation");
		attribute.Destructive.Should().BeFalse(
			because: "guidance lookup cannot require destructive authorization");
	}

	[Test]
	[Description("Maps an active synthetic article through get-guidance without changing its stable identity or text.")]
	public async Task GetGuidance_ShouldReturnSyntheticArticle_WhenSourceIsActive() {
		// Arrange
		const string text = "Synthetic delivery fixture.\n";
		_source.FindByName("synthetic-guide").Returns(new KnowledgeArticleLookup(
			KnowledgeArticleLookupStatus.Active,
			new KnowledgeArticle("synthetic-guide", "docs://synthetic/guides/guide", text),
			1));

		// Act
		GuidanceGetResponse response = await _tool.GetGuidance(new GuidanceGetArgs("synthetic-guide"));

		// Assert
		response.Success.Should().BeTrue(because: "an active verified article must be returned");
		response.Article!.Name.Should().Be("synthetic-guide",
			because: "the stable bundle identifier must survive tool routing");
		response.Article.Uri.Should().Be("docs://synthetic/guides/guide",
			because: "the bundle-owned resource URI must survive tool routing");
		response.Article.Text.Should().Be(text,
			because: "the delivery surface must not rewrite verified synthetic bytes");
	}

	[Test]
	[Description("Returns the selected external article together with its complete source provenance.")]
	public async Task GetGuidance_ShouldReturnCompleteProvenance_WhenExternalArticleIsActive() {
		// Arrange
		KnowledgeArticleProvenance provenance = new(
			"partner",
			"com.example.partner",
			"guide.item",
			"topic.shared",
			42,
			"sha256:verified",
			"C:\\knowledge\\partner\\guide.md");
		_source.FindByName("partner-guide").Returns(new KnowledgeArticleLookup(
			KnowledgeArticleLookupStatus.Active,
			new KnowledgeArticle("partner-guide", "docs://partner/guides/guide", "Partner guidance.\n"),
			42,
			provenance));

		// Act
		GuidanceGetResponse response = await _tool.GetGuidance(new GuidanceGetArgs("partner-guide"));

		// Assert
		response.Success.Should().BeTrue(because: "verified external guidance should be returned");
		response.Article!.LibraryId.Should().Be("com.example.partner",
			because: "agents need the stable publisher identity for attribution");
		response.Article.ItemId.Should().Be("guide.item",
			because: "agents need the stable item identity inside the publisher library");
		response.Article.TopicId.Should().Be("topic.shared",
			because: "agents need the logical topic used by deterministic resolution");
		response.Article.Sequence.Should().Be(42,
			because: "agents need the selected library generation");
		response.Article.BundleDigest.Should().Be("sha256:verified",
			because: "the response must identify the verified content generation");
		response.Article.SourceAlias.Should().Be("partner",
			because: "the response must identify which configured source won resolution");
		response.Article.LocalPath.Should().Be("C:\\knowledge\\partner\\guide.md",
			because: "agents need the readable on-disk content path when one is available");
	}

	[Test]
	[Description("Returns typed ambiguity and the resolver diagnostic when no deterministic guidance winner exists.")]
	public async Task GetGuidance_ShouldReturnTypedAmbiguity_WhenResolutionHasNoWinner() {
		// Arrange
		const string diagnostic = "Guidance 'shared-guide' is ambiguous across partner-a and partner-b.";
		_source.FindByName("shared-guide").Returns(new KnowledgeArticleLookup(
			KnowledgeArticleLookupStatus.Ambiguous,
			null,
			null,
			null,
			diagnostic));

		// Act
		GuidanceGetResponse response = await _tool.GetGuidance(new GuidanceGetArgs("shared-guide"));

		// Assert
		response.Success.Should().BeFalse(
			because: "ambiguous guidance must fail closed rather than selecting an arbitrary source");
		response.ErrorCode.Should().Be(KnowledgeGuidanceAmbiguousException.ErrorCode,
			because: "callers need a stable typed ambiguity result");
		response.Error.Should().Be(diagnostic,
			because: "the resolver diagnostic tells agents which source conflict needs operator action");
		response.Article.Should().BeNull(
			because: "no article may be returned when deterministic resolution failed");
	}

	[Test]
	[Description("Returns typed unavailability instead of permissive empty content when no verified bundle is active.")]
	public async Task GetGuidance_ShouldReturnTypedUnavailable_WhenSourceIsUnavailable() {
		// Arrange
		_source.FindByName("synthetic-guide").Returns(new KnowledgeArticleLookup(
			KnowledgeArticleLookupStatus.Unavailable, null, null));

		// Act
		GuidanceGetResponse response = await _tool.GetGuidance(new GuidanceGetArgs("synthetic-guide"));

		// Assert
		response.Success.Should().BeFalse(because: "unavailable guidance cannot be treated as success");
		response.ErrorCode.Should().Be(KnowledgeGuidanceUnavailableException.ErrorCode,
			because: "callers must distinguish an unavailable bundle from an unknown identifier");
		response.Article.Should().BeNull(because: "cold guidance must fail closed");
	}

	[Test]
	[Description("Returns typed not-found and the discoverable names when an active source lacks the requested identifier.")]
	public async Task GetGuidance_ShouldReturnTypedNotFound_WhenIdentifierIsUnknown() {
		// Arrange
		_source.FindByName("missing-guide").Returns(new KnowledgeArticleLookup(
			KnowledgeArticleLookupStatus.NotFound, null, 7));

		// Act
		GuidanceGetResponse response = await _tool.GetGuidance(new GuidanceGetArgs("missing-guide"));

		// Assert
		response.Success.Should().BeFalse(because: "an absent identifier cannot produce an article");
		response.ErrorCode.Should().Be("guidance-not-found",
			because: "not-found must remain distinct from bundle unavailability");
		response.AvailableGuides.Should().Equal(["synthetic-guide"],
			because: "discovery must return the stable source-owned catalog");
	}

	[Test]
	[Description("Rejects a missing name without consulting the article source.")]
	public async Task GetGuidance_ShouldRejectMissingName_WhenArgumentsAreEmpty() {
		// Arrange
		GuidanceGetArgs args = new();

		// Act
		GuidanceGetResponse response = await _tool.GetGuidance(args);

		// Assert
		response.Success.Should().BeFalse(because: "name is the required routing key");
		response.Error.Should().Contain("Missing required parameter 'name'",
			because: "the caller needs a stable actionable argument diagnostic");
		_source.ReceivedCalls().Should().NotContain(
			call => call.GetMethodInfo().Name == nameof(IKnowledgeGuidanceSource.FindByName),
			because: "validation must stop before an article lookup without a routing key");
	}

	[Test]
	[Description("Preserves the legacy topic alias while routing it through the same stable name lookup.")]
	public async Task GetGuidance_ShouldAcceptLegacyTopicAlias_WhenNameIsAbsent() {
		// Arrange
		_source.FindByName("synthetic-guide").Returns(new KnowledgeArticleLookup(
			KnowledgeArticleLookupStatus.Active,
			new KnowledgeArticle("synthetic-guide", "docs://synthetic/guides/guide", "Synthetic delivery fixture.\n"),
			1));
		GuidanceGetArgs args = new() {
			ExtensionData = new() { ["topic"] = JsonSerializer.SerializeToElement("synthetic-guide") }
		};

		// Act
		GuidanceGetResponse response = await _tool.GetGuidance(args);

		// Assert
		response.Success.Should().BeTrue(because: "the supported compatibility alias must still resolve");
		response.Hint.Should().Contain("Accepted 'topic' as 'name'",
			because: "the caller should receive a migration hint for the canonical argument");
		_source.ReceivedCalls().Should().ContainSingle(
			call => call.GetMethodInfo().Name == nameof(IKnowledgeGuidanceSource.FindByName)
				&& Equals(call.GetArguments()[0], "synthetic-guide"),
			because: "the compatibility alias must map to one canonical source lookup");
	}
}
