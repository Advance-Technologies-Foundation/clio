using System;
using System.Collections.Generic;
using Clio.Common;
using Clio.Common.DataForge;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
public sealed class DataForgeReadClientTests {
	[Test]
	[Category("Unit")]
	[Description("FindSimilarTables should call the Creatio proxy route with a bounded timeout and map table results.")]
	public void FindSimilarTables_Should_Call_Proxy_Route_With_Timeout_And_Map_Result() {
		// Arrange
		(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder, IDataForgePlatformVersionGuard versionGuard) =
			CreateDependencies("rest/DataForgeSchemaReadService/GetSimilarTableNames", "http://localhost/0/rest/DataForgeSchemaReadService/GetSimilarTableNames");
		applicationClient.ExecutePostRequest(
				"http://localhost/0/rest/DataForgeSchemaReadService/GetSimilarTableNames",
				Arg.Is<string>(body => body.Contains("\"query\":\"customer\"") && body.Contains("\"limit\":3")),
				DataForgeReadClient.RequestTimeoutMs,
				1,
				1)
			.Returns("""{"GetSimilarTableNamesResult":{"Success":true,"Data":[{"Name":"Contact","Caption":"Contact","Description":"Primary contact"}]}}""");
		DataForgeReadClient client = new(applicationClient, serviceUrlBuilder, versionGuard);

		// Act
		IReadOnlyList<SimilarTableResult> result = client.FindSimilarTables("customer", 3);

		// Assert
		result.Should().ContainSingle(because: "one proxy table result should map to one DataForge table result");
		result[0].Name.Should().Be("Contact", because: "table schema names should be preserved from the proxy payload");
		versionGuard.Received(1).EnsureSupported();
		applicationClient.Received(1).ExecutePostRequest(
			"http://localhost/0/rest/DataForgeSchemaReadService/GetSimilarTableNames",
			Arg.Any<string>(),
			DataForgeReadClient.RequestTimeoutMs,
			1,
			1);
	}

	[Test]
	[Category("Unit")]
	[Description("FindSimilarLookups should call the Creatio proxy route with a bounded timeout and map lookup results.")]
	public void FindSimilarLookups_Should_Call_Proxy_Route_With_Timeout_And_Map_Result() {
		// Arrange
		(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder, IDataForgePlatformVersionGuard versionGuard) =
			CreateDependencies("rest/DataForgeSchemaReadService/GetLookupValues", "http://localhost/0/rest/DataForgeSchemaReadService/GetLookupValues");
		applicationClient.ExecutePostRequest(
				"http://localhost/0/rest/DataForgeSchemaReadService/GetLookupValues",
				Arg.Is<string>(body => body.Contains("\"query\":\"industry\"") && body.Contains("\"schemaName\":\"Industry\"")),
				DataForgeReadClient.RequestTimeoutMs,
				1,
				1)
			.Returns("""{"GetLookupValuesResult":{"Success":true,"Data":[{"valueId":"lookup-id","referenceSchemaName":"Industry","valueName":"Manufacturing","vectorSimilarityScore":0.88}]}}""");
		DataForgeReadClient client = new(applicationClient, serviceUrlBuilder, versionGuard);

		// Act
		IReadOnlyList<SimilarLookupResult> result = client.FindSimilarLookups("industry", "Industry");

		// Assert
		result.Should().ContainSingle(because: "one proxy lookup result should map to one DataForge lookup result");
		result[0].SchemaName.Should().Be("Industry", because: "lookup schema names should be preserved from the proxy payload");
		result[0].Score.Should().Be(0.88m, because: "similarity scores should be converted to decimals for the MCP response");
		versionGuard.Received(1).EnsureSupported();
	}

