using System;
using System.Linq;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// ENG-103025. execute-esq given SQL text, or top-level schema-name/columns, returns the exact SelectQuery shape and,
/// where the mapping is trivially safe, the equivalent query - and executes nothing.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ExecuteEsqQueryHintsTests {

	private IApplicationClient _client = null!;
	private ExecuteEsqTool _tool = null!;

	[SetUp]
	public void SetUp() {
		_client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(_client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns("http://creatio/DataService/json/SyncReply/SelectQuery");
		_tool = new ExecuteEsqTool(resolver);
	}

	private static ExecuteEsqArgs Args(string json) => JsonSerializer.Deserialize<ExecuteEsqArgs>(json)!;

	private static string Quote(string text) => JsonSerializer.Serialize(text);

	private ExecuteEsqResponse Run(string json) => _tool.Execute(Args(json));

	private static JsonElement[] Items(JsonElement query, string section) =>
		query.GetProperty(section).GetProperty("items").EnumerateObject().Select(property => property.Value).ToArray();

	[Test]
	[Category("Unit")]
	[Description("A simple SELECT with equality filters and LIMIT comes back as the equivalent query, with inferred value types, and is not executed.")]
	public void Execute_ShouldSuggestTheEquivalentQuery_WhenGivenASimpleSelect() {
		// Arrange
		string sql = "SELECT Id, Name FROM Contact WHERE Name = 'O''Neil' AND Type = 5 AND Owner = '0a1b2c3d-1111-2222-3333-444455556666' LIMIT 7";

		// Act
		ExecuteEsqResponse response = Run($"{{\"environment-name\":\"dev\",\"query\":{Quote(sql)}}}");

		// Assert
		response.Success.Should().BeFalse(because: "SQL is not run");
		_client.ReceivedCalls().Should().BeEmpty(because: "a translated query is returned, never auto-executed");
		response.Error.Should().Contain("SQL text").And.Contain("'query' argument");
		JsonElement query = response.SuggestedQuery!.Value;
		query.GetProperty("rootSchemaName").GetString().Should().Be("Contact");
		query.GetProperty("rowCount").GetInt32().Should().Be(7);
		query.GetProperty("allColumns").GetBoolean().Should().BeFalse();
		query.GetProperty("columns").GetProperty("items").EnumerateObject().Select(p => p.Name)
			.Should().Equal("Id", "Name");
		JsonElement[] filters = Items(query, "filters");
		filters.Should().HaveCount(3);
		JsonElement text = filters[0].GetProperty("rightExpression").GetProperty("parameter");
		text.GetProperty("value").GetString().Should().Be("O'Neil", because: "a doubled quote is an escaped quote");
		text.GetProperty("dataValueType").GetInt32().Should().Be(1);
		JsonElement number = filters[1].GetProperty("rightExpression").GetProperty("parameter");
		number.GetProperty("value").GetInt64().Should().Be(5);
		number.GetProperty("dataValueType").GetInt32().Should().Be(4);
		filters[2].GetProperty("rightExpression").GetProperty("parameter").GetProperty("dataValueType").GetInt32()
			.Should().Be(0, because: "a quoted GUID is a Guid value");
	}

	[Test]
	[Category("Unit")]
	[Description("The suggested query is accepted by the same tool: resending it as 'query' executes it.")]
	public void Execute_ShouldRunTheSuggestedQuery_WhenItIsResent() {
		// Arrange
		ExecuteEsqResponse hint = Run($"{{\"environment-name\":\"dev\",\"query\":{Quote("select Id from Contact")}}}");
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true,\"rows\":[{\"Id\":\"1\"}]}");

		// Act
		ExecuteEsqResponse response = _tool.Execute(new ExecuteEsqArgs {
			EnvironmentName = "dev", Query = hint.SuggestedQuery!.Value
		});

		// Assert
		response.Success.Should().BeTrue(because: "advice the tool would refuse is worse than none");
		hint.SuggestedQuery!.Value.GetProperty("rowCount").GetInt32().Should().Be(ExecuteEsqQueryHints.DefaultRowCount,
			because: "no row cap was given, and the default is named in the message");
		hint.Error.Should().Contain("default");
	}

	[TestCase("SELECT * FROM Contact")]
	[TestCase("SELECT Id FROM Contact c WHERE c.Name = 'x'")]
	[TestCase("SELECT Id FROM Contact JOIN Account ON 1 = 1")]
	[TestCase("SELECT Id FROM Contact WHERE Name = 'a' OR Name = 'b'")]
	[TestCase("SELECT Id FROM Contact WHERE Age > 5")]
	[TestCase("SELECT COUNT(Id) FROM Contact")]
	[TestCase("SELECT Id FROM Contact ORDER BY Name")]
	[TestCase("SELECT Owner.Name FROM Contact")]
	[TestCase("SELECT Id AS Key FROM Contact")]
	[TestCase("SELECT Id FROM Contact WHERE Name = 'a'; DROP TABLE Contact")]
	[Category("Unit")]
	[Description("SQL that is not a single-table list of plain columns with simple equality filters gets the shape and NO suggestion.")]
	public void Execute_ShouldNotSuggest_WhenTheSqlIsNotTriviallySafe(string sql) {
		// Act
		ExecuteEsqResponse response = Run($"{{\"environment-name\":\"dev\",\"query\":{Quote(sql)}}}");

		// Assert
		response.Success.Should().BeFalse();
		response.SuggestedQuery.Should().BeNull(because: "a wrong guess would run a different read than the one intended");
		response.Error.Should().Contain("SQL text").And.Contain("rootSchemaName",
			because: "the exact shape is still given");
		_client.ReceivedCalls().Should().BeEmpty();
	}

	[Test]
	[Category("Unit")]
	[Description("SQL sent under a sql key instead of query is recognised the same way.")]
	public void Execute_ShouldReadSqlFromASqlKey() {
		// Act
		ExecuteEsqResponse response = Run($"{{\"environment-name\":\"dev\",\"sql\":{Quote("SELECT Id, Name FROM dbo.Contact;")}}}");

		// Assert
		response.SuggestedQuery!.Value.GetProperty("rootSchemaName").GetString().Should().Be("Contact");
	}

	[Test]
	[Category("Unit")]
	[Description("Top-level schema-name/columns/filters/row-count are mapped onto the query; execution is left to the caller.")]
	public void Execute_ShouldSuggestTheQuery_WhenSchemaAndColumnsAreTopLevel() {
		// Act
		ExecuteEsqResponse response = Run(
			"{\"environment-name\":\"dev\",\"schema-name\":\"Contact\",\"columns\":[\"Id\",\"Name\"],"
			+ "\"filters\":{\"Name\":\"John\",\"Type\":3},\"row-count\":5}");

		// Assert
		response.Success.Should().BeFalse();
		_client.ReceivedCalls().Should().BeEmpty();
		JsonElement query = response.SuggestedQuery!.Value;
		query.GetProperty("rootSchemaName").GetString().Should().Be("Contact");
		query.GetProperty("rowCount").GetInt32().Should().Be(5);
		Items(query, "filters").Should().HaveCount(2);
		response.Error.Should().Contain("'query' argument");
	}

	[Test]
	[Category("Unit")]
	[Description("A SelectQuery object that only lacks its rootSchemaName but carries a flat schema-name is completed the same way.")]
	public void Execute_ShouldSuggest_WhenTheQueryObjectIsMissingItsRootSchema() {
		// Act
		ExecuteEsqResponse response = Run(
			"{\"environment-name\":\"dev\",\"query\":{\"schemaName\":\"Account\",\"columns\":[\"Name\"]}}");

		// Assert
		response.SuggestedQuery!.Value.GetProperty("rootSchemaName").GetString().Should().Be("Account");
	}

	[Test]
	[Category("Unit")]
	[Description("Top-level values that are not a plain schema + column list + equality object get the shape and no suggestion.")]
	public void Execute_ShouldNotSuggest_WhenTheTopLevelValuesAreNotSimple() {
		// Act
		ExecuteEsqResponse noColumns = Run("{\"environment-name\":\"dev\",\"schema-name\":\"Contact\"}");
		ExecuteEsqResponse dotted = Run("{\"environment-name\":\"dev\",\"schema-name\":\"Contact\",\"columns\":[\"Owner.Name\"]}");
		ExecuteEsqResponse range = Run(
			"{\"environment-name\":\"dev\",\"schema-name\":\"Contact\",\"columns\":[\"Id\"],\"filters\":[{\"c\":1}]}");
		ExecuteEsqResponse huge = Run(
			"{\"environment-name\":\"dev\",\"schema-name\":\"Contact\",\"columns\":[\"Id\"],\"limit\":50000}");

		// Assert
		new[] { noColumns, dotted, range, huge }.Should().OnlyContain(response => response.SuggestedQuery == null,
			because: "none of these has one obvious reading");
		new[] { noColumns, dotted, range, huge }.Should().OnlyContain(response => response.Error!.Contains("rootSchemaName"),
			because: "each still gets the exact shape");
	}

	[Test]
	[Category("Unit")]
	[Description("A call that carries a real query ignores extra top-level keys exactly as before, and an empty call keeps the original refusal.")]
	public void Execute_ShouldBehaveAsBefore_WhenThereIsNothingToTranslate() {
		// Arrange
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true,\"rows\":[]}");

		// Act
		ExecuteEsqResponse valid = Run(
			"{\"environment-name\":\"dev\",\"columns\":[\"X\"],\"query\":{\"rootSchemaName\":\"Contact\",\"rowCount\":1}}");
		ExecuteEsqResponse empty = Run("{\"environment-name\":\"dev\"}");

		// Assert
		valid.Success.Should().BeTrue(because: "a usable query wins; stray keys never changed that and still do not");
		empty.Success.Should().BeFalse();
		empty.SuggestedQuery.Should().BeNull();
		empty.Error.Should().StartWith("query must be a JSON SelectQuery object.");
	}

	[Test]
	[Category("Unit")]
	[Description("The wire JSON of a failure without a suggestion does not carry the new field.")]
	public void Execute_ShouldOmitSuggestedQuery_WhenThereIsNone() {
		// Act
		string json = JsonSerializer.Serialize(Run("{\"environment-name\":\"dev\"}"));

		// Assert
		json.Should().NotContain("suggested-query");
	}
}
