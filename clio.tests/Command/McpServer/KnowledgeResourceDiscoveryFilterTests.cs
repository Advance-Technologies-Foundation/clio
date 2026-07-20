using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class KnowledgeResourceDiscoveryFilterTests {
	[Test]
	[Description("Appends active knowledge discovery metadata without replacing ordinary MCP resources.")]
	public async Task AppendKnowledgeResources_ShouldExposeActiveCatalog_WhenResourcesAreListed() {
		// Arrange
		IKnowledgeGuidanceSource source = Substitute.For<IKnowledgeGuidanceSource>();
		source.GetCatalog().Returns([
			new KnowledgeGuidanceDescriptor(
				"synthetic-guide",
				"Synthetic guide",
				"Explains the synthetic mechanics fixture.",
				"docs://knowledge/com.example.synthetic/synthetic-guide",
				"text/markdown")
		]);
		using ServiceProvider services = new ServiceCollection().AddSingleton(source).BuildServiceProvider();
		ModelContextProtocol.Server.McpServer server = Substitute.For<ModelContextProtocol.Server.McpServer>();
		server.Services.Returns(services);
		RequestContext<ListResourcesRequestParams> context = new(
			server,
			new JsonRpcRequest { Id = new RequestId(1), Method = RequestMethods.ResourcesList },
			new ListResourcesRequestParams());
		McpRequestHandler<ListResourcesRequestParams, ListResourcesResult> next = (_, _) =>
			ValueTask.FromResult(new ListResourcesResult {
				Resources = [new Resource { Name = "help", Uri = "docs://help/command/mcp-server" }]
			});
		McpRequestHandler<ListResourcesRequestParams, ListResourcesResult> handler =
			KnowledgeResourceDiscoveryFilter.AppendKnowledgeResources(next);

		// Act
		ListResourcesResult result = await handler(context, CancellationToken.None);

		// Assert
		result.Resources.Should().ContainSingle(resource => resource.Uri == "docs://help/command/mcp-server",
			because: "dynamic knowledge discovery must preserve resources registered by Clio mechanics");
		Resource knowledge = result.Resources.Should().ContainSingle(resource =>
			resource.Uri == "docs://knowledge/com.example.synthetic/synthetic-guide",
			because: "the active trusted catalog must be visible through resources/list").Subject;
		knowledge.Name.Should().Be("synthetic-guide",
			because: "the publisher-owned item ID is the stable discovery key");
		knowledge.Title.Should().Be("Synthetic guide",
			because: "human-readable titles are owned by the knowledge library");
		knowledge.Description.Should().Be("Explains the synthetic mechanics fixture.",
			because: "agents need publisher-owned descriptions to choose a resource");
	}

	[Test]
	[Description("Preserves the SDK resource continuation before exposing dynamic knowledge resources.")]
	public async Task AppendKnowledgeResources_ShouldPreserveSdkContinuation_WhenStaticResourcesHaveAnotherPage() {
		// Arrange
		IKnowledgeGuidanceSource source = Substitute.For<IKnowledgeGuidanceSource>();
		int catalogCalls = 0;
		source.GetCatalog().Returns(_ => {
			catalogCalls++;
			return [];
		});
		using ServiceProvider services = new ServiceCollection().AddSingleton(source).BuildServiceProvider();
		RequestContext<ListResourcesRequestParams> context = CreateContext(services);
		McpRequestHandler<ListResourcesRequestParams, ListResourcesResult> next = (_, _) =>
			ValueTask.FromResult(new ListResourcesResult {
				Resources = [new Resource { Name = "help", Uri = "docs://help/command/mcp-server" }],
				NextCursor = "sdk-static-page-2"
			});
		McpRequestHandler<ListResourcesRequestParams, ListResourcesResult> handler =
			KnowledgeResourceDiscoveryFilter.AppendKnowledgeResources(next);

		// Act
		ListResourcesResult result = await handler(context, CancellationToken.None);

		// Assert
		result.Resources.Should().ContainSingle(
			because: "dynamic resources must wait until the SDK has exhausted its static-resource pages");
		result.NextCursor.Should().Be("sdk-static-page-2",
			because: "the SDK continuation is opaque and must be returned unchanged");
		catalogCalls.Should().Be(0,
			because: "the dynamic catalog must not be read while the SDK has another static page");
	}

	[Test]
	[Description("Starts dynamic knowledge pagination after an SDK continuation reaches its final static page.")]
	public async Task AppendKnowledgeResources_ShouldStartDynamicPage_WhenSdkCursorReachesFinalStaticPage() {
		// Arrange
		IKnowledgeGuidanceSource source = Substitute.For<IKnowledgeGuidanceSource>();
		source.GetCatalog().Returns([CreateDescriptor(0)]);
		using ServiceProvider services = new ServiceCollection().AddSingleton(source).BuildServiceProvider();
		RequestContext<ListResourcesRequestParams> context = CreateContext(services, "sdk-static-page-2");
		string? observedCursor = null;
		McpRequestHandler<ListResourcesRequestParams, ListResourcesResult> next = (request, _) => {
			observedCursor = request.Params.Cursor;
			return ValueTask.FromResult(new ListResourcesResult {
				Resources = [new Resource { Name = "restart", Uri = "docs://mcp/actions/restart" }]
			});
		};
		McpRequestHandler<ListResourcesRequestParams, ListResourcesResult> handler =
			KnowledgeResourceDiscoveryFilter.AppendKnowledgeResources(next);

		// Act
		ListResourcesResult result = await handler(context, CancellationToken.None);

		// Assert
		observedCursor.Should().Be("sdk-static-page-2",
			because: "non-Clio cursors remain owned by the wrapped SDK handler");
		result.Resources.Should().Contain(resource => resource.Name == "guide-000",
			because: "knowledge pagination starts only after the final SDK page is returned");
		result.NextCursor.Should().BeNull(
			because: "one dynamic resource fits in the final combined page");
	}

	[Test]
	[Description("Pages a large knowledge catalog with a deterministic bounded cursor without returning to the SDK handler.")]
	public async Task AppendKnowledgeResources_ShouldPageDynamicCatalog_WhenCatalogExceedsPageSize() {
		// Arrange
		IKnowledgeGuidanceSource source = Substitute.For<IKnowledgeGuidanceSource>();
		source.GetCatalog().Returns(Enumerable.Range(0, KnowledgeResourceDiscoveryFilter.DynamicPageSize + 1)
			.Reverse()
			.Select(CreateDescriptor)
			.ToArray());
		using ServiceProvider services = new ServiceCollection().AddSingleton(source).BuildServiceProvider();
		int sdkCalls = 0;
		McpRequestHandler<ListResourcesRequestParams, ListResourcesResult> next = (_, _) => {
			sdkCalls++;
			return ValueTask.FromResult(new ListResourcesResult {
				Resources = [new Resource { Name = "help", Uri = "docs://help/command/mcp-server" }]
			});
		};
		McpRequestHandler<ListResourcesRequestParams, ListResourcesResult> handler =
			KnowledgeResourceDiscoveryFilter.AppendKnowledgeResources(next);

		// Act
		ListResourcesResult first = await handler(CreateContext(services), CancellationToken.None);
		ListResourcesResult second = await handler(CreateContext(services, first.NextCursor), CancellationToken.None);

		// Assert
		first.Resources.Should().HaveCount(KnowledgeResourceDiscoveryFilter.DynamicPageSize + 1,
			because: "the final static page is followed by one bounded dynamic page");
		first.Resources.Skip(1).Select(resource => resource.Name).Should().Equal(
			Enumerable.Range(0, KnowledgeResourceDiscoveryFilter.DynamicPageSize).Select(index => $"guide-{index:D3}"),
			because: "dynamic resource order must remain deterministic across source enumeration order");
		first.NextCursor.Should().Be(
			$"{KnowledgeResourceDiscoveryFilter.DynamicCursorPrefix}{KnowledgeResourceDiscoveryFilter.DynamicPageSize:D10}",
			because: "Clio emits a fixed-width private cursor containing the next deterministic offset");
		second.Resources.Should().ContainSingle(resource => resource.Name == $"guide-{KnowledgeResourceDiscoveryFilter.DynamicPageSize:D3}",
			because: "the private cursor resumes at the first resource not returned on the previous page");
		second.NextCursor.Should().BeNull(
			because: "the final dynamic page has no continuation");
		sdkCalls.Should().Be(1,
			because: "Clio-owned dynamic cursors must not be passed into the SDK static-resource handler");
	}

	[Test]
	[Description("Rejects malformed Clio-owned cursors without forwarding them to the SDK handler.")]
	public async Task AppendKnowledgeResources_ShouldRejectCursor_WhenPrivateCursorIsMalformed() {
		// Arrange
		IKnowledgeGuidanceSource source = Substitute.For<IKnowledgeGuidanceSource>();
		using ServiceProvider services = new ServiceCollection().AddSingleton(source).BuildServiceProvider();
		int sdkCalls = 0;
		McpRequestHandler<ListResourcesRequestParams, ListResourcesResult> next = (_, _) => {
			sdkCalls++;
			return ValueTask.FromResult(new ListResourcesResult { Resources = [] });
		};
		McpRequestHandler<ListResourcesRequestParams, ListResourcesResult> handler =
			KnowledgeResourceDiscoveryFilter.AppendKnowledgeResources(next);
		RequestContext<ListResourcesRequestParams> context = CreateContext(
			services,
			KnowledgeResourceDiscoveryFilter.DynamicCursorPrefix + "123");

		// Act
		Func<Task> act = async () => await handler(context, CancellationToken.None);

		// Assert
		await act.Should().ThrowAsync<McpProtocolException>(
			because: "private cursors must have the exact bounded fixed-width representation emitted by Clio");
		sdkCalls.Should().Be(0,
			because: "malformed Clio-owned cursors must not cross into the SDK cursor namespace");
	}

	private static KnowledgeGuidanceDescriptor CreateDescriptor(int index) => new(
		$"guide-{index:D3}",
		$"Guide {index:D3}",
		$"Synthetic guide {index:D3}.",
		$"docs://knowledge/com.example.synthetic/guide-{index:D3}",
		"text/markdown");

	private static RequestContext<ListResourcesRequestParams> CreateContext(
		IServiceProvider services,
		string? cursor = null) {
		ModelContextProtocol.Server.McpServer server = Substitute.For<ModelContextProtocol.Server.McpServer>();
		server.Services.Returns(services);
		return new RequestContext<ListResourcesRequestParams>(
			server,
			new JsonRpcRequest { Id = new RequestId(1), Method = RequestMethods.ResourcesList },
			new ListResourcesRequestParams { Cursor = cursor });
	}
}