	[Test]
	[Category("Unit")]
	[Description("GetTableRelationships should call the Creatio proxy route with a bounded timeout and return relation paths.")]
	public void GetTableRelationships_Should_Call_Proxy_Route_With_Timeout_And_Map_Result() {
		// Arrange
		(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder, IDataForgePlatformVersionGuard versionGuard) =
			CreateDependencies("rest/DataForgeSchemaReadService/GetTableRelationships", "http://localhost/0/rest/DataForgeSchemaReadService/GetTableRelationships");
		applicationClient.ExecutePostRequest(
				"http://localhost/0/rest/DataForgeSchemaReadService/GetTableRelationships",
				Arg.Is<string>(body => body.Contains("\"sourceTable\":\"Contact\"") && body.Contains("\"targetTable\":\"Account\"")),
				DataForgeReadClient.RequestTimeoutMs,
				1,
				1)
			.Returns("""{"GetTableRelationshipsResult":{"Success":true,"Paths":["(Contact)-[:Account]->(Account)"]}}""");
		DataForgeReadClient client = new(applicationClient, serviceUrlBuilder, versionGuard);

		// Act
		IReadOnlyList<string> result = client.GetTableRelationships("Contact", "Account");

		// Assert
		result.Should().Equal(["(Contact)-[:Account]->(Account)"],
			because: "relationship paths should pass through from the Creatio proxy response");
		versionGuard.Received(1).EnsureSupported();
	}

	[Test]
	[Category("Unit")]
	[Description("GetTableColumnsDetails should call the Creatio proxy route with a bounded timeout and map column details.")]
	public void GetTableColumnsDetails_Should_Call_Proxy_Route_With_Timeout_And_Map_Result() {
		// Arrange
		(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder, IDataForgePlatformVersionGuard versionGuard) =
			CreateDependencies("rest/DataForgeSchemaReadService/GetTableColumnsDetails", "http://localhost/0/rest/DataForgeSchemaReadService/GetTableColumnsDetails");
		applicationClient.ExecutePostRequest(
				"http://localhost/0/rest/DataForgeSchemaReadService/GetTableColumnsDetails",
				Arg.Is<string>(body => body.Contains("\"tableName\":\"Contact\"")),
				DataForgeReadClient.RequestTimeoutMs,
				1,
				1)
			.Returns("""{"GetTableColumnsDetailsResult":{"Success":true,"Data":{"tableName":"Contact","columns":[{"columnName":"Name","columnCaption":"Full name","columnType":"Text","columnRequired":true}]}}}""");
		DataForgeReadClient client = new(applicationClient, serviceUrlBuilder, versionGuard);

		// Act
		IReadOnlyList<DataForgeColumnResult> result = client.GetTableColumnsDetails("Contact");

		// Assert
		result.Should().ContainSingle(because: "one proxy column result should map to one DataForge column result");
		result[0].Name.Should().Be("Name", because: "column names should be preserved from the proxy payload");
		result[0].Required.Should().BeTrue(because: "required flags should be preserved from the proxy payload");
		versionGuard.Received(1).EnsureSupported();
	}

	[Test]
	[Category("Unit")]
	[Description("Proxy error responses should be surfaced as InvalidOperationException messages.")]
	public void FindSimilarTables_Should_Throw_When_Proxy_Returns_Error() {
		// Arrange
		(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder, IDataForgePlatformVersionGuard versionGuard) =
			CreateDependencies("rest/DataForgeSchemaReadService/GetSimilarTableNames", "http://localhost/0/rest/DataForgeSchemaReadService/GetSimilarTableNames");
		applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Any<string>(),
				DataForgeReadClient.RequestTimeoutMs,
				1,
				1)
			.Returns("""{"GetSimilarTableNamesResult":{"Success":false,"ErrorInfo":{"Message":"proxy failed"}}}""");
		DataForgeReadClient client = new(applicationClient, serviceUrlBuilder, versionGuard);

		// Act
		Action action = () => client.FindSimilarTables("customer");

		// Assert
		action.Should().Throw<InvalidOperationException>()
			.WithMessage("proxy failed", because: "proxy error messages should be surfaced to MCP callers");
	}

	private static (IApplicationClient ApplicationClient, IServiceUrlBuilder ServiceUrlBuilder,
			IDataForgePlatformVersionGuard VersionGuard)
		CreateDependencies(string route, string url) {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IDataForgePlatformVersionGuard versionGuard = Substitute.For<IDataForgePlatformVersionGuard>();
		serviceUrlBuilder.Build(route).Returns(url);
		return (applicationClient, serviceUrlBuilder, versionGuard);
	}
}
