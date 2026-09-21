using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class SchemaNamePreValidationToolTests {

	[Test]
	[Category("Unit")]
	[Description("create-page rejects an empty schema-name before resolving any command, surfacing the 'schema-name is required' message and never touching the environment resolver.")]
	public void CreatePage_ShouldReturnRequiredErrorAndNotResolve_WhenSchemaNameIsBlank() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PageCreateTool tool = new(null!, ConsoleLogger.Instance, commandResolver);

		// Act
		PageCreateResponse response = tool.CreatePage(
			new PageCreateArgs("   ", "FormPage", "UsrPackage", Caption: null, Description: null, EntitySchemaName: null) { EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeFalse(
			because: "a blank schema-name is invalid input and must fail before resolution");
		response.Error.Should().Be("schema-name is required",
			because: "the blank-name branch must surface the required-field message, mirroring the command layer");
		commandResolver.DidNotReceive().Resolve<PageCreateCommand>(Arg.Any<PageCreateOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("create-page rejects a syntactically invalid schema-name before resolving any command, surfacing the shared format message and never touching the environment resolver.")]
	public void CreatePage_ShouldReturnFormatErrorAndNotResolve_WhenSchemaNameIsInvalid() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		PageCreateTool tool = new(null!, ConsoleLogger.Instance, commandResolver);

		// Act
		PageCreateResponse response = tool.CreatePage(
			new PageCreateArgs("1BadName", "FormPage", "UsrPackage", Caption: null, Description: null, EntitySchemaName: null) { EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeFalse(
			because: "a name that does not start with a letter is invalid and must fail before resolution");
		response.Error.Should().Be(PageSchemaMetadataHelper.SchemaNameFormatError,
			because: "the invalid-format branch must surface the shared schema-name format constant");
		commandResolver.DidNotReceive().Resolve<PageCreateCommand>(Arg.Any<PageCreateOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("create-schema rejects an empty schema-name before resolving any command, surfacing the 'schema-name is required' message and never touching the environment resolver.")]
	public void CreateSchema_ShouldReturnRequiredErrorAndNotResolve_WhenSchemaNameIsBlank() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		SchemaCreateTool tool = new(null!, ConsoleLogger.Instance, commandResolver);

		// Act
		SourceCodeSchemaCreateResponse response = tool.CreateSchema(
			new SchemaCreateArgs("   ", "UsrPackage") { EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeFalse(
			because: "a blank schema-name is invalid input and must fail before resolution");
		response.Error.Should().Be("schema-name is required" + SchemaCreateTool.ValidArgumentsHint,
			because: "the blank-name branch must surface the required-field message, mirroring the command layer");
		commandResolver.DidNotReceive().Resolve<SourceCodeSchemaCreateCommand>(Arg.Any<SourceCodeSchemaCreateOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("create-schema rejects a syntactically invalid schema-name before resolving any command, surfacing the shared format message and never touching the environment resolver.")]
	public void CreateSchema_ShouldReturnFormatErrorAndNotResolve_WhenSchemaNameIsInvalid() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		SchemaCreateTool tool = new(null!, ConsoleLogger.Instance, commandResolver);

		// Act
		SourceCodeSchemaCreateResponse response = tool.CreateSchema(
			new SchemaCreateArgs("Bad Name", "UsrPackage") { EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeFalse(
			because: "a name containing a space is invalid and must fail before resolution");
		response.Error.Should().Be(PageSchemaMetadataHelper.SchemaNameFormatError + SchemaCreateTool.ValidArgumentsHint,
			because: "the invalid-format branch must surface the shared schema-name format constant");
		commandResolver.DidNotReceive().Resolve<SourceCodeSchemaCreateCommand>(Arg.Any<SourceCodeSchemaCreateOptions>());
		ConsoleLogger.Instance.ClearMessages();
	}
	[TestCase(null, null, "schema-name is required; package-name is required")]
	[TestCase(" ", "\t", "schema-name is required; package-name is required")]
	[TestCase("1BadName", null, "schema-name must start with a letter and contain only letters, digits, or underscores; package-name is required")]
	[TestCase("UsrValid", null, "package-name is required")]
	[Category("Unit")]
	[Description("Reports every name validation failure and canonical argument hint before environment resolution.")]
	public void CreateSchema_ShouldReportAllInputErrors_WhenNamesAreInvalid(string schemaName, string packageName, string expected) {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		SchemaCreateTool tool = new(null!, ConsoleLogger.Instance, commandResolver);

		// Act
		SourceCodeSchemaCreateResponse response = tool.CreateSchema(
			new SchemaCreateArgs(schemaName, packageName) { EnvironmentName = "missing-environment" });

		// Assert
		response.Success.Should().BeFalse(because: "invalid metadata must fail before any environment is used");
		response.Error.Should().Be(expected + SchemaCreateTool.ValidArgumentsHint,
			because: "the caller needs all validation failures and canonical argument names in one response");
		commandResolver.ReceivedCalls().Should().BeEmpty(because: "invalid inputs must not resolve a command or environment");
	}

}
