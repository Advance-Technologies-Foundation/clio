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
		response.Error.Should().Contain("SQL text").And.Contain("'query' argument", because: "the refusal names SQL as the problem and the 'query' argument as the fix");
		JsonElement query = response.SuggestedQuery!.Value;
		query.GetProperty("rootSchemaName").GetString().Should().Be("Contact", because: "the table becomes the root schema");
		query.GetProperty("rowCount").GetInt32().Should().Be(7, because: "LIMIT 7 becomes the row cap");
		query.GetProperty("allColumns").GetBoolean().Should().BeFalse(because: "only the listed columns are read");
		query.GetProperty("columns").GetProperty("items").EnumerateObject().Select(p => p.Name)
			.Should().Equal(["Id", "Name"], because: "the selected columns are carried over in order");
		JsonElement[] filters = Items(query, "filters");
		filters.Should().HaveCount(3, because: "each equality term becomes one filter");
		JsonElement text = filters[0].GetProperty("rightExpression").GetProperty("parameter");
		text.GetProperty("value").GetString().Should().Be("O'Neil", because: "a doubled quote is an escaped quote");
		text.GetProperty("dataValueType").GetInt32().Should().Be(1, because: "a quoted literal is a Text value");
		JsonElement number = filters[1].GetProperty("rightExpression").GetProperty("parameter");
		number.GetProperty("value").GetInt64().Should().Be(5, because: "an unquoted literal keeps its number");
		number.GetProperty("dataValueType").GetInt32().Should().Be(4, because: "an unquoted literal is an Integer value");
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
		hint.Error.Should().Contain("default", because: "the message says the row cap was defaulted");
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
		response.Success.Should().BeFalse(because: "a hint is a refusal, not a result");
		response.SuggestedQuery.Should().BeNull(because: "a wrong guess would run a different read than the one intended");
		response.Error.Should().Contain("SQL text").And.Contain("rootSchemaName",
			because: "the exact shape is still given");
		_client.ReceivedCalls().Should().BeEmpty(because: "nothing is executed for SQL text");
	}

	[Test]
	[Category("Unit")]
	[Description("SQL sent under a sql key instead of query is recognised the same way.")]
	public void Execute_ShouldReadSqlFromASqlKey() {
		// Act
		ExecuteEsqResponse response = Run($"{{\"environment-name\":\"dev\",\"sql\":{Quote("SELECT Id, Name FROM dbo.Contact;")}}}");

		// Assert
		response.SuggestedQuery!.Value.GetProperty("rootSchemaName").GetString().Should().Be("Contact", because: "dbo. and the trailing semicolon are dropped from the table name");
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
		response.Success.Should().BeFalse(because: "a hint is a refusal, not a result");
		_client.ReceivedCalls().Should().BeEmpty(because: "nothing is executed for top-level fields");
		JsonElement query = response.SuggestedQuery!.Value;
		query.GetProperty("rootSchemaName").GetString().Should().Be("Contact", because: "schema-name becomes the root schema");
		query.GetProperty("rowCount").GetInt32().Should().Be(5, because: "row-count becomes the row cap");
		Items(query, "filters").Should().HaveCount(2, because: "each equality entry becomes one filter");
		response.Error.Should().Contain("'query' argument", because: "the refusal points at the 'query' argument");
	}

	[Test]
	[Category("Unit")]
	[Description("A SelectQuery object that only lacks its rootSchemaName but carries a flat schema-name is completed the same way.")]
	public void Execute_ShouldSuggest_WhenTheQueryObjectIsMissingItsRootSchema() {
		// Act
		ExecuteEsqResponse response = Run(
			"{\"environment-name\":\"dev\",\"query\":{\"schemaName\":\"Account\",\"columns\":[\"Name\"]}}");

		// Assert
		response.SuggestedQuery!.Value.GetProperty("rootSchemaName").GetString().Should().Be("Account", because: "the flat schemaName completes the query object");
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
	[Description("A call that carries a well-formed query runs as before, and an empty call keeps the original refusal.")]
	public void Execute_ShouldBehaveAsBefore_WhenThereIsNothingToTranslate() {
		// Arrange
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true,\"rows\":[]}");

		// Act
		ExecuteEsqResponse valid = Run(
			"{\"environment-name\":\"dev\",\"timeout\":5000,\"query\":{\"rootSchemaName\":\"Contact\",\"rowCount\":1}}");
		ExecuteEsqResponse empty = Run("{\"environment-name\":\"dev\"}");

		// Assert
		valid.Success.Should().BeTrue(because: "a well-formed query runs as before");
		empty.Success.Should().BeFalse(because: "an empty call is still refused");
		empty.SuggestedQuery.Should().BeNull(because: "there is nothing to translate");
		empty.Error.Should().StartWith("query must be a JSON SelectQuery object.", because: "the original refusal text is kept");
	}

	[TestCase("SELECT Id FROM Contact LIMIT 99999999999")]
	[TestCase("SELECT Id FROM Contact WHERE Age = 99999999999999999999")]
	[TestCase("SELECT TOP 5 Id FROM Contact LIMIT 10")]
	[Category("Unit")]
	[Description("A row cap or integer literal out of range, or both TOP and LIMIT, gets the SQL refusal and no suggestion instead of an overflow error or a guessed cap.")]
	public void Execute_ShouldNotSuggest_WhenANumberIsOutOfRangeOrTheCapIsAmbiguous(string sql) {
		// Act
		ExecuteEsqResponse response = Run($"{{\"environment-name\":\"dev\",\"query\":{Quote(sql)}}}");

		// Assert
		response.Error.Should().Contain("SQL text", because: "the caller learns the SQL was the problem, not a .NET overflow");
		response.SuggestedQuery.Should().BeNull(because: "no single query has the same meaning");
	}

	[Test]
	[Category("Unit")]
	[Description("Two top-level keys for one part of the query, or a row cap that is not a whole number, get no suggestion, so a filter or cap is never silently dropped.")]
	public void Execute_ShouldNotSuggest_WhenTopLevelKeysConflictOrTheCapIsUnreadable() {
		// Act
		ExecuteEsqResponse twoFilters = Run("{\"environment-name\":\"dev\",\"schema-name\":\"Contact\",\"columns\":[\"Id\"],"
			+ "\"filters\":{\"Name\":\"a\"},\"where\":{\"Type\":1}}");
		ExecuteEsqResponse twoSchemas = Run(
			"{\"environment-name\":\"dev\",\"schema-name\":\"Contact\",\"table\":\"Account\",\"columns\":[\"Id\"]}");
		ExecuteEsqResponse textCap = Run(
			"{\"environment-name\":\"dev\",\"schema-name\":\"Contact\",\"columns\":[\"Id\"],\"limit\":\"5\"}");

		// Assert
		new[] { twoFilters, twoSchemas, textCap }.Should().OnlyContain(response => response.SuggestedQuery == null,
			because: "using one of two keys, or the default cap, would read something other than what was asked");
	}

	[Test]
	[Category("Unit")]
	[Description("A SelectQuery object (columns.items) that only lacks rootSchemaName keeps the original refusal naming rootSchemaName, not the top-level hint.")]
	public void Execute_ShouldKeepTheRootSchemaRefusal_WhenASelectQueryLacksOnlyItsRootSchema() {
		// Act
		ExecuteEsqResponse response = Run("{\"environment-name\":\"dev\",\"query\":"
			+ "{\"columns\":{\"items\":{\"Name\":{\"expression\":{\"columnPath\":\"Name\"}}}},\"rowCount\":5}}");

		// Assert
		response.Error.Should().NotContain("not at the top level",
			because: "everything was inside 'query'; only rootSchemaName is missing");
		response.Error.Should().Contain("rootSchemaName", because: "the refusal names the missing field");
		_client.ReceivedCalls().Should().BeEmpty(because: "nothing runs without a root schema");
	}

	[Test]
	[Category("Unit")]
	[Description("The wire JSON of a failure without a suggestion does not carry the new field.")]
	public void Execute_ShouldOmitSuggestedQuery_WhenThereIsNone() {
		// Act
		string json = JsonSerializer.Serialize(Run("{\"environment-name\":\"dev\"}"));

		// Assert
		json.Should().NotContain("suggested-query", because: "a null suggestion is omitted from the wire");
	}
	[Test]
	[Category("Unit")]
	[Description("QA ENG-103025: a SelectQuery with columns as an array is refused with the equivalent query, never run with its columns dropped (it returned 525 Id-only rows live).")]
	public void Execute_ShouldRefuseAndTranslate_WhenSelectQueryColumnsAreAnArray() {
		// Act
		ExecuteEsqResponse response = Run(
			"{\"environment-name\":\"dev\",\"query\":{\"rootSchemaName\":\"Contact\",\"columns\":[\"Name\"]}}");

		// Assert
		response.Success.Should().BeFalse(because: "running it would silently drop the columns");
		_client.ReceivedCalls().Should().BeEmpty(because: "nothing is executed while a part would be lost");
		response.Error.Should().Contain("'columns' must be an object", because: "the refusal names the wrong part and its shape");
		JsonElement query = response.SuggestedQuery!.Value;
		query.GetProperty("rootSchemaName").GetString().Should().Be("Contact", because: "the root schema is kept");
		query.GetProperty("columns").GetProperty("items").EnumerateObject().Select(p => p.Name)
			.Should().Equal(["Name"], because: "the array's column becomes a columns.items entry");
	}

	[TestCase("{\"rootSchemaName\":\"Contact\",\"columns\":{\"Name\":{}}}", "'columns' must be an object")]
	[TestCase("{\"rootSchemaName\":\"Contact\",\"filters\":[{\"Name\":\"a\"}]}", "'filters' must be a filter object")]
	[TestCase("{\"rootSchemaName\":\"Contact\",\"rowCount\":\"5\"}", "'rowCount' must be a whole number")]
	[TestCase("{\"rootSchemaName\":\"Contact\",\"where\":{\"Name\":\"a\"}}", "'where' is not a SelectQuery member")]
	[TestCase("{\"rootSchemaName\":\"Contact\",\"orderBy\":\"Name\"}", "'orderBy' is not a SelectQuery member")]
	[TestCase("{\"rootSchemaName\":\"Contact\",\"sortBy\":\"Name\"}", "'sortBy' is not a SelectQuery member")]
	[TestCase("{\"rootSchemaName\":\"Contact\",\"filters\":{\"filterType\":6,\"items\":[{\"filterType\":1}]}}", "'filters' must be a filter object")]
	[TestCase("{\"rootSchemaName\":\"Contact\",\"columns\":[\"Name\"],\"isDistinct\":true}", "no query is suggested")]
	[TestCase("{\"rootSchemaName\":\"Contact\",\"columns\":[\"Name\"],\"filters\":{\"filterType\":6,\"logicalOperation\":0}}", "no query is suggested")]
	[Category("Unit")]
	[Description("QA ENG-103025: any SelectQuery part Creatio would ignore (wrong-shaped columns, filters or rowCount, or a SQL/OData-style key) is refused, never executed without it.")]
	public void Execute_ShouldRefuse_WhenASelectQueryPartWouldBeIgnored(string query, string expected) {
		// Act
		ExecuteEsqResponse response = Run($"{{\"environment-name\":\"dev\",\"query\":{query}}}");

		// Assert
		response.Success.Should().BeFalse(because: "a part of the query would be lost");
		response.Error.Should().Contain(expected, because: "the refusal says what is wrong");
		_client.ReceivedCalls().Should().BeEmpty(because: "nothing is executed");
	}

	[Test]
	[Category("Unit")]
	[Description("QA ENG-103025: query parts sent at the top level next to a real query are refused, because running the query would drop them.")]
	public void Execute_ShouldRefuse_WhenQueryPartsAreAlsoAtTheTopLevel() {
		// Act
		ExecuteEsqResponse response = Run(
			"{\"environment-name\":\"dev\",\"columns\":[\"Name\"],\"query\":{\"rootSchemaName\":\"Contact\",\"rowCount\":1}}");

		// Assert
		response.Success.Should().BeFalse(because: "the top-level columns would be dropped");
		response.Error.Should().Contain("'columns'", because: "the stray key is named");
		_client.ReceivedCalls().Should().BeEmpty(because: "nothing is executed");
	}

	[TestCase("UPDATE Contact SET Name = 'x' WHERE Id = '0a1b2c3d-1111-2222-3333-444455556666'")]
	[TestCase("delete from Contact")]
	[TestCase("INSERT INTO Contact (Name) VALUES ('x')")]
	[Category("Unit")]
	[Description("QA ENG-103025: write SQL gets a clear read-only refusal, not a 'not valid JSON' parse error, and is not executed.")]
	public void Execute_ShouldRefuseWriteSqlAsReadOnly(string sql) {
		// Act
		ExecuteEsqResponse viaQuery = Run($"{{\"environment-name\":\"dev\",\"query\":{Quote(sql)}}}");
		ExecuteEsqResponse viaSqlKey = Run($"{{\"environment-name\":\"dev\",\"sql\":{Quote(sql)}}}");

		// Assert
		foreach (ExecuteEsqResponse response in new[] { viaQuery, viaSqlKey }) {
			response.Error.Should().Contain("execute-esq is read-only", because: "the caller learns the tool only reads")
				.And.NotContain("not valid JSON", because: "the statement is SQL, not malformed JSON");
			response.SuggestedQuery.Should().BeNull(because: "a write has no SelectQuery equivalent");
		}
		_client.ReceivedCalls().Should().BeEmpty(because: "nothing is executed");
	}
	[Test]
	[Category("Unit")]
	[Description("Explicit nulls for columns, filters or rowCount ask for nothing, so a query carrying them still runs.")]
	public void Execute_ShouldRun_WhenOptionalPartsAreExplicitlyNull() {
		// Arrange
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"success\":true,\"rows\":[]}");

		// Act
		ExecuteEsqResponse response = Run("{\"environment-name\":\"dev\",\"query\":{\"rootSchemaName\":\"Contact\","
			+ "\"allColumns\":false,\"columns\":{\"items\":{\"Id\":{\"expression\":{\"expressionType\":0,\"columnPath\":\"Id\"}}}},"
			+ "\"filters\":null,\"rowsOffset\":0,\"isPageable\":false}}");

		// Assert
		response.Success.Should().BeTrue(because: $"nothing in the query would be lost: {response.Error}");
	}
}
