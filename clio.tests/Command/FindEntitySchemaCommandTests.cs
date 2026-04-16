using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
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
