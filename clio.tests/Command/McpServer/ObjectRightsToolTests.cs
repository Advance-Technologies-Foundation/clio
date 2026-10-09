using System;
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
	[Description("The set-object-rights description tells the agent that the object is named by its code, that a title is refused, and to name the object by code and title before a write: the contract an agent reads even when it skips the guidance.")]
	public void SetObjectRightsTool_ShouldTellTheAgentTheObjectIsNamedByItsCode_WhenInspectingDescription() {
		// Arrange & Act
		string description = DescriptionOf(typeof(SetObjectRightsTool), nameof(SetObjectRightsTool.SetObjectRights));

		// Assert
		description.Should().Contain("entity-schema-name is the object's CODE, not its title",
			because: "the developer's word can be another object's title");
		description.Should().Contain("a title is refused with the code it belongs to",
			because: "the agent learns what a title gets it before it sends one");
		description.Should().Contain("before a write, name the object to the developer by both",
			because: "the developer is the one who can tell which object was meant");
	}

	[Test]
	[Category("Unit")]
	[Description("The get-object-rights description says that a title is read when no object has the code, and that the output shows the title next to the code.")]
	public void GetObjectRightsTool_ShouldTellTheAgentHowATitleIsRead_WhenInspectingDescription() {
		// Arrange & Act
		string description = DescriptionOf(typeof(GetObjectRightsTool), nameof(GetObjectRightsTool.GetObjectRights));

		// Assert
		description.Should().Contain("when no object has that code, the object with that title is read",
			because: "the code always wins, and a title is read only when no object has the code");
		description.Should().Contain("each object is shown by its title next to its code",
			because: "the agent can relay which object it read");
	}

	private static string DescriptionOf(Type toolType, string methodName) =>
		((System.ComponentModel.DescriptionAttribute)toolType.GetMethod(methodName)!
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Single()).Description;

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
			"""{"environment-name":"sandbox","entity-schema-name":"UsrPortalSpike","grantee":"720b771c-e7a7-4f31-9cfb-52cd21c3739f","operations":"read,edit","revoke":true,"disable-operation-permissions":true,"enable-operation-permissions":false,"preview":true}""",
			options)!;

		// Assert
		args.EnvironmentName.Should().Be("sandbox", because: "the kebab-case environment-name binds");
		args.EntitySchemaName.Should().Be("UsrPortalSpike", because: "the kebab-case entity-schema-name binds");
		args.Grantee.Should().Be("720b771c-e7a7-4f31-9cfb-52cd21c3739f", because: "the grantee id binds");
		args.Operations.Should().Be("read,edit", because: "the operations list binds");
		args.Revoke.Should().BeTrue(because: "the revoke flag binds");
		args.DisableOperationPermissions.Should().BeTrue(because: "the disable flag binds");
		args.EnableOperationPermissions.Should().BeFalse(because: "the enable flag binds");
		args.Preview.Should().BeTrue(because: "the dry-run flag binds");
		args.ExtensionData.Should().BeNullOrEmpty(because: "every key is a known argument");
	}

	[TestCase("""{"environment-name":"dev","entity-schema-name":"UsrFoo","grantee":"720b771c-e7a7-4f31-9cfb-52cd21c3739f"}""",
		null, TestName = "SetObjectRightsArgs_ShouldBindNullOperations_WhenTheKeyIsMissing")]
	[TestCase("""{"environment-name":"dev","entity-schema-name":"UsrFoo","grantee":"720b771c-e7a7-4f31-9cfb-52cd21c3739f","Operations":"read"}""",
		"read", TestName = "SetObjectRightsArgs_ShouldBindOperations_WhenTheKeyIsCapitalized")]
	[Category("Unit")]
	[Description("operations binds exactly what the caller sent with the real MCP serializer options: a missing key binds null, which the command refuses, and a differently cased key is the same argument, not an unknown one.")]
	public void SetObjectRightsArgs_ShouldBindOperationsExactly_WhenDeserializedWithMcpOptions(string json, string expected) {
		// Arrange
		JsonSerializerOptions options = Clio.BindingsModule.CreateMcpSerializerOptions();

		// Act
		SetObjectRightsArgs args = JsonSerializer.Deserialize<SetObjectRightsArgs>(json, options)!;

		// Assert
		args.Operations.Should().Be(expected, because: "no default operation is added on the way in");
		args.ExtensionData.Should().BeNullOrEmpty(because: "a differently cased known key is not an unknown argument");
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

	[Test]
	[Category("Unit")]
	[Description("An MCP set-object-rights call fits the worker budget (120 s): one attempt of at most 25 s per request, a shared 100 s limit — five sequential requests at most — and a confirmed write unless it is a preview.")]
	public void SetObjectRightsTool_ShouldBoundEveryRequestByTheCallBudget_WhenBuildingOptions() {
		// Arrange
		SetObjectRightsArgs args = new("sandbox", "UsrFoo", "720b771c-e7a7-4f31-9cfb-52cd21c3739f", "read", Revoke: true);

		// Act
		Clio.Command.ObjectRights.SetObjectRightsOptions options = SetObjectRightsTool.BuildOptions(args);

		// Assert
		options.TimeOut.Should().Be(25_000, because: "one request may take at most 25 s");
		options.MaxAttempts.Should().Be(1, because: "a retry would multiply the wait inside the worker budget");
		options.CallBudget.Should().Be(TimeSpan.FromSeconds(100),
			because: "all requests share a limit below the 120 s worker kill, so the result is reported before it");
		(options.CallBudget!.Value.TotalMilliseconds).Should().BeGreaterThanOrEqualTo(4.0 * options.TimeOut,
			because: "the budget leaves room for the reads before the save and for the save plus its read-back");
		options.Confirm.Should().BeTrue(because: "the host's approval of the call is the confirmation");
		options.Revoke.Should().BeTrue(because: "the arguments map one to one");
	}

	[Test]
	[Category("Unit")]
	[Description("An MCP get-object-rights call fits the read deadline (120 s): one attempt of at most 30 s per request and a shared 90 s limit.")]
	public void GetObjectRightsTool_ShouldBoundEveryRequestByTheReadBudget_WhenBuildingOptions() {
		// Arrange
		GetObjectRightsArgs args = new("sandbox", "UsrFoo", IncludeConnected: true);

		// Act
		Clio.Command.ObjectRights.GetObjectRightsOptions options = GetObjectRightsTool.BuildOptions(args);

		// Assert
		options.TimeOut.Should().Be(30_000, because: "one request may take at most 30 s");
		options.MaxAttempts.Should().Be(1, because: "a retry would multiply the wait inside the read deadline");
		options.ReadBudget.Should().Be(TimeSpan.FromSeconds(90), because: "the listing stops before the 120 s deadline");
		options.IncludeConnected.Should().BeTrue(because: "the arguments map one to one");
	}
}
