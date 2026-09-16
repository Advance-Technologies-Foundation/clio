using System.Text.Json;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class SchemaCreateBodyContractTests {
	[Test]
	[Description("Publishes optional body and body-file inputs in the real MCP input schema, including file precedence.")]
	public void InputSchema_ShouldAdvertiseOptionalBodyInputs_WhenCreateSchemaIsDiscovered() {
		// Arrange
		using JsonDocument schema = EmittedSchemaProbe.EmittedInputSchema(SchemaCreateTool.ToolName);
		// Act
		JsonElement args = schema.RootElement.GetProperty("properties").GetProperty("args");
		string[] required = EmittedSchemaProbe.RequiredNames(args);
		// Assert
		EmittedSchemaProbe.PropertyDescription(args, "body").Should().NotBeNullOrWhiteSpace(
			because: "callers must discover the inline source input");
		EmittedSchemaProbe.PropertyDescription(args, "body-file").Should().Contain("precedence",
			because: "callers must discover file input and its precedence");
		required.Should().NotContain("body", because: "body-less creation remains supported");
		required.Should().NotContain("body-file", because: "a local source file is optional");
	}
}
