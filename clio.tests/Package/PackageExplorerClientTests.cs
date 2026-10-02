using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Package;

[TestFixture, Category("Unit"), Property("Module", "Package")]
public sealed class PackageExplorerClientTests {
	private ServiceProvider _container;
	private ICreatioApplicationClient _transport;
	private IPackageExplorerClient _client;
	[SetUp]
	public void Setup() {
		_transport = Substitute.For<ICreatioApplicationClient>();
		var urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns(call => "https://example.test/" + call.Arg<ServiceUrlBuilder.KnownRoute>());
		var services = new ServiceCollection();
		services.AddSingleton<IApplicationClient>(_transport);
		services.AddSingleton(urls);
		services.AddTransient<IPackageExplorerClient, PackageExplorerClient>();
		_container = services.BuildServiceProvider();
		_client = _container.GetRequiredService<IPackageExplorerClient>();
	}
	[TearDown]
	public void TearDown() { _transport.ClearReceivedCalls(); _container.Dispose(); }
	private void Reply(string route, string json, HttpStatusCode status = HttpStatusCode.OK) {
		_transport.ExecutePostRequestAsync("https://example.test/" + route, Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(_ => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json) }));
	}
	[TestCase(false), TestCase(true)]
	[Description("A v1 client continues to work when a newer server also advertises v2 and adds fields.")]
	public void Read_ShouldKeepV1_WhenServerAddsVersionsAndFields(bool alsoV2) {
		// Arrange
		Reply("DependencyCapabilities", "{\"success\":true,\"contracts\":[{\"major\":1,\"operations\":[\"graph\"]}" + (alsoV2 ? ",{\"major\":2,\"operations\":[\"graph\"]}" : "") + "]}");
		Reply("DependencyGraphV1", """{"success":true,"contractMajor":1,"futureField":{"value":123},"packages":[],"dependencies":[]}""");
		// Act
		JObject result = _client.Read(ServiceUrlBuilder.KnownRoute.DependencyGraphV1, new JObject());
		// Assert
		((int)result["futureField"]["value"]).Should().Be(123, because: "unknown additive fields remain forward compatible");
	}
	[Test, Description("Stock servers are refused rather than treated as experimental v1.")]
	public void Read_ShouldRefuseUnsupportedServer_WhenCapabilitiesMissing() {
		// Arrange
		Reply("DependencyCapabilities", "not found", HttpStatusCode.NotFound);
		// Act
		Action action = () => _client.Read(ServiceUrlBuilder.KnownRoute.DependencyGraphV1, new JObject());
		// Assert
		action.Should().Throw<NotSupportedException>(because: "404 does not establish any supported contract");
	}
	[Test, Description("A v2-only server cannot be used by a v1 client.")]
	public void Read_ShouldRefuseIncompatibleServer_WhenNoCommonMajor() {
		// Arrange
		Reply("DependencyCapabilities", """{"success":true,"contracts":[{"major":2,"operations":["graph"]}]}""");
		// Act
		Action action = () => _client.Read(ServiceUrlBuilder.KnownRoute.DependencyGraphV1, new JObject());
		// Assert
		action.Should().Throw<NotSupportedException>(because: "a client must understand the selected contract");
	}
	[Test, Description("Unknown future assessment values never turn into permission to remove a dependency.")]
	public void Read_ShouldFailClosed_WhenAssessmentUnknown() {
		// Arrange
		Reply("DependencyCapabilities", """{"success":true,"contracts":[{"major":1,"operations":["dropImpact"]}]}""");
		Reply("DependencyDropV1", """{"success":true,"contractMajor":1,"assessment":"futureVerdict"}""");
		// Act
		Action action = () => _client.Read(ServiceUrlBuilder.KnownRoute.DependencyDropV1, new JObject());
		// Assert
		action.Should().Throw<InvalidOperationException>(because: "unknown semantics cannot imply a safe verdict");
	}
	[Test, Description("Features advertised only by v2 must never be used with the retained v1 route.")]
	public void Read_ShouldRejectV2OnlyFeature_WhenV1IsSelected() {
		// Arrange
		Reply("DependencyCapabilities", """{"success":true,"contracts":[{"major":1,"operations":["searchSchemas"],"features":[]},{"major":2,"operations":["searchSchemas"],"features":["schemaSearch.literalContains"]}]}""");
		// Act
		Action action = () => _client.Read(ServiceUrlBuilder.KnownRoute.DependencySearchV1,
			new JObject { ["matchMode"] = "contains" });
		// Assert
		action.Should().Throw<NotSupportedException>(because: "v2 capability must not leak into the v1 contract");
	}

	[Test, Description("Existing entity diagnostics exclude packages already reachable through an intermediate dependency.")]
	public void GetReachablePackageNames_ShouldIncludeIndirectOwner_WhenGraphHasTwoHopPath() {
		// Arrange
		Guid source = Guid.Parse("00000000-0000-0000-0000-000000000001");
		Reply("DependencyCapabilities", """{"success":true,"contracts":[{"major":1,"operations":["graph"]}]}""");
		Reply("DependencyGraphV1", """{"success":true,"contractMajor":1,"packages":[{"uId":"00000000-0000-0000-0000-000000000001","name":"A"},{"uId":"b","name":"B"},{"uId":"c","name":"C"}],"dependencies":[{"packageUId":"00000000-0000-0000-0000-000000000001","dependOnPackageUId":"b"},{"packageUId":"b","dependOnPackageUId":"c"}],"missingDependencies":[]}""");
		// Act
		var names = _client.GetReachablePackageNames(source, "A", 30_000);
		// Assert
		names.Should().Contain("C", because: "A -> B -> C already supplies C without a new direct dependency");
	}

}
