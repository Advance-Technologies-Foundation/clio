using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Common.DataForge;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
public sealed class DataForgeClientTests {
	[Test]
	[Category("Unit")]
	[Description("FindSimilarTablesAsync should call the similarDetails endpoint with the resolved limit and attach bearer auth plus correlation headers when OAuth is configured.")]
	public async Task FindSimilarTablesAsync_Should_Build_Request_And_Attach_Auth() {
		// Arrange
		StubHttpMessageHandler handler = new([
			new StubResponse(HttpMethod.Post, "https://identity.example/connect/token", new HttpResponseMessage(HttpStatusCode.OK) {
				Content = new StringContent("""{"access_token":"token-123","expires_in":3600}""")
			}),
			new StubResponse(HttpMethod.Get,
				"https://dataforge.example/api/v1/dataStructure/tables/similarDetails?query=Contact&limit=7",
				new HttpResponseMessage(HttpStatusCode.OK) {
					Content = new StringContent("""[{"name":"Contact","caption":"Contact","description":"Primary contact"}]""")
				})
		]);
		using HttpClient httpClient = new(handler);
		IDataForgeConfigResolver resolver = Substitute.For<IDataForgeConfigResolver>();
		resolver.Resolve(Arg.Any<DataForgeConfigRequest>()).Returns(new DataForgeResolvedConfig(
			ServiceUrl: "https://dataforge.example/",
			TimeoutMs: 15000,
			SimilarTablesLimit: 50,
			LookupResultLimit: 5,
			TableRelationshipsLimit: 5,
			AuthMode: DataForgeAuthMode.OAuthClientCredentials,
			TokenUrl: "https://identity.example/connect/token",
			ClientId: "client-id",
			ClientSecret: "client-secret",
			Scope: ""));
		DataForgeClient client = new(httpClient, resolver, Substitute.For<ILogger>());

		// Act
		IReadOnlyList<SimilarTableResult> result = await client.FindSimilarTablesAsync("Contact", 7);

		// Assert
		result.Should().ContainSingle();
		result[0].Name.Should().Be("Contact");
		handler.Requests.Should().HaveCount(2);
		handler.Requests[1].Headers.Authorization?.Scheme.Should().Be("Bearer");
		handler.Requests[1].Headers.Authorization?.Parameter.Should().Be("token-123");
		handler.Requests[1].Headers.Should().Contain(header => header.Key == "X-Correlation-ID");
	}

	[Test]
	[Category("Unit")]
	[Description("GetTableRelationshipsAsync should reuse the resolved default limit when the caller omits it.")]
	public async Task GetTableRelationshipsAsync_Should_Use_Default_Limit_From_Config() {
		// Arrange
		StubHttpMessageHandler handler = new([
			new StubResponse(HttpMethod.Get,
				"https://dataforge.example/api/v1/dataStructure/tables/relations/cypher?sourceTable=Contact&targetTable=Account&limit=5",
				new HttpResponseMessage(HttpStatusCode.OK) {
					Content = new StringContent("""["(Contact)-[:Account]->(Account)"]""")
				})
		]);
		using HttpClient httpClient = new(handler);
		IDataForgeConfigResolver resolver = Substitute.For<IDataForgeConfigResolver>();
		resolver.Resolve(Arg.Any<DataForgeConfigRequest>()).Returns(new DataForgeResolvedConfig(
			ServiceUrl: "https://dataforge.example/",
			TimeoutMs: 15000,
			SimilarTablesLimit: 50,
			LookupResultLimit: 5,
			TableRelationshipsLimit: 5,
			AuthMode: DataForgeAuthMode.None,
			TokenUrl: null,
			ClientId: null,
			ClientSecret: null,
			Scope: ""));
		DataForgeClient client = new(httpClient, resolver, Substitute.For<ILogger>());

		// Act
		IReadOnlyList<string> result = await client.GetTableRelationshipsAsync("Contact", "Account", null);

		// Assert
		result.Should().Equal("(Contact)-[:Account]->(Account)");
		handler.Requests.Should().ContainSingle();
	}

