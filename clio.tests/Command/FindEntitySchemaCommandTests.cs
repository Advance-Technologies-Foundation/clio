using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
internal class FindEntitySchemaCommandTests : BaseCommandTests<FindEntitySchemaOptions>
{
	private FindEntitySchemaCommand _command;
	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ILogger _logger;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<FindEntitySchemaCommand>();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_logger = Substitute.For<ILogger>();
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select).Returns("http://localhost/select");
		containerBuilder.AddSingleton(_applicationClient);
		containerBuilder.AddSingleton(_serviceUrlBuilder);
		containerBuilder.AddSingleton(_logger);
	}

	[Test]
	[Description("FindSchemas returns parsed results when the DataService response contains matching rows.")]
	public void FindSchemas_ReturnsParsedResults_WhenResponseContainsRows() {
		// Arrange
		FindEntitySchemaOptions options = new() { SchemaName = "UsrTask" };
		string json = BuildSuccessJson([
			new FindSchemaRow("UsrTask", "aaa", "UsrTaskApp", "Advance", "BaseEntity")
		]);
		_applicationClient
			.ExecutePostRequest("http://localhost/select", Arg.Any<string>())
			.Returns(json);

		// Act
		IReadOnlyList<EntitySchemaSearchResult> results = _command.FindSchemas(options);

		// Assert
		results.Should().HaveCount(1, "one matching row was returned by the DataService");
		results[0].SchemaName.Should().Be("UsrTask", "schema name maps from the Name column");
		results[0].PackageName.Should().Be("UsrTaskApp", "package name maps from PackageName");
		results[0].PackageMaintainer.Should().Be("Advance", "maintainer maps from PackageMaintainer");
		results[0].ParentSchemaName.Should().Be("BaseEntity", "parent schema name maps from ParentSchemaName");
	}

	[Test]
	[Description("FindSchemas returns empty list when the DataService response contains no rows.")]
	public void FindSchemas_ReturnsEmptyList_WhenNoRowsReturned() {
		// Arrange
		FindEntitySchemaOptions options = new() { SearchPattern = "NoMatch" };
		string json = BuildSuccessJson([]);
		_applicationClient
			.ExecutePostRequest("http://localhost/select", Arg.Any<string>())
			.Returns(json);

		// Act
		IReadOnlyList<EntitySchemaSearchResult> results = _command.FindSchemas(options);

		// Assert
		results.Should().BeEmpty("no rows were returned by DataService");
	}

	[Test]
	[Description("FindSchemas uses the Creatio contains comparison type for search-pattern filters.")]
	public void FindSchemas_UsesContainsComparisonType_WhenSearchPatternProvided() {
		// Arrange
		const int containsComparisonType = 11;
		FindEntitySchemaOptions options = new() { SearchPattern = "Task" };
		string json = BuildSuccessJson([
			new FindSchemaRow("UsrTask", "aaa", "UsrTaskApp", "Advance", "BaseEntity")
		]);
		List<string> requests = [];
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(request => requests.Add(request)))
			.Returns(json);

		// Act
		_command.FindSchemas(options);

		// Assert
		requests.Should().NotBeEmpty(
			"the command should send a SelectQuery request to DataService");
		requests.Should().ContainSingle(
			"a non-empty filtered result should remain on the normal one-query fast path");
		using JsonDocument document = JsonDocument.Parse(requests[0]);
		JsonElement? searchPatternFilter = null;
		foreach (JsonProperty item in document
			.RootElement
			.GetProperty("filters")
			.GetProperty("items")
			.EnumerateObject()) {
			JsonElement filter = item.Value;
			if (filter.GetProperty("leftExpression").GetProperty("columnPath").GetString() == "Name"
				&& filter.GetProperty("rightExpression").GetProperty("parameter").GetProperty("value").GetString() == "Task") {
				searchPatternFilter = filter;
				break;
			}
		}
		searchPatternFilter.Should().NotBeNull(
			"the SelectQuery should include a filter for the requested search pattern");
		searchPatternFilter.Value.GetProperty("comparisonType").GetInt32().Should().Be(
			containsComparisonType,
			"search-pattern must use Creatio's contains comparison type");
	}

	[Test]
	[Description("FindSchemas cross-checks an empty server-side pattern result with a broader query and filters it case-insensitively.")]
	public void FindSchemas_ShouldCrossCheckBroaderQuery_WhenSearchPatternReturnsEmpty() {
		// Arrange
		FindEntitySchemaOptions options = new() { SearchPattern = "reserv" };
		List<string> requests = [];
		_applicationClient
			.ExecutePostRequest("http://localhost/select", Arg.Do<string>(request => requests.Add(request)))
			.Returns(
				BuildSuccessJson([]),
				BuildSuccessJson([
					new FindSchemaRow("labReservation", "aaa", "labFORENOM", "Creatio", "BaseEntity"),
					new FindSchemaRow("labOffer", "bbb", "labFORENOM", "Creatio", "BaseEntity")
				]));

		// Act
		IReadOnlyList<EntitySchemaSearchResult> results = _command.FindSchemas(options);

		// Assert
		results.Should().ContainSingle(
			result => result.SchemaName == "labReservation",
			"an empty server-side contains result must be cross-checked before reporting the schema absent");
		requests.Should().HaveCount(2,
			"the broader read should run only after the filtered query returns no rows");
		requests[0].Should().Contain("reserv",
			"the fast-path request should retain the requested server-side contains filter");
		requests[1].Should().NotContain("reserv",
			"the fallback request must remove the unreliable contains filter before filtering locally");
	}

	[TestCase(false)]
	[TestCase(true)]
	[Description("FindSchemas refuses to return an incomplete result when the broader cross-check reaches its safety bound.")]
	public void FindSchemas_ShouldThrow_WhenBroaderCrossCheckReachesSafetyBound(bool includesMatch) {
		// Arrange
		FindEntitySchemaOptions options = new() { SearchPattern = "missing" };
		IEnumerable<FindSchemaRow> cappedRows = Enumerable.Range(0, 10000)
			.Select(index => new FindSchemaRow(
				includesMatch && index == 0 ? "UsrMissingSchema" : $"UsrSchema{index}",
				Guid.NewGuid().ToString(),
				"UsrPackage",
				"Advance",
				"BaseEntity"));
		_applicationClient
			.ExecutePostRequest("http://localhost/select", Arg.Any<string>())
			.Returns(BuildSuccessJson([]), BuildSuccessJson(cappedRows));

		// Act
		Action act = () => _command.FindSchemas(options);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "a saturated broader query cannot prove that all matching schemas were returned")
			.WithMessage("*10000-row safety bound*--schema-name or --uid*",
				because: "the failure should explain the bound and direct callers to an exact lookup");
	}

	[Test]
	[Description("FindSchemas sets ParentSchemaName to null when the DataService row has an empty parent.")]
	public void FindSchemas_SetsParentSchemaNameToNull_WhenParentIsEmpty() {
		// Arrange
		FindEntitySchemaOptions options = new() { SchemaName = "UsrTask" };
		string json = BuildSuccessJson([
			new FindSchemaRow("UsrTask", "aaa", "UsrTaskApp", "Advance", "")
		]);
		_applicationClient
			.ExecutePostRequest("http://localhost/select", Arg.Any<string>())
			.Returns(json);

		// Act
		IReadOnlyList<EntitySchemaSearchResult> results = _command.FindSchemas(options);

		// Assert
		results[0].ParentSchemaName.Should().BeNull(
			"empty parent schema name should be normalized to null");
	}

	[Test]
	[Description("FindSchemas throws ArgumentException when none of schema-name, search-pattern, or uid is provided.")]
	public void FindSchemas_ThrowsArgumentException_WhenNoSearchCriteriaProvided() {
		// Arrange
		FindEntitySchemaOptions options = new();

		// Act
		Action act = () => _command.FindSchemas(options);

		// Assert
		act.Should().Throw<ArgumentException>(
			"at least one search criterion is required");
	}

	[Test]
	[Description("FindSchemas throws ArgumentException when uid is not a valid Guid.")]
	public void FindSchemas_ThrowsArgumentException_WhenUidIsInvalidGuid() {
		// Arrange
		FindEntitySchemaOptions options = new() { Uid = "not-a-guid" };

		// Act
		Action act = () => _command.FindSchemas(options);

		// Assert
		act.Should().Throw<ArgumentException>(
			"the uid option must contain a parseable Guid value");
	}

	[Test]
	[Description("Execute returns 0 and logs labeled schema ownership fields when matching schemas are found.")]
	public void Execute_ReturnsZeroAndLogsResults_WhenSchemasFound() {
		// Arrange
		FindEntitySchemaOptions options = new() { SearchPattern = "UsrTask" };
		string json = BuildSuccessJson([
			new FindSchemaRow("UsrTask", "aaa", "UsrTaskApp", "Advance", "BaseEntity")
		]);
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(json);

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, "command succeeds when results are found");
		_logger.Received(1).WriteInfo(Arg.Is<string>(s =>
			s == "Schema: UsrTask | Package: UsrTaskApp | Maintainer: Advance | Parent: BaseEntity"));
	}

	[Test]
	[Description("Execute omits the parent segment when a matching schema has no parent schema name.")]
	public void Execute_OmitsParentSegment_WhenSchemaHasNoParent() {
		// Arrange
		FindEntitySchemaOptions options = new() { SchemaName = "UsrTask" };
		string json = BuildSuccessJson([
			new FindSchemaRow("UsrTask", "aaa", "UsrTaskApp", "Advance", "")
		]);
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(json);

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, "command succeeds when a matching schema without parent is found");
		_logger.Received(1).WriteInfo(Arg.Is<string>(s =>
			s == "Schema: UsrTask | Package: UsrTaskApp | Maintainer: Advance"));
	}

	[Test]
	[Description("Execute returns 0 and logs 'No entity schemas found' when no schemas match the search.")]
	public void Execute_ReturnsZeroAndLogsNotFound_WhenNoSchemasMatch() {
		// Arrange
		FindEntitySchemaOptions options = new() { SearchPattern = "NoMatch" };
		string json = BuildSuccessJson([]);
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(json);

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, "an empty result is not an error");
		_logger.Received(1).WriteInfo(Arg.Is<string>(s => s.Contains("No entity schemas found")));
	}

	[Test]
	[Description("Execute returns 1 and logs an error when no search criteria are provided.")]
	public void Execute_ReturnsOne_WhenNoSearchCriteriaProvided() {
		// Arrange
		FindEntitySchemaOptions options = new();

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, "missing search criteria is a usage error");
		_logger.Received(1).WriteError(Arg.Any<string>());
	}

	[Test]
	[Description("FindSchemas passes uid as a Guid value type filter in the DataService request.")]
	public void FindSchemas_SendsRequest_WhenUidIsValidGuid() {
		// Arrange
		string validGuid = Guid.NewGuid().ToString();
		FindEntitySchemaOptions options = new() { Uid = validGuid };
		string json = BuildSuccessJson([]);
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(json);

		// Act
		Action act = () => _command.FindSchemas(options);

		// Assert
		act.Should().NotThrow("a valid Guid is accepted without validation errors");
	}

	[Test]
	[Description("FindSchemas surfaces a typed error naming the endpoint when DataService returns an HTML page.")]
	public void FindSchemas_ThrowsTypedError_WhenDataServiceReturnsHtml() {
		// Arrange — the ENG-93365 reproduction: an exact schema-name lookup whose endpoint answers with HTML.
		FindEntitySchemaOptions options = new() { SchemaName = "Contact" };
		_applicationClient
			.ExecutePostRequest("http://localhost/select", Arg.Any<string>())
			.Returns("<!DOCTYPE html><html><body>Runtime Error</body></html>");

		// Act
		Action act = () => _command.FindSchemas(options);

		// Assert
		InvalidOperationException exception = act.Should().Throw<InvalidOperationException>(
			"a non-JSON response is a reportable endpoint failure")
			.Which;
		exception.Message.Should().Contain("HTML page instead of JSON",
			"the agent must be told what the endpoint actually returned");
		exception.Message.Should().Contain("http://localhost/select",
			"the message must name the endpoint that failed");
		exception.Message.Should().NotContain("is an invalid start of a value",
			"the raw System.Text.Json parser message must never reach the caller (ENG-93365)");
	}

	[Test]
	[Description("FindSchemas surfaces a typed error with a response preview when DataService returns truncated JSON.")]
	public void FindSchemas_ThrowsTypedErrorWithPreview_WhenDataServiceReturnsTruncatedJson() {
		// Arrange
		FindEntitySchemaOptions options = new() { SchemaName = "Contact" };
		_applicationClient
			.ExecutePostRequest("http://localhost/select", Arg.Any<string>())
			.Returns("""{"success":tr""");

		// Act
		Action act = () => _command.FindSchemas(options);

		// Assert
		string message = act.Should().Throw<InvalidOperationException>(
			"a truncated body cannot be parsed and must be reported")
			.Which.Message;
		message.Should().Contain("unparseable response",
			"the message must state that the body could not be parsed");
		message.Should().Contain("Response preview:",
			"the caller needs the actual body to diagnose the endpoint");
		message.Should().NotContain("is an invalid start of a value",
			"the raw System.Text.Json parser message must never reach the caller (ENG-93365)");
	}

	[Test]
	[Description("Execute returns 1 and logs the typed endpoint error when DataService returns an HTML page.")]
	public void Execute_ReturnsOneAndLogsTypedError_WhenDataServiceReturnsHtml() {
		// Arrange
		FindEntitySchemaOptions options = new() { SchemaName = "Contact" };
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns("<html><body>Runtime Error</body></html>");

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1, "an unusable endpoint response is a command failure");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("HTML page instead of JSON")
			&& !message.Contains("is an invalid start of a value")));
	}

	private static string BuildSuccessJson(IEnumerable<FindSchemaRow> rows) {
		object response = new {
			success = true,
			rows = rows
		};
		return JsonSerializer.Serialize(response);
	}

	private sealed record FindSchemaRow(
		[property: System.Text.Json.Serialization.JsonPropertyName("Name")]
		string Name,
		[property: System.Text.Json.Serialization.JsonPropertyName("UId")]
		string UId,
		[property: System.Text.Json.Serialization.JsonPropertyName("PackageName")]
		string PackageName,
		[property: System.Text.Json.Serialization.JsonPropertyName("PackageMaintainer")]
		string PackageMaintainer,
		[property: System.Text.Json.Serialization.JsonPropertyName("ParentSchemaName")]
		string ParentSchemaName
	);
}
