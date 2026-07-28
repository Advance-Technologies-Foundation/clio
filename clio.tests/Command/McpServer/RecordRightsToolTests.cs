using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Server;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class RecordRightsToolTests {

	[Test]
	[Category("Unit")]
	[Description("Declares get-record-rights as a read-only, non-destructive, idempotent, closed-world probe under its canonical name.")]
	public void GetRecordRightsTool_ShouldDeclareReadOnlySafetyFlags_WhenInspectingAttribute() {
		// Arrange & Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(GetRecordRightsTool)
			.GetMethod(nameof(GetRecordRightsTool.GetRecordRights))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(GetRecordRightsTool.ToolName, because: "the read tool publishes under its canonical kebab-case name");
		attribute.ReadOnly.Should().BeTrue(because: "reading rights never mutates state");
		attribute.Destructive.Should().BeFalse(because: "a read is never destructive");
	}

	[Test]
	[Category("Unit")]
	[Description("Declares set-record-rights as a destructive, non-read-only tool under its canonical name.")]
	public void SetRecordRightsTool_ShouldDeclareDestructiveSafetyFlags_WhenInspectingAttribute() {
		// Arrange & Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(SetRecordRightsTool)
			.GetMethod(nameof(SetRecordRightsTool.SetRecordRights))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(SetRecordRightsTool.ToolName, because: "the write tool publishes under its canonical kebab-case name");
		attribute.ReadOnly.Should().BeFalse(because: "granting/revoking rights mutates state");
		attribute.Destructive.Should().BeTrue(because: "a last-writer-wins apply is destructive");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks the single args wrapper as schema-required on set-record-rights so an omitted args object fails with a structured error.")]
	public void SetRecordRightsTool_ShouldRequireArgsWrapper_WhenInspectingMethodSignature() {
		// Arrange & Act
		object[] required = typeof(SetRecordRightsTool)
			.GetMethod(nameof(SetRecordRightsTool.SetRecordRights))!
			.GetParameters()[0]
			.GetCustomAttributes(typeof(RequiredAttribute), false);

		// Assert
		required.Should().NotBeEmpty(because: "the args wrapper must be schema-required so an omitted args object fails cleanly");
	}

	[Test]
	[Category("Unit")]
	[Description("Binds the set-record-rights arguments from kebab-case JSON using the real MCP serializer options.")]
	public void SetRecordRightsArgs_ShouldBindKebabCaseFields_WhenDeserializedWithMcpOptions() {
		// Arrange
		JsonSerializerOptions options = Clio.BindingsModule.CreateMcpSerializerOptions();

		// Act
		SetRecordRightsArgs args = JsonSerializer.Deserialize<SetRecordRightsArgs>(
			"""{"environment-name":"sandbox","entity":"Contact","record-id":"rec-1","grantee":"Everyone","operation":"read","level":"delegated","revoke":true}""",
			options)!;

		// Assert
		args.EnvironmentName.Should().Be("sandbox", because: "the kebab-case environment-name binds");
		args.Entity.Should().Be("Contact", because: "the entity binds");
		args.RecordId.Should().Be("rec-1", because: "the kebab-case record-id binds");
		args.Grantee.Should().Be("Everyone", because: "the grantee binds");
		args.Operation.Should().Be("read", because: "the operation binds");
		args.Level.Should().Be("delegated", because: "the level binds");
		args.Revoke.Should().BeTrue(because: "the revoke flag binds");
	}

	[Test]
	[Category("Unit")]
	[Description("Binds the get-record-rights arguments from kebab-case JSON using the real MCP serializer options.")]
	public void GetRecordRightsArgs_ShouldBindKebabCaseFields_WhenDeserializedWithMcpOptions() {
		// Arrange
		JsonSerializerOptions options = Clio.BindingsModule.CreateMcpSerializerOptions();

		// Act
		GetRecordRightsArgs args = JsonSerializer.Deserialize<GetRecordRightsArgs>(
			"""{"environment-name":"sandbox","entity":"Contact","record-id":"rec-1"}""",
			options)!;

		// Assert
		args.EnvironmentName.Should().Be("sandbox", because: "the kebab-case environment-name binds");
		args.Entity.Should().Be("Contact", because: "the entity binds");
		args.RecordId.Should().Be("rec-1", because: "the kebab-case record-id binds");
	}
}