	[Test]
	[Category("Unit")]
	[Description("FindSimilarLookupsAsync should normalize the actual dataforge-service lookup payload into MCP lookup fields instead of returning nulls.")]
	public async Task FindSimilarLookupsAsync_Should_Map_Service_Lookup_Payload() {
		// Arrange
		StubHttpMessageHandler handler = new([
			new StubResponse(HttpMethod.Get,
				"https://dataforge.example/api/v1/lookups/similar?query=customer&limit=5",
				new HttpResponseMessage(HttpStatusCode.OK) {
					Content = new StringContent("""
						[
						  {
						    "id": "lookup-schema-id",
						    "name": "ContactType",
						    "description": "Contact type lookup",
						    "referenceSchemaName": "ContactType",
						    "valueId": "value-id-1",
						    "valueName": "Customer",
						    "vectorSimilarityScore": 0.97
						  }
						]
						""")
				})
		]);
		using HttpClient httpClient = new(handler);
		IDataForgeConfigResolver resolver = Substitute.For<IDataForgeConfigResolver>();
		resolver.Resolve(Arg.Any<DataForgeConfigRequest>()).Returns(new DataForgeResolvedConfig(
			ServiceUrl: "https://dataforge.example/",
			TimeoutMs: 15000,
			SimilarTablesLimit: 50,
			LookupResultLimit: 5,
			TableRelationshipsLimit: 5,
			AuthMode: DataForgeAuthMode.None,
			TokenUrl: null,
			ClientId: null,
			ClientSecret: null,
			Scope: ""));
		DataForgeClient client = new(httpClient, resolver, Substitute.For<ILogger>());

		// Act
		IReadOnlyList<SimilarLookupResult> result = await client.FindSimilarLookupsAsync("customer");

		// Assert
		result.Should().ContainSingle();
		result[0].LookupId.Should().Be("value-id-1");
		result[0].SchemaName.Should().Be("ContactType");
		result[0].Value.Should().Be("Customer");
		result[0].Score.Should().Be(0.97m);
	}

	[Test]
	[Category("Unit")]
	[Description("CheckHealthAsync should treat non-success probe status codes as degraded health instead of throwing, because liveness/readiness endpoints commonly return 5xx for unhealthy states.")]
	public async Task CheckHealthAsync_Should_Return_Degraded_Probe_Statuses_Without_Throwing() {
		// Arrange
		StubHttpMessageHandler handler = new([
			new StubResponse(
				HttpMethod.Get,
				"https://dataforge.example/liveness",
				new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
			new StubResponse(
				HttpMethod.Get,
				"https://dataforge.example/readiness",
				new HttpResponseMessage(HttpStatusCode.InternalServerError))
		]);
		using HttpClient httpClient = new(handler);
		IDataForgeConfigResolver resolver = Substitute.For<IDataForgeConfigResolver>();
		resolver.Resolve(Arg.Any<DataForgeConfigRequest>()).Returns(new DataForgeResolvedConfig(
			ServiceUrl: "https://dataforge.example/",
			TimeoutMs: 15000,
			SimilarTablesLimit: 50,
			LookupResultLimit: 5,
			TableRelationshipsLimit: 5,
			AuthMode: DataForgeAuthMode.None,
			TokenUrl: null,
			ClientId: null,
			ClientSecret: null,
			Scope: string.Empty));
		DataForgeClient client = new(httpClient, resolver, Substitute.For<ILogger>());

		// Act
		DataForgeHealthResult result = await client.CheckHealthAsync();

		// Assert
		result.Liveness.Should().BeFalse(
			because: "a non-success liveness probe should be reported as degraded health instead of throwing");
		result.Readiness.Should().BeFalse(
			because: "a non-success readiness probe should be reported as degraded health instead of throwing");
		result.DataStructureReadiness.Should().BeFalse(
			because: "downstream readiness probes should not run when the top-level readiness probe already failed");
		result.LookupsReadiness.Should().BeFalse(
			because: "downstream readiness probes should not run when the top-level readiness probe already failed");
		handler.Requests.Should().HaveCount(2,
			because: "only the top-level liveness and readiness probes should run for a degraded service");
	}

	private sealed record StubResponse(HttpMethod Method, string Uri, HttpResponseMessage Response);

	private sealed class StubHttpMessageHandler(IReadOnlyList<StubResponse> responses) : HttpMessageHandler {
		public List<HttpRequestMessage> Requests { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
			Requests.Add(request);
			StubResponse response = responses.FirstOrDefault(candidate =>
				candidate.Method == request.Method &&
				string.Equals(candidate.Uri, request.RequestUri?.ToString(), StringComparison.Ordinal))
				?? throw new InvalidOperationException($"Unexpected HTTP request: {request.Method} {request.RequestUri}");
			return Task.FromResult(response.Response);
		}
	}
}
