using System;
using System.Net.Http;
using ATF.Repository.Providers;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class SchemaNamePrefixToolTests {

	[Test]
	[Category("Unit")]
	[Description("Advertises the stable MCP tool name so callers and tests share the same production identifier.")]
	public void GetSchemaNamePrefix_Should_Advertise_Stable_Tool_Name() {
		// Arrange & Act
		string toolName = SchemaNamePrefixTool.GetSchemaNamePrefixToolName;

		// Assert
		toolName.Should().Be("get-schema-name-prefix",
			because: "the MCP tool name must stay centralized on the production type");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the active prefix from the environment when the environment is reachable and the setting is configured.")]
	public void GetSchemaNamePrefix_Should_Return_Prefix_From_Environment() {
		// Arrange
		IDataProvider dataProvider = Substitute.For<IDataProvider>();
		dataProvider.GetSysSettingValue<string>("SchemaNamePrefix").Returns("Usr");
		SysSettingsManager manager = new(dataProvider);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SysSettingsManager>(Arg.Any<EnvironmentOptions>()).Returns(manager);
		SchemaNamePrefixTool tool = new(commandResolver);

		// Act
		SchemaNamePrefixResult result = tool.GetSchemaNamePrefix(new GetSchemaNamePrefixArgs("sandbox"));

		// Assert
		result.Success.Should().BeTrue(
			because: "a reachable environment with a configured prefix should produce a success response");
		result.SchemaNamePrefix.Should().Be("Usr",
			because: "the tool should return the raw prefix value read from the environment setting");
		result.Error.Should().BeNull(
			because: "successful calls should not include an error payload");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns empty prefix when SchemaNamePrefix is not configured — callers must not add a prefix in that case.")]
	public void GetSchemaNamePrefix_Should_Return_Empty_When_Setting_Is_Not_Configured() {
		// Arrange
		IDataProvider dataProvider = Substitute.For<IDataProvider>();
		dataProvider.GetSysSettingValue<string>("SchemaNamePrefix").Returns(string.Empty);
		SysSettingsManager manager = new(dataProvider);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SysSettingsManager>(Arg.Any<EnvironmentOptions>()).Returns(manager);
		SchemaNamePrefixTool tool = new(commandResolver);

		// Act
		SchemaNamePrefixResult result = tool.GetSchemaNamePrefix(new GetSchemaNamePrefixArgs("sandbox"));

		// Assert
		result.Success.Should().BeTrue(
			because: "an unconfigured prefix is a valid state, not a failure");
		result.SchemaNamePrefix.Should().BeEmpty(
			because: "no prefix means the caller should use plain PascalCase codes without a prefix");
		result.Error.Should().BeNull(
			because: "successful empty-prefix calls should not include an error");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns success:false with a network error message when environment connectivity fails.")]
	public void GetSchemaNamePrefix_Should_Return_Error_On_Network_Failure() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SysSettingsManager>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new HttpRequestException("Connection refused."));
		SchemaNamePrefixTool tool = new(commandResolver);

		// Act
		SchemaNamePrefixResult result = tool.GetSchemaNamePrefix(new GetSchemaNamePrefixArgs("offline-env"));

		// Assert
		result.Success.Should().BeFalse(
			because: "a network failure should produce a structured error response, not propagate the exception");
		result.SchemaNamePrefix.Should().BeEmpty(
			because: "error responses must not expose a partial or defaulted prefix");
		result.Error.Should().Be("Network error reading SchemaNamePrefix.",
			because: "the error message must be a safe category label that does not expose raw exception details");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns success:false with a generic error message when environment resolution fails (unknown env name).")]
	public void GetSchemaNamePrefix_Should_Return_Error_When_Environment_Is_Not_Registered() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SysSettingsManager>(Arg.Any<EnvironmentOptions>())
			.Returns(_ => throw new InvalidOperationException("Environment 'unknown' is not registered."));
		SchemaNamePrefixTool tool = new(commandResolver);

		// Act
		SchemaNamePrefixResult result = tool.GetSchemaNamePrefix(new GetSchemaNamePrefixArgs("unknown"));

		// Assert
		result.Success.Should().BeFalse(
			because: "an unknown environment name should produce a structured error, not propagate the exception");
		result.SchemaNamePrefix.Should().BeEmpty(
			because: "error responses must not expose a partial or defaulted prefix");
		result.Error.Should().Be("Failed to read SchemaNamePrefix.",
			because: "non-network, non-auth failures should use the generic safe category label");
	}

	[Test]
	[Category("Unit")]
	[Description("Strips surrounding quotes from the setting value so agents receive a clean prefix code without punctuation.")]
	public void GetSchemaNamePrefix_Should_Strip_Surrounding_Quotes_From_Prefix() {
		// Arrange
		IDataProvider dataProvider = Substitute.For<IDataProvider>();
		dataProvider.GetSysSettingValue<string>("SchemaNamePrefix").Returns("\"Usr\"");
		SysSettingsManager manager = new(dataProvider);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<SysSettingsManager>(Arg.Any<EnvironmentOptions>()).Returns(manager);
		SchemaNamePrefixTool tool = new(commandResolver);

		// Act
		SchemaNamePrefixResult result = tool.GetSchemaNamePrefix(new GetSchemaNamePrefixArgs("sandbox"));

		// Assert
		result.Success.Should().BeTrue();
		result.SchemaNamePrefix.Should().Be("Usr",
			because: "surrounding double-quotes in the raw setting value must be stripped before returning the prefix");
	}
}
