namespace Clio.Tests.Command;

using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageSchemaMetadataHelperTests
{
	private const string TestBase = "http://test";
	private const string SelectQueryUrl = TestBase + "/DataService/json/SyncReply/SelectQuery";
	private const string SchemaUId = "11111111-2222-3333-4444-555555555555";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
	}

	[Test]
	[Description("QuerySysSchemaRowByUId must filter SysSchema by UId (GUID dataValueType 0) and by ClientUnitSchemaManager, projecting the requested columns.")]
	public void QuerySysSchemaRowByUId_ShouldFilterByUIdAndManager_WhenCalled() {
		// Arrange
		string capturedRequest = null;
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Do<string>(r => capturedRequest = r))
			.Returns("""{"success": true, "rows": [{"Checksum": "abc", "ModifiedOn": "2026-06-12T00:00:00"}]}""");

		// Act
		(JToken row, string error) = PageSchemaMetadataHelper.QuerySysSchemaRowByUId(
			_applicationClient, _serviceUrlBuilder, SchemaUId, ("Checksum", "Checksum"), ("ModifiedOn", "ModifiedOn"));

		// Assert
		error.Should().BeNull(because: "a successful query with a matching row must not produce an error");
		row["Checksum"]?.ToString().Should().Be("abc", because: "the projected Checksum column must be returned from the first row");
		JObject request = JObject.Parse(capturedRequest);
		request["rootSchemaName"]?.ToString().Should().Be("SysSchema", because: "the query must target the SysSchema root schema");
		JToken byUId = request["filters"]?["items"]?["byUId"];
		byUId["leftExpression"]?["columnPath"]?.ToString().Should().Be("UId", because: "the filter must compare the UId column");
		byUId["rightExpression"]?["parameter"]?["dataValueType"]?.Value<int>().Should().Be(0,
			because: "UId filters must use the GUID data value type (0), mirroring the existing SysPackage.UId filter");
		byUId["rightExpression"]?["parameter"]?["value"]?.ToString().Should().Be(SchemaUId,
			because: "the filter value must be the requested schema UId");
		JToken byManager = request["filters"]?["items"]?["byManager"];
		byManager["rightExpression"]?["parameter"]?["value"]?.ToString().Should().Be("ClientUnitSchemaManager",
			because: "only client unit schemas participate in page conflict detection");
	}

	[Test]
	[Description("QuerySysSchemaRowByUId must return a not-found error when the server responds with zero rows.")]
	public void QuerySysSchemaRowByUId_ShouldReturnError_WhenSchemaNotFound() {
		// Arrange
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns("""{"success": true, "rows": []}""");

		// Act
		(JToken row, string error) = PageSchemaMetadataHelper.QuerySysSchemaRowByUId(
			_applicationClient, _serviceUrlBuilder, SchemaUId, ("Checksum", "Checksum"));

		// Assert
		row.Should().BeNull(because: "no row exists for the requested UId");
		error.Should().Contain(SchemaUId, because: "the error must identify which schema UId was not found");
	}

	[Test]
	[Description("QuerySysSchemaRowByUId must return a query-failure error when the DataService reports success=false.")]
	public void QuerySysSchemaRowByUId_ShouldReturnError_WhenQueryFails() {
		// Arrange
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns("""{"success": false}""");

		// Act
		(JToken row, string error) = PageSchemaMetadataHelper.QuerySysSchemaRowByUId(
			_applicationClient, _serviceUrlBuilder, SchemaUId, ("Checksum", "Checksum"));

		// Assert
		row.Should().BeNull(because: "a failed query cannot yield a row");
		error.Should().Be("Failed to query schema metadata", because: "the caller needs a stable error message to degrade gracefully");
	}
}
