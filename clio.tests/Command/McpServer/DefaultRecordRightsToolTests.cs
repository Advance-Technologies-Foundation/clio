using System;
using System.Linq;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using Clio.Command.ObjectRights;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// The MCP surface of <c>set-default-record-rights</c> and <c>apply-default-record-rights</c>: safety flags, kebab-case
/// binding with the real serializer options, the options an MCP call runs with (host approval = confirm, one attempt,
/// a call budget), and the refusal of an unknown argument before any read or write.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public class DefaultRecordRightsToolTests {

	private static McpServerToolAttribute ToolAttribute(Type tool, string method) =>
		(McpServerToolAttribute)tool.GetMethod(method)!.GetCustomAttributes(typeof(McpServerToolAttribute), false).Single();

	[Test]
	[Description("set-default-record-rights is destructive and idempotent; apply-default-record-rights is destructive and NOT idempotent (each call starts a run).")]
	public void Tools_ShouldDeclareSafetyFlags() {
		// Act
		McpServerToolAttribute set = ToolAttribute(typeof(SetDefaultRecordRightsTool), nameof(SetDefaultRecordRightsTool.SetDefaultRecordRights));
		McpServerToolAttribute apply = ToolAttribute(typeof(ApplyDefaultRecordRightsTool), nameof(ApplyDefaultRecordRightsTool.ApplyDefaultRecordRights));

		// Assert
		set.Name.Should().Be("set-default-record-rights", because: "the canonical kebab-case name");
		set.Destructive.Should().BeTrue(because: "it changes access rights");
		set.Idempotent.Should().BeTrue(because: "a repeated identical call changes nothing");
		apply.Name.Should().Be("apply-default-record-rights", because: "the canonical kebab-case name");
		apply.Destructive.Should().BeTrue(because: "it rewrites the rights of every record");
		apply.Idempotent.Should().BeFalse(because: "every call starts another run");
	}

	[Test]
	[Description("set-default-record-rights binds every kebab-case argument with the MCP serializer options and maps them to the command: confirm from the host approval, one attempt, the call budget.")]
	public void SetDefaultRecordRights_ShouldBindAndBuildOptions() {
		// Arrange
		SetDefaultRecordRightsArgs args = JsonSerializer.Deserialize<SetDefaultRecordRightsArgs>(
			"""{"environment-name":"dev","entity-schema-name":"UsrFoo","author":"a","grantee":"g","operations":"read","level":"delegated","do-not-apply-for-manager":true,"revoke":false,"enable-record-permissions":true,"disable-record-permissions":false,"preview":false}""",
			Clio.BindingsModule.CreateMcpSerializerOptions())!;

		// Act
		SetDefaultRecordRightsOptions options = SetDefaultRecordRightsTool.BuildOptions(args);

		// Assert
		args.ExtensionData.Should().BeNullOrEmpty(because: "every key is a known argument");
		options.Author.Should().Be("a", because: "author maps");
		options.Grantee.Should().Be("g", because: "grantee maps");
		options.Level.Should().Be("delegated", because: "level maps");
		options.DoNotApplyForManager.Should().BeTrue(because: "the flag maps");
		options.EnableRecordPermissions.Should().BeTrue(because: "the enable flag maps");
		options.Confirm.Should().BeTrue(because: "on MCP the host approval is the confirmation");
		options.MaxAttempts.Should().Be(1, because: "a write is never retried");
		options.CallBudget.Should().Be(SetDefaultRecordRightsTool.McpCallBudget, because: "the call answers before the worker budget");
	}

	[Test]
	[Description("A preview on MCP is never confirmed: it writes nothing.")]
	public void SetDefaultRecordRights_ShouldNotConfirm_WhenPreview() {
		// Act
		SetDefaultRecordRightsOptions options = SetDefaultRecordRightsTool.BuildOptions(
			new SetDefaultRecordRightsArgs("dev", "UsrFoo", Preview: true));

		// Assert
		options.Confirm.Should().BeFalse(because: "--preview and --confirm are exclusive");
		options.Preview.Should().BeTrue(because: "the dry run maps");
	}

	[Test]
	[Description("A misspelled argument on set-default-record-rights is refused before any read or write (the serializer would drop it, and a dropped 'revoke' would grant).")]
	public void SetDefaultRecordRights_ShouldRefuseUnknownArgument() {
		// Arrange
		SetDefaultRecordRightsArgs args = JsonSerializer.Deserialize<SetDefaultRecordRightsArgs>(
			"""{"environment-name":"dev","entity-schema-name":"UsrFoo","author":"a","grantee":"g","operations":"read","revok":true}""",
			Clio.BindingsModule.CreateMcpSerializerOptions())!;
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		SetDefaultRecordRightsTool tool = new(null!, Substitute.For<ILogger>(), resolver);

		// Act
		ObjectRightsToolResponse response = tool.SetDefaultRecordRights(args);

		// Assert
		response.Success.Should().BeFalse(because: "an unknown key is refused");
		response.Error.Should().Contain("revok", because: "the refusal names the unknown key");
		resolver.DidNotReceiveWithAnyArgs().Resolve<SetDefaultRecordRightsCommand>(default!);
	}

	[Test]
	[Description("apply-default-record-rights on MCP waits by default with a 60 s wait inside a 100 s call budget, one attempt, confirmed by the host approval.")]
	public void ApplyDefaultRecordRights_ShouldBuildOptions() {
		// Act
		ApplyDefaultRecordRightsOptions options = ApplyDefaultRecordRightsTool.BuildOptions(
			new ApplyDefaultRecordRightsArgs("dev", "UsrFoo"));

		// Assert
		options.Wait.Should().BeTrue(because: "waiting is the default");
		options.TimeoutSeconds.Should().Be(ApplyDefaultRecordRightsTool.McpDefaultWaitSeconds, because: "the MCP default wait");
		options.CallBudget.Should().Be(ApplyDefaultRecordRightsTool.McpCallBudget, because: "the call answers before the worker budget");
		options.MaxAttempts.Should().Be(1, because: "a launch is never retried");
		options.Confirm.Should().BeTrue(because: "on MCP the host approval is the confirmation");
	}

	[Test]
	[Description("A misspelled argument on apply-default-record-rights is refused before the run is started.")]
	public void ApplyDefaultRecordRights_ShouldRefuseUnknownArgument() {
		// Arrange
		ApplyDefaultRecordRightsArgs args = JsonSerializer.Deserialize<ApplyDefaultRecordRightsArgs>(
			"""{"environment-name":"dev","entity-schema-name":"UsrFoo","timeout":5}""",
			Clio.BindingsModule.CreateMcpSerializerOptions())!;
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		ApplyDefaultRecordRightsTool tool = new(null!, Substitute.For<ILogger>(), resolver);

		// Act
		ObjectRightsToolResponse response = tool.ApplyDefaultRecordRights(args);

		// Assert
		response.Success.Should().BeFalse(because: "an unknown key is refused");
		resolver.DidNotReceiveWithAnyArgs().Resolve<ApplyDefaultRecordRightsCommand>(default!);
	}

	[Test]
	[Description("The dangerous set-default-record-rights arguments — revoke, disable-record-permissions, operations, entity-schema-name — map to the command exactly: a dropped revoke would turn a revoke into a grant.")]
	public void SetDefaultRecordRights_ShouldMapDangerousArguments() {
		// Arrange
		SetDefaultRecordRightsArgs args = JsonSerializer.Deserialize<SetDefaultRecordRightsArgs>(
			"""{"environment-name":"dev","entity-schema-name":"UsrFoo","author":"a","grantee":"g","operations":"read,delete","revoke":true,"disable-record-permissions":true}""",
			Clio.BindingsModule.CreateMcpSerializerOptions())!;

		// Act
		SetDefaultRecordRightsOptions options = SetDefaultRecordRightsTool.BuildOptions(args);

		// Assert
		options.EntitySchemaName.Should().Be("UsrFoo", because: "the object maps");
		options.Operations.Should().Be("read,delete", because: "the operations map verbatim");
		options.Revoke.Should().BeTrue(because: "a revoke must stay a revoke");
		options.DisableRecordPermissions.Should().BeTrue(because: "the disable flag maps");
		options.EnableRecordPermissions.Should().BeFalse(because: "it was not given");
	}

	[Test]
	[Description("apply-default-record-rights maps an explicit wait=false and timeout-seconds.")]
	public void ApplyDefaultRecordRights_ShouldMapExplicitWait() {
		// Arrange
		ApplyDefaultRecordRightsArgs args = JsonSerializer.Deserialize<ApplyDefaultRecordRightsArgs>(
			"""{"environment-name":"dev","entity-schema-name":"UsrFoo","wait":false,"timeout-seconds":5}""",
			Clio.BindingsModule.CreateMcpSerializerOptions())!;

		// Act
		ApplyDefaultRecordRightsOptions options = ApplyDefaultRecordRightsTool.BuildOptions(args);

		// Assert
		options.Wait.Should().BeFalse(because: "wait maps");
		options.TimeoutSeconds.Should().Be(5, because: "timeout-seconds maps");
	}

	[Test]
	[Description("get-object-rights binds the new author argument and maps it to the rule filter.")]
	public void GetObjectRights_ShouldBindAndMapAuthor() {
		// Arrange
		GetObjectRightsArgs args = JsonSerializer.Deserialize<GetObjectRightsArgs>(
			"""{"environment-name":"dev","entity-schema-name":"UsrFoo","author":"a29a3ba5-4b0d-de11-9a51-005056c00008"}""",
			Clio.BindingsModule.CreateMcpSerializerOptions())!;

		// Act
		var options = GetObjectRightsTool.BuildOptions(args);

		// Assert
		args.ExtensionData.Should().BeNullOrEmpty(because: "author is a known argument");
		options.Author.Should().Be("a29a3ba5-4b0d-de11-9a51-005056c00008", because: "the filter maps");
	}

	[Test]
	[Description("The apply tool states one call limit, the one it runs with: McpCallBudget.")]
	public void ApplyDefaultRecordRights_ShouldDescribeItsOwnCallBudget() {
		// Arrange
		string description = ((System.ComponentModel.DescriptionAttribute)typeof(ApplyDefaultRecordRightsTool)
			.GetMethod(nameof(ApplyDefaultRecordRightsTool.ApplyDefaultRecordRights))!
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false).Single()).Description;

		// Act
		string limit = $"{ApplyDefaultRecordRightsTool.McpCallBudget.TotalSeconds:0} s";

		// Assert
		description.Should().Contain($"ends within {limit}", because: "the described limit is the one the call runs with");
		description.Should().NotContain("90 s", because: "a second, different ceiling would contradict it");
	}
}
