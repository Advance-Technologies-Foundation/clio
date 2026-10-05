using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Package;

/// <summary>
/// Covers the transient-failure retry that <see cref="SelectQueryHelper.ExecuteSelectQuery{T}" /> applies to
/// server-reported failures (issue #1119) and the wire shape of the SelectQuery builders.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public class SelectQueryHelperTests {

	#region Constants: Private

	private const string SelectUrl = "https://test.creatio.com/0/DataService/json/SyncReply/SelectQuery";

	private const string TransientFailureJson =
		"""{"success":false,"errorInfo":{"message":"System.InvalidOperationException: Collection was modified; enumeration operation may not execute."}}""";

	private const string SuccessJson = """{"success":true}""";

	private static readonly SelectQueryHelper.SelectQueryColumnDefinition[] Columns = [
		new("Id", "Id"),
		new("Name", "Name")
	];

	#endregion

	#region Fields: Private

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;

	#endregion

	#region Methods: Public

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns(SelectUrl);
	}

	[TearDown]
	public void TearDown() {
		_applicationClient.ClearReceivedCalls();
		_serviceUrlBuilder.ClearReceivedCalls();
	}

	[Test]
	[Description("Re-sends a SelectQuery whose HTTP-200 body reports a transient server failure, and returns the response of the send that succeeded.")]
	public void ExecuteSelectQuery_Should_Retry_Transient_Server_Failure() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(TransientFailureJson, SuccessJson);

		// Act
		TestSelectResponse response = SelectQueryHelper.ExecuteSelectQuery<TestSelectResponse>(
			_applicationClient, _serviceUrlBuilder, new { rootSchemaName = "SysPackage" });

		// Assert
		response.Success.Should().BeTrue(
			because: "the second send answered with a success envelope, so that is the answer the caller gets");
		_applicationClient.Received(2).ExecutePostRequest(
			SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Fails immediately, without re-sending, when the server reports a failure that a re-send cannot clear.")]
	public void ExecuteSelectQuery_Should_Not_Retry_NonTransient_Server_Failure() {
		// Arrange
		const string notFoundJson =
			"""{"success":false,"errorInfo":{"message":"Package 'UsrMissing' was not found."}}""";
		_applicationClient.ExecutePostRequest(
				SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(notFoundJson);

		// Act
		Action act = () => SelectQueryHelper.ExecuteSelectQuery<TestSelectResponse>(
			_applicationClient, _serviceUrlBuilder, new { rootSchemaName = "SysPackage" });

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*was not found*",
				because: "a real answer must reach the caller unchanged instead of being retried and delayed");
		_applicationClient.Received(1).ExecutePostRequest(
			SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Gives up after the transient-retry budget is spent and surfaces the server-reported failure text.")]
	public void ExecuteSelectQuery_Should_Throw_After_Transient_Retry_Budget_Is_Spent() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(TransientFailureJson);

		// Act
		Action act = () => SelectQueryHelper.ExecuteSelectQuery<TestSelectResponse>(
			_applicationClient, _serviceUrlBuilder, new { rootSchemaName = "SysPackage" });

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*Collection was modified*",
				because: "a failure that outlives the retry budget must still name what the server reported");
		_applicationClient.Received(3).ExecutePostRequest(
			SelectUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Sends a bounded SelectQuery exactly once even when the server reports a transient failure, so a caller that budgeted the call keeps the bound it stated.")]
	public void ExecuteSelectQuery_Should_Not_Retry_When_Caller_Bounded_The_Call() {
		// Arrange
		const int boundedTimeoutMs = 30_000;
		_applicationClient.ExecutePostRequest(
				SelectUrl, Arg.Any<string>(), boundedTimeoutMs, Arg.Any<int>(), Arg.Any<int>())
			.Returns(TransientFailureJson);

		// Act
		Action act = () => SelectQueryHelper.ExecuteSelectQuery<TestSelectResponse>(
			_applicationClient, _serviceUrlBuilder, new { rootSchemaName = "SysPackage" }, boundedTimeoutMs);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "the failure must reach the caller instead of being retried past the budget it set");
		_applicationClient.Received(1).ExecutePostRequest(
			SelectUrl, Arg.Any<string>(), boundedTimeoutMs, Arg.Any<int>(), Arg.Any<int>());
	}


	[Test]
	[Description("Builds one IN filter (filterType 4, comparisonType 3) inside an AND group, carrying every value as an ordered right expression of the given data value type (ENG-102120).")]
	public void BuildSelectQueryWithInFilter_Should_Emit_Single_In_Filter_With_Ordered_Values() {
		// Arrange
		string[] values = [
			"33333333-3333-3333-3333-333333333333",
			"11111111-1111-1111-1111-111111111111",
			"22222222-2222-2222-2222-222222222222"
		];

		// Act
		using JsonDocument query = Serialize(SelectQueryHelper.BuildSelectQueryWithInFilter(
			"ApplicationSection", Columns, "ApplicationId", values, SelectQueryHelper.GuidDataValueType));

		// Assert
		JsonElement group = query.RootElement.GetProperty("filters");
		group.GetProperty("filterType").GetInt32().Should().Be(6,
			because: "the filters of a SelectQuery are always wrapped in a filter group");
		group.GetProperty("logicalOperation").GetInt32().Should().Be(0,
			because: "the single IN filter needs no OR between filters, so the group stays AND");
		JsonElement[] filters = FilterItems(group);
		filters.Should().ContainSingle(
			because: "the virtual ApplicationSection schema reads only the first filter on a column");
		filters[0].GetProperty("filterType").GetInt32().Should().Be(4,
			because: "filterType 4 is the DataService IN filter");
		filters[0].GetProperty("comparisonType").GetInt32().Should().Be(3,
			because: "comparisonType 3 is equality");
		filters[0].GetProperty("leftExpression").GetProperty("columnPath").GetString().Should().Be("ApplicationId",
			because: "the filter compares the requested column");
		JsonElement[] parameters = filters[0].GetProperty("rightExpressions").EnumerateArray()
			.Select(expression => expression.GetProperty("parameter")).ToArray();
		parameters.Select(parameter => parameter.GetProperty("value").GetString())
			.Should().Equal(values, because: "every value must be carried, in the order the caller passed them");
		parameters.Select(parameter => parameter.GetProperty("dataValueType").GetInt32())
			.Should().AllBeEquivalentTo(SelectQueryHelper.GuidDataValueType,
				because: "every value must carry the caller's data value type");
	}

	[Test]
	[Description("Keeps the OR builder emitting logicalOperation 1 with one equality filter per value after the shared envelope refactor.")]
	public void BuildSelectQueryWithOrFilter_Should_Emit_Or_Group_With_One_Filter_Per_Value() {
		// Arrange
		string[] values = ["a", "b"];

		// Act
		using JsonDocument query = Serialize(SelectQueryHelper.BuildSelectQueryWithOrFilter(
			"SysSchema", Columns, "Name", values, SelectQueryHelper.TextDataValueType));

		// Assert
		JsonElement group = query.RootElement.GetProperty("filters");
		group.GetProperty("logicalOperation").GetInt32().Should().Be(1,
			because: "the OR builder must combine its per-value filters with OR, or it would match nothing");
		FilterItems(group).Select(filter => filter.GetProperty("rightExpression").GetProperty("parameter")
				.GetProperty("value").GetString())
			.Should().Equal(values, because: "the OR builder emits one comparison filter per value");
	}

	[Test]
	[Description("Keeps the plain builder emitting logicalOperation 0 with its columns after the shared envelope refactor.")]
	public void BuildSelectQuery_Should_Emit_And_Group_With_Columns() {
		// Arrange
		SelectQueryHelper.SelectQueryFilterDefinition[] filters = [
			new("Name", "a", SelectQueryHelper.TextDataValueType),
			new("Code", "b", SelectQueryHelper.TextDataValueType)
		];

		// Act
		using JsonDocument query = Serialize(SelectQueryHelper.BuildSelectQuery("SysSchema", Columns, filters));

		// Assert
		query.RootElement.GetProperty("rootSchemaName").GetString().Should().Be("SysSchema",
			because: "the envelope must target the requested schema");
		query.RootElement.GetProperty("filters").GetProperty("logicalOperation").GetInt32().Should().Be(0,
			because: "the plain builder combines its filters with AND");
		query.RootElement.GetProperty("columns").GetProperty("items").EnumerateObject()
			.Select(column => column.Value.GetProperty("expression").GetProperty("columnPath").GetString())
			.Should().Equal(["Id", "Name"], because: "the envelope must carry every requested column");
	}

	#endregion

	#region Methods: Private

	private static JsonDocument Serialize(object query) => JsonDocument.Parse(JsonSerializer.Serialize(query));

	private static JsonElement[] FilterItems(JsonElement group) =>
		group.GetProperty("items").EnumerateObject().Select(item => item.Value).ToArray();

	#endregion

	#region Class: TestSelectResponse

	private sealed class TestSelectResponse : SelectQueryHelper.SelectQueryResponseBaseDto {

		[JsonPropertyName("rows")]
		public object[] Rows { get; init; } = [];

	}

	#endregion

	#region Methods: Public (column ordering)

	private static System.Text.Json.JsonElement ColumnItem(object query, string alias) =>
		System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(query)).RootElement
			.GetProperty("columns").GetProperty("items").GetProperty(alias);

	[Test]
	[Description("A column declared without ordering keeps the unordered wire defaults (orderDirection 0, orderPosition -1) in both builders, so existing callers are unaffected by the optional ordering parameters.")]
	public void Builders_Should_Emit_Unordered_Defaults_When_Column_Has_No_Ordering() {
		// Arrange
		SelectQueryHelper.SelectQueryColumnDefinition[] columns = { new("Name", "Name") };

		// Act
		object andQuery = SelectQueryHelper.BuildSelectQuery("SysSchema", columns,
			new SelectQueryHelper.SelectQueryFilterDefinition[] { new("Name", "UsrFoo", SelectQueryHelper.TextDataValueType) });
		object orQuery = SelectQueryHelper.BuildSelectQueryWithOrFilter("SysSchema", columns, "Name",
			new[] { "UsrFoo", "UsrBar" }, SelectQueryHelper.TextDataValueType);

		// Assert
		foreach (object query in new[] { andQuery, orQuery }) {
			System.Text.Json.JsonElement column = ColumnItem(query, "Name");
			column.GetProperty("orderDirection").GetInt32().Should().Be(0, because: "no ordering was requested");
			column.GetProperty("orderPosition").GetInt32().Should().Be(-1, because: "an unordered column has no sort position");
		}
	}

	[Test]
	[Description("Explicit ordering is emitted by both builders, and the OR builder combines its filters with logicalOperation 1.")]
	public void Builders_Should_Emit_Explicit_Ordering_When_Column_Is_Ordered() {
		// Arrange
		SelectQueryHelper.SelectQueryColumnDefinition[] columns = { new("ExtendParent", "ExtendParent", 1, 0) };

		// Act
		object andQuery = SelectQueryHelper.BuildSelectQuery("SysSchema", columns,
			Array.Empty<SelectQueryHelper.SelectQueryFilterDefinition>());
		object orQuery = SelectQueryHelper.BuildSelectQueryWithOrFilter("SysSchema", columns, "Name",
			new[] { "UsrFoo", "UsrBar" }, SelectQueryHelper.TextDataValueType);

		// Assert
		foreach (object query in new[] { andQuery, orQuery }) {
			System.Text.Json.JsonElement column = ColumnItem(query, "ExtendParent");
			column.GetProperty("orderDirection").GetInt32().Should().Be(1, because: "ascending was requested");
			column.GetProperty("orderPosition").GetInt32().Should().Be(0, because: "it is the primary sort key");
		}
		System.Text.Json.JsonElement orFilters = System.Text.Json.JsonDocument
			.Parse(System.Text.Json.JsonSerializer.Serialize(orQuery)).RootElement.GetProperty("filters");
		orFilters.GetProperty("logicalOperation").GetInt32().Should().Be(1, because: "the batch filter is an OR group");
		orFilters.GetProperty("items").EnumerateObject().Should().HaveCount(2, because: "one filter per value");
	}

	#endregion
}
