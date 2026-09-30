using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.ObjectRights;
using Clio.Common;
using Clio.Common.ObjectRights;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Behaviour of the object-rights MCP tools beyond their attributes: how the args map onto the command options,
/// the one-call apply (the host approval is the confirmation) with <c>preview</c> as a dry run, the refusal of
/// unknown or misspelled argument names before any read or write, and redaction of both the success and the
/// failure payloads.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ObjectRightsToolBehaviourTests {

	private const string Grantee = "720b771c-e7a7-4f31-9cfb-52cd21c3739f";
	private static readonly Guid GranteeId = Guid.Parse(Grantee);
	private static readonly Guid AllEmployees = Guid.Parse("a29a3ba5-4b0d-de11-9a51-005056c00008");
	private const string SecretUri = "https://tenant.example/0/ServiceModel/RightManagementService.svc/GetAdministratedObject";

	private ILogger _logger;
	private IObjectRightsWriter _writer;
	private IObjectRightsReader _reader;
	private IConnectedObjectsResolver _connected;
	private IToolCommandResolver _resolver;
	private SetObjectRightsOptions _capturedSet;
	private GetObjectRightsOptions _capturedGet;

	[SetUp]
	public void SetUp() {
		_logger = Substitute.For<ILogger>();
		// BaseTool snapshots the tool logger's captured messages after the run and clears them; the substitute
		// captures what the command writes so the response carries it, as the real logger does.
		List<LogMessage> messages = new();
		_logger.LogMessages.Returns(_ => messages.ToList());
		_logger.When(l => l.WriteInfo(Arg.Any<string>())).Do(call => messages.Add(new InfoMessage((string)call[0])));
		_logger.When(l => l.WriteWarning(Arg.Any<string>())).Do(call => messages.Add(new WarningMessage((string)call[0])));
		_logger.When(l => l.WriteError(Arg.Any<string>())).Do(call => messages.Add(new ErrorMessage((string)call[0])));
		_logger.When(l => l.ClearMessages()).Do(_ => messages.Clear());
		_writer = Substitute.For<IObjectRightsWriter>();
		// null is a successful save (a substitute would otherwise return an empty string, which is an error).
		_writer.Save(Arg.Any<ObjectRightsSnapshot>(), Arg.Any<ObjectRightsState>(), Arg.Any<CreatioRequestOptions>())
			.Returns(ObjectRightsSaveResult.Saved);
		_reader = Substitute.For<IObjectRightsReader>();
		_connected = Substitute.For<IConnectedObjectsResolver>();
		_connected.Resolve(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<int?>())
			.Returns(callInfo => new ConnectedObjectsResolution(new[] { (string)callInfo[0] }, Array.Empty<string>()));
		// The object before the change, then the object as the save left it (the read-back).
		ObjectRightsInfo before = new(true, "UsrFoo", "UsrFoo", true,
			new[] { new RoleOperationRights(AllEmployees, "All employees", 0, true, true, true, true) });
		ObjectRightsInfo after = before with {
			Roles = before.Roles.Append(new RoleOperationRights(GranteeId, "Grantee", 1, true, true, true, false)).ToArray()
		};
		_reader.GetObjectRights(Arg.Any<string>(), Arg.Any<CreatioRequestOptions>()).Returns(before, after);
		_resolver = Substitute.For<IToolCommandResolver>();
		_capturedSet = null;
		_capturedGet = null;
		_resolver.Resolve<SetObjectRightsCommand>(Arg.Do<EnvironmentOptions>(o => _capturedSet = (SetObjectRightsOptions)o))
			.Returns(_ => SetCommand());
		_resolver.Resolve<GetObjectRightsCommand>(Arg.Do<EnvironmentOptions>(o => _capturedGet = (GetObjectRightsOptions)o))
			.Returns(_ => new GetObjectRightsCommand(_reader, _connected, _logger));
	}

	private SetObjectRightsCommand SetCommand() =>
		new(_reader, _writer, new ObjectRightsPlanner(), Granted(), new ObjectRightsReadBackVerifier(),
			Substitute.For<IInteractiveConsole>(), _logger);

	private SetObjectRightsTool SetTool() => new(SetCommand(), _logger, _resolver);

	private GetObjectRightsTool GetTool() => new(new GetObjectRightsCommand(_reader, _connected, _logger), _logger, _resolver);

	private static IGranteeLookup Granted() {
		IGranteeLookup lookup = Substitute.For<IGranteeLookup>();
		lookup.ResolveGranteeName(Arg.Any<Guid>(), Arg.Any<CreatioRequestOptions>()).Returns("Grantee");
		return lookup;
	}

	private static T Bind<T>(string json) =>
		JsonSerializer.Deserialize<T>(json, Clio.BindingsModule.CreateMcpSerializerOptions())!;

	private void NothingSaved() => _writer.DidNotReceiveWithAnyArgs().Save(default, default, default);

	[TestCase("revok")]
	[TestCase("operation")]
	[TestCase("disable-operation-permission")]
	[TestCase("include-connected")]
	[TestCase("confirmation-code")]
	[TestCase("confirm")]
	[TestCase("allow-security-object")]
	[Description("A misspelled or retired set-object-rights argument is refused before any read or write — a typo is never dropped by the serializer and turned into the opposite change, and a caller still on the old contract (include-connected, confirmation-code, confirm, allow-security-object) is told so instead of being half-understood.")]
	public void SetObjectRights_ShouldRefuseBeforeAnyReadOrWrite_WhenAnArgumentIsUnknown(string unknown) {
		// Arrange
		SetObjectRightsArgs args = Bind<SetObjectRightsArgs>(
			$$"""{"environment-name":"dev","entity-schema-name":"UsrFoo","grantee":"{{Grantee}}","operations":"read","{{unknown}}":true}""");

		// Act
		ObjectRightsToolResponse response = SetTool().SetObjectRights(args);

		// Assert
		response.Success.Should().BeFalse(because: "an unknown argument name must be refused, not silently dropped");
		response.Error.Should().Contain(unknown, because: "the refusal names the offending key");
		_resolver.DidNotReceive().Resolve<SetObjectRightsCommand>(Arg.Any<EnvironmentOptions>());
		_reader.DidNotReceiveWithAnyArgs().GetObjectRights(default, default);
		NothingSaved();
	}

	[Test]
	[Description("A misspelled get-object-rights argument is refused instead of silently reporting every role.")]
	public void GetObjectRights_ShouldRefuse_WhenAnArgumentIsUnknown() {
		// Arrange
		GetObjectRightsArgs args = Bind<GetObjectRightsArgs>(
			$$"""{"environment-name":"dev","entity-schema-name":"UsrFoo","grantee-id":"{{Grantee}}"}""");

		// Act
		ObjectRightsToolResponse response = GetTool().GetObjectRights(args);

		// Assert
		response.Success.Should().BeFalse(because: "an unknown argument name must be refused");
		response.Error.Should().Contain("grantee-id", because: "the refusal names the offending key");
		_resolver.DidNotReceive().Resolve<GetObjectRightsCommand>(Arg.Any<EnvironmentOptions>());
	}

	[Test]
	[Description("With only the required args, the tool maps to a confirmed, non-revoking grant with every transition flag off: the host's approval of the call is the confirmation.")]
	public void SetObjectRights_ShouldMapToAConfirmedGrant_WhenOnlyTheRequiredArgsArePassed() {
		// Arrange
		SetObjectRightsArgs args = new("dev", "UsrFoo", Grantee, "read,create,edit");

		// Act
		ObjectRightsToolResponse response = SetTool().SetObjectRights(args);

		// Assert
		response.Success.Should().BeTrue(because: $"the grant was saved and read back. Error: {response.Error}");
		_capturedSet.Should().NotBeNull(because: "the command must have been resolved for the call");
		_capturedSet.Environment.Should().Be("dev", because: "environment-name maps onto Environment");
		_capturedSet.EntitySchemaName.Should().Be("UsrFoo", because: "entity-schema-name maps through");
		_capturedSet.Grantee.Should().Be(Grantee, because: "grantee maps through");
		_capturedSet.Operations.Should().Be("read,create,edit", because: "operations maps through");
		_capturedSet.Revoke.Should().BeFalse(because: "an omitted revoke is a grant");
		_capturedSet.EnableOperationPermissions.Should().BeFalse(because: "enabling is an explicit opt-in only");
		_capturedSet.DisableOperationPermissions.Should().BeFalse(because: "disabling is an explicit opt-in only");
		_capturedSet.Preview.Should().BeFalse(because: "a call without preview applies the change");
		_capturedSet.Confirm.Should().BeTrue(because: "MCP cannot prompt; the host approval of the call is the confirmation");
		_writer.ReceivedWithAnyArgs(1).Save(default, default, default);
	}

	[Test]
	[Description("preview=true maps to an unconfirmed dry run: nothing is saved.")]
	public void SetObjectRights_ShouldRunADryRun_WhenPreviewIsTrue() {
		// Arrange
		SetObjectRightsArgs args = new("dev", "UsrFoo", Grantee, "read,create,edit", Preview: true);

		// Act
		ObjectRightsToolResponse response = SetTool().SetObjectRights(args);

		// Assert
		response.Success.Should().BeTrue(because: "a preview of an allowed change is not a failure");
		_capturedSet.Preview.Should().BeTrue(because: "preview maps through");
		_capturedSet.Confirm.Should().BeFalse(because: "a dry run is never confirmed");
		response.Output.Should().Contain("PREVIEW", because: "the dry run says that nothing was changed");
		NothingSaved();
	}

	[Test]
	[Description("Every explicit transition flag maps through: enable on a grant; revoke with disable.")]
	public void SetObjectRights_ShouldMapTransitionFlags_WhenProvided() {
		// Arrange
		SetObjectRightsArgs grant = new("dev", "UsrFoo", Grantee, Operations: "read", EnableOperationPermissions: true);
		SetObjectRightsArgs revoke = new("dev", "UsrFoo", Grantee, Operations: "read", Revoke: true,
			DisableOperationPermissions: true);

		// Act
		SetTool().SetObjectRights(grant);
		SetObjectRightsOptions grantOptions = _capturedSet;
		SetTool().SetObjectRights(revoke);

		// Assert
		grantOptions.EnableOperationPermissions.Should().BeTrue(because: "enable-operation-permissions maps through");
		grantOptions.Operations.Should().Be("read", because: "operations maps through");
		_capturedSet.Revoke.Should().BeTrue(because: "revoke maps through");
		_capturedSet.DisableOperationPermissions.Should().BeTrue(because: "disable-operation-permissions maps through");
	}

	[TestCase(null, false, "--operations is required", TestName = "SetObjectRights_ShouldRefuseAGrant_WhenOperationsAreOmitted")]
	[TestCase(null, true, "--operations is required", TestName = "SetObjectRights_ShouldRefuseARevoke_WhenOperationsAreOmitted")]
	[TestCase("", true, "no operation given", TestName = "SetObjectRights_ShouldRefuseARevoke_WhenOperationsAreEmpty")]
	[Description("A call must name its operations: the approved arguments show what is granted or taken away, and nothing is granted by default.")]
	public void SetObjectRights_ShouldRefuse_WhenTheCallNamesNoOperation(string operations, bool revoke, string expected) {
		// Arrange
		SetObjectRightsArgs args = new("dev", "UsrFoo", Grantee, Operations: operations, Revoke: revoke);

		// Act
		ObjectRightsToolResponse response = SetTool().SetObjectRights(args);

		// Assert
		response.Success.Should().BeFalse(because: "a call with no named operation is refused");
		response.Error.Should().Contain(expected, because: "the refusal names what is missing");
		_reader.DidNotReceiveWithAnyArgs().GetObjectRights(default, default);
		NothingSaved();
	}

	[Test]
	[Description("A refused change (a grant that would turn operation permissions on without the flag) fails the call and saves nothing.")]
	public void SetObjectRights_ShouldFailAndNotSave_WhenPlanIsRefused() {
		// Arrange
		_reader.GetObjectRights(Arg.Any<string>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "UsrFoo", "UsrFoo", false,
				new[] { new RoleOperationRights(AllEmployees, "All employees", 0, true, true, true, true) }));

		// Act
		ObjectRightsToolResponse response = SetTool().SetObjectRights(new SetObjectRightsArgs("dev", "UsrFoo", Grantee, "read"));

		// Assert
		response.Success.Should().BeFalse(because: "turning operation permissions on must be named in the arguments");
		response.Error.Should().Contain("enable-operation-permissions", because: "the refusal names the flag to pass");
		NothingSaved();
	}

	[Test]
	[Description("get-object-rights maps its args onto the command options.")]
	public void GetObjectRights_ShouldMapEveryArgument_WhenAllArePassed() {
		// Arrange
		GetObjectRightsArgs args = new("dev", "UsrFoo", Grantee, IncludeConnected: true);

		// Act
		ObjectRightsToolResponse response = GetTool().GetObjectRights(args);

		// Assert
		response.Success.Should().BeTrue(because: "the read succeeded");
		_capturedGet.Environment.Should().Be("dev", because: "environment-name maps onto Environment");
		_capturedGet.EntitySchemaName.Should().Be("UsrFoo", because: "entity-schema-name maps through");
		_capturedGet.Grantee.Should().Be(Grantee, because: "grantee maps through");
		_capturedGet.IncludeConnected.Should().BeTrue(because: "include-connected maps through");
	}

	[Test]
	[Description("When the command throws, the tool fails and the service URI in the exception text is redacted.")]
	public void SetObjectRights_ShouldFailRedacted_WhenCommandThrows() {
		// Arrange
		_reader.GetObjectRights(Arg.Any<string>(), Arg.Any<CreatioRequestOptions>())
			.Returns(_ => throw new ArgumentException("failed at " + SecretUri));

		// Act
		ObjectRightsToolResponse response = SetTool().SetObjectRights(new SetObjectRightsArgs("dev", "UsrFoo", Grantee, "read"));

		// Assert
		response.Success.Should().BeFalse(because: "a thrown command is a failure");
		response.Error.Should().NotBeNullOrWhiteSpace(because: "the failure carries the exception text, redacted");
		response.Error.Should().NotContain("tenant.example",
			because: "service URIs must not cross the MCP boundary unredacted");
		response.Error.Should().Contain("[redacted-uri]", because: "the URI is replaced, not dropped with the message");
	}

	// ---- ObjectRightsToolResponse redaction ----

	[Test]
	[Description("A successful (exit 0) run maps its output and still redacts embedded service text such as a request URI or an HTML error body.")]
	public void From_ShouldRedactOutput_WhenExitCodeIsZero() {
		// Arrange
		CommandExecutionResult result = new(0, [
			new InfoMessage("  UsrFoo: granted [read] for grantee " + Grantee + "."),
			new WarningMessage("  UsrBar: could not read object rights (" + SecretUri + " <html>Request Error</html>).")
		]);

		// Act
		ObjectRightsToolResponse response = ObjectRightsToolResponse.From(result);

		// Assert
		response.Success.Should().BeTrue(because: "exit code 0 is a success");
		response.Output.Should().Contain("granted [read]", because: "the ordinary output is preserved");
		response.Output.Should().Contain("[redacted-uri]", because: "a request URI in a success-path warning is redacted");
		response.Output.Should().NotContain("tenant.example", because: "the host must not leak on the success path");
	}

	[Test]
	[Description("A failed (exit 1) run maps its messages to a redacted error.")]
	public void From_ShouldRedactError_WhenExitCodeIsNonZero() {
		// Arrange
		CommandExecutionResult result = new(1, [new ErrorMessage("  UsrFoo: save failed at " + SecretUri)]);

		// Act
		ObjectRightsToolResponse response = ObjectRightsToolResponse.From(result);

		// Assert
		response.Success.Should().BeFalse(because: "a non-zero exit code is a failure");
		response.Error.Should().Contain("[redacted-uri]", because: "the error path is redacted");
		response.Output.Should().BeNull(because: "a failure carries its text in error, not output");
	}

	[Test]
	[Description("An exception maps to a redacted failure.")]
	public void FromError_ShouldRedactTheMessage_WhenTheExceptionCarriesAHost() {
		// Arrange
		Exception exception = new InvalidOperationException("boom at " + SecretUri);

		// Act
		ObjectRightsToolResponse response = ObjectRightsToolResponse.FromError(exception);

		// Assert
		response.Success.Should().BeFalse(because: "an exception is a failure");
		response.Error.Should().Contain("[redacted-uri]", because: "exception text is redacted");
		response.Error.Should().NotContain("tenant.example", because: "the host must not leak");
	}
}
