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
public class ObjectRightsToolTests {

	[Test]
	[Category("Unit")]
	[Description("Declares set-object-rights as a destructive, non-read-only, idempotent tool under its canonical name.")]
	public void SetObjectRightsTool_ShouldDeclareDestructiveSafetyFlags_WhenInspectingAttribute() {
		// Arrange & Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(SetObjectRightsTool)
			.GetMethod(nameof(SetObjectRightsTool.SetObjectRights))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(SetObjectRightsTool.ToolName, because: "the tool publishes under its canonical kebab-case name");
		attribute.ReadOnly.Should().BeFalse(because: "granting object permissions mutates state");
		attribute.Destructive.Should().BeTrue(because: "changing access rights is destructive");
		attribute.Idempotent.Should().BeTrue(because: "re-granting the same object access is safe to repeat");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks the single args wrapper as schema-required on set-object-rights so an omitted args object fails with a structured error.")]
	public void SetObjectRightsTool_ShouldRequireArgsWrapper_WhenInspectingMethodSignature() {
		// Arrange & Act
		object[] required = typeof(SetObjectRightsTool)
			.GetMethod(nameof(SetObjectRightsTool.SetObjectRights))!
			.GetParameters()[0]
			.GetCustomAttributes(typeof(RequiredAttribute), false);

		// Assert
		required.Should().NotBeEmpty(because: "the args wrapper must be schema-required so an omitted args object fails cleanly");
	}

	[Test]
	[Category("Unit")]
	[Description("Binds the set-object-rights arguments from kebab-case JSON using the real MCP serializer options.")]
	public void SetObjectRightsArgs_ShouldBindKebabCaseFields_WhenDeserializedWithMcpOptions() {
		// Arrange
		JsonSerializerOptions options = Clio.BindingsModule.CreateMcpSerializerOptions();

		// Act
		SetObjectRightsArgs args = JsonSerializer.Deserialize<SetObjectRightsArgs>(
			"""{"environment-name":"sandbox","entity-schema-name":"UsrPortalSpike","grantee":"720b771c-e7a7-4f31-9cfb-52cd21c3739f","operations":"read,edit","revoke":true,"include-connected":true}""",
			options)!;

		// Assert
		args.EnvironmentName.Should().Be("sandbox", because: "the kebab-case environment-name binds");
		args.EntitySchemaName.Should().Be("UsrPortalSpike", because: "the kebab-case entity-schema-name binds");
		args.Grantee.Should().Be("720b771c-e7a7-4f31-9cfb-52cd21c3739f", because: "the grantee id binds");
		args.Operations.Should().Be("read,edit", because: "the operations list binds");
		args.Revoke.Should().BeTrue(because: "the revoke flag binds");
		args.IncludeConnected.Should().BeTrue(because: "the include-connected flag binds");
	}

	[Test]
	[Category("Unit")]
	[Description("Declares get-object-rights as a read-only, non-destructive, idempotent tool under its canonical name.")]
	public void GetObjectRightsTool_ShouldDeclareReadOnlySafetyFlags_WhenInspectingAttribute() {
		// Arrange & Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(GetObjectRightsTool)
			.GetMethod(nameof(GetObjectRightsTool.GetObjectRights))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(GetObjectRightsTool.ToolName, because: "the tool publishes under its canonical kebab-case name");
		attribute.ReadOnly.Should().BeTrue(because: "reading object rights does not mutate state");
		attribute.Destructive.Should().BeFalse(because: "a read does not change access rights");
		attribute.Idempotent.Should().BeTrue(because: "reading the same object rights is safe to repeat");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks the single args wrapper as schema-required on get-object-rights so an omitted args object fails with a structured error.")]
	public void GetObjectRightsTool_ShouldRequireArgsWrapper_WhenInspectingMethodSignature() {
		// Arrange & Act
		object[] required = typeof(GetObjectRightsTool)
			.GetMethod(nameof(GetObjectRightsTool.GetObjectRights))!
			.GetParameters()[0]
			.GetCustomAttributes(typeof(RequiredAttribute), false);

		// Assert
		required.Should().NotBeEmpty(because: "the args wrapper must be schema-required so an omitted args object fails cleanly");
	}

	[Test]
	[Category("Unit")]
	[Description("Binds the get-object-rights arguments from kebab-case JSON using the real MCP serializer options.")]
	public void GetObjectRightsArgs_ShouldBindKebabCaseFields_WhenDeserializedWithMcpOptions() {
		// Arrange
		JsonSerializerOptions options = Clio.BindingsModule.CreateMcpSerializerOptions();

		// Act
		GetObjectRightsArgs args = JsonSerializer.Deserialize<GetObjectRightsArgs>(
			"""{"environment-name":"sandbox","entity-schema-name":"UsrPortalSpike","grantee":"720b771c-e7a7-4f31-9cfb-52cd21c3739f","include-connected":true}""",
			options)!;

		// Assert
		args.EnvironmentName.Should().Be("sandbox", because: "the kebab-case environment-name binds");
		args.EntitySchemaName.Should().Be("UsrPortalSpike", because: "the kebab-case entity-schema-name binds");
		args.Grantee.Should().Be("720b771c-e7a7-4f31-9cfb-52cd21c3739f", because: "the grantee id binds");
		args.IncludeConnected.Should().BeTrue(because: "the include-connected flag binds");
	}
}
