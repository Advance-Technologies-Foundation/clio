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
