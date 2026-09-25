using System;
using System.Collections.Generic;
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
/// Behaviour of the object-rights MCP tools beyond their attributes: how the args map onto the command options
/// (the only guard on the auto-confirmed destructive path), the refusal of unknown/misspelled argument names
/// before any write, and redaction of both the success and the failure payloads.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ObjectRightsToolBehaviourTests {

	private const string Grantee = "720b771c-e7a7-4f31-9cfb-52cd21c3739f";
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
		// BaseTool snapshots the tool logger's captured messages after the run; a bare substitute returns null.
		_logger.LogMessages.Returns(new List<LogMessage>());
		_writer = Substitute.For<IObjectRightsWriter>();
		_reader = Substitute.For<IObjectRightsReader>();
		_connected = Substitute.For<IConnectedObjectsResolver>();
		_connected.Resolve(Arg.Any<string>(), Arg.Any<bool>())
			.Returns(callInfo => new ConnectedObjectsResolution(new[] { (string)callInfo[0] }, Array.Empty<string>()));
		_writer.SetObjectRights(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<ObjectOperation>>(),
			Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CreatioRequestOptions>()).Returns(new ObjectRightsChange(true, true));
		_reader.GetObjectRights(Arg.Any<string>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new ObjectRightsInfo(true, "UsrFoo", "UsrFoo", true, Array.Empty<RoleOperationRights>()));
		_resolver = Substitute.For<IToolCommandResolver>();
		_capturedSet = null;
		_capturedGet = null;
		_resolver.Resolve<SetObjectRightsCommand>(Arg.Do<EnvironmentOptions>(o => _capturedSet = (SetObjectRightsOptions)o))
			.Returns(_ => new SetObjectRightsCommand(_writer, _connected, Substitute.For<IInteractiveConsole>(), _logger));
		_resolver.Resolve<GetObjectRightsCommand>(Arg.Do<EnvironmentOptions>(o => _capturedGet = (GetObjectRightsOptions)o))
			.Returns(_ => new GetObjectRightsCommand(_reader, _connected, _logger));
	}

	private SetObjectRightsTool SetTool() =>
		new(new SetObjectRightsCommand(_writer, _connected, Substitute.For<IInteractiveConsole>(), _logger), _logger, _resolver);

	private GetObjectRightsTool GetTool() =>
		new(new GetObjectRightsCommand(_reader, _connected, _logger), _logger, _resolver);

	private static T Bind<T>(string json) =>
		JsonSerializer.Deserialize<T>(json, Clio.BindingsModule.CreateMcpSerializerOptions())!;

	[TestCase("revok")]
	[TestCase("operation")]
	[TestCase("disable-operation-permission")]
	[Description("A misspelled set-object-rights argument is refused before any write, so a typo can never be dropped by the serializer and turned into the opposite change.")]
	public void SetObjectRights_ShouldRefuseUnknownArgument_BeforeAnyWrite(string misspelled) {
		// Arrange
		SetObjectRightsArgs args = Bind<SetObjectRightsArgs>(
			$$"""{"environment-name":"dev","entity-schema-name":"UsrFoo","grantee":"{{Grantee}}","{{misspelled}}":true}""");

		// Act
		ObjectRightsToolResponse response = SetTool().SetObjectRights(args);

		// Assert
		response.Success.Should().BeFalse(because: "an unknown argument name must be refused, not silently dropped");
		response.Error.Should().Contain(misspelled, because: "the refusal names the offending key");
		_resolver.DidNotReceive().Resolve<SetObjectRightsCommand>(Arg.Any<EnvironmentOptions>());
		_writer.DidNotReceiveWithAnyArgs().SetObjectRights(default, default, default, default, default, default);
	}

	[Test]
	[Description("A misspelled get-object-rights argument is refused instead of silently reporting every role.")]
	public void GetObjectRights_ShouldRefuseUnknownArgument() {
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
	[Description("With only the required args, the tool maps to a non-revoking, non-disabling, auto-confirmed grant with the default operation sets.")]
	public void SetObjectRights_ShouldMapRequiredArgsToSafeDefaults() {
		// Arrange
		SetObjectRightsArgs args = new("dev", "UsrFoo", Grantee);

		// Act
		ObjectRightsToolResponse response = SetTool().SetObjectRights(args);

		// Assert
		response.Success.Should().BeTrue(because: "the grant succeeded");
		_capturedSet.Should().NotBeNull(because: "the command must have been resolved for the call");
		_capturedSet.Environment.Should().Be("dev", because: "environment-name maps onto Environment");
		_capturedSet.EntitySchemaName.Should().Be("UsrFoo", because: "entity-schema-name maps through");
		_capturedSet.Grantee.Should().Be(Grantee, because: "grantee maps through");
		_capturedSet.Revoke.Should().BeFalse(because: "an omitted revoke is a grant");
		_capturedSet.DisableOperationPermissions.Should().BeFalse(because: "disabling is an explicit opt-in only");
		_capturedSet.Operations.Should().BeNull(because: "omitted operations fall to the command's least-privilege default");
		_capturedSet.ConnectedOperations.Should().BeNull(because: "omitted connected-operations fall to read-only");
		_capturedSet.IncludeConnected.Should().BeFalse(because: "fan-out is opt-in");
		_capturedSet.Confirm.Should().BeTrue(because: "on MCP the Destructive flag is the gate, so the tool confirms the apply");
	}

	[Test]
	[Description("revoke without the opt-in never maps to disabling operation permissions.")]
	public void SetObjectRights_ShouldNotDisable_WhenRevokeWithoutOptIn() {
		// Arrange
		SetObjectRightsArgs args = new("dev", "UsrFoo", Grantee, Revoke: true);

		// Act
		SetTool().SetObjectRights(args);

		// Assert
		_capturedSet.Revoke.Should().BeTrue(because: "revoke maps through");
		_capturedSet.DisableOperationPermissions.Should().BeFalse(
			because: "a revoke must never widen access to every internal user as a side effect");
	}

	[Test]
	[Description("revoke with the opt-in maps both flags, and connected-operations maps through.")]
	public void SetObjectRights_ShouldMapOptInAndConnectedOperations_WhenProvided() {
		// Arrange
		SetObjectRightsArgs args = new("dev", "UsrFoo", Grantee, Operations: "read", Revoke: true,
			IncludeConnected: true, ConnectedOperations: "read,edit", DisableOperationPermissions: true);

		// Act
		SetTool().SetObjectRights(args);

		// Assert
		_capturedSet.Revoke.Should().BeTrue(because: "revoke maps through");
		_capturedSet.DisableOperationPermissions.Should().BeTrue(because: "the explicit opt-in maps through");
		_capturedSet.IncludeConnected.Should().BeTrue(because: "include-connected maps through");
		_capturedSet.ConnectedOperations.Should().Be("read,edit", because: "connected-operations maps through");
		_capturedSet.Operations.Should().Be("read", because: "operations maps through");
	}

	[Test]
	[Description("get-object-rights maps its args onto the command options.")]
	public void GetObjectRights_ShouldMapArgs() {
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
		_connected.Resolve(Arg.Any<string>(), Arg.Any<bool>())
			.Returns(_ => throw new InvalidOperationException("failed at " + SecretUri));

		// Act
		ObjectRightsToolResponse response = SetTool().SetObjectRights(new SetObjectRightsArgs("dev", "UsrFoo", Grantee));

		// Assert
		response.Success.Should().BeFalse(because: "a thrown command is a failure");
		(response.Error ?? string.Empty).Should().NotContain("tenant.example",
			because: "service URIs must not cross the MCP boundary unredacted");
	}

	// ---- ObjectRightsToolResponse redaction ----

	[Test]
	[Description("A successful (exit 0) run maps its output and still redacts embedded service text such as a request URI or an HTML error body.")]
	public void From_ShouldRedactOutput_WhenExitCodeIsZero() {
		// Arrange
		CommandExecutionResult result = new(0, [
			new InfoMessage("  UsrFoo: granted [read] for grantee " + Grantee + "."),
			new WarningMessage("  UsrBar: could not read object rights (" + SecretUri + " <html>Request Error</html>) — skipped.")
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
	public void FromError_ShouldRedactExceptionMessage() {
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
