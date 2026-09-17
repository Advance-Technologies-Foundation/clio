namespace Clio.Tests.Command;

using System;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
public sealed class SqlSchemaCreateCommandTests : BaseCommandTests<SqlSchemaCreateOptions> {
	private const string PackageUId = "aa000000-0000-0000-0000-000000000001";
	private IApplicationClient _client;
	private SqlSchemaCreateCommand _command;
	private JObject _saved;
	private string _saveResponse;
	private string _scriptRows;

	protected override void AdditionalRegistrations(IServiceCollection services) {
		_client = Substitute.For<IApplicationClient>();
		services.AddSingleton(_client);
	}

	[SetUp]
	public void ArrangeCommand() {
		_command = Container.GetRequiredService<SqlSchemaCreateCommand>();
		_saved = null;
		_saveResponse = "{\"success\":true}";
		_scriptRows = "[]";
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(call => {
			string url = call.ArgAt<string>(0);
			if (url.EndsWith("GetSystemEnvironmentInfo", StringComparison.Ordinal)) {
				return "{\"success\":true,\"dbEngineType\":\"PostgreSql\"}";
			}
			JObject request = JObject.Parse(call.ArgAt<string>(1));
			string rows = request["rootSchemaName"]?.ToString() == "SysPackage"
				? $"[{{\"UId\":\"{PackageUId}\"}}]" : _scriptRows;
			return $"{{\"success\":true,\"rows\":{rows}}}";
		});
		_client.ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(call => {
			_saved = JObject.Parse(call.ArgAt<string>(1));
			return _saveResponse;
		});
	}

	[TearDown]
	public void ClearCalls() => _client.ClearReceivedCalls();

	[Test]
	[Description("Native SQL creation saves a package script directly with detected engine and default installation phase.")]
	public void TryCreate_ShouldSaveNativeDescriptor_WhenInputIsValid() {
		// Arrange
		SqlSchemaCreateOptions options = new() { SchemaName = "UsrSql", PackageName = "Custom" };
		// Act
		bool result = _command.TryCreate(options, out SqlSchemaCreateResponse response);
		// Assert
		result.Should().BeTrue(because: "the native save accepted the new script");
		Guid.TryParse(response.SchemaUId, out _).Should().BeTrue(because: "creation assigns a stable script identity");
		_saved["package"]["uId"].Value<string>().Should().Be(PackageUId, because: "the descriptor belongs to the requested package");
		_saved["dbEngineType"].Value<int>().Should().Be(2, because: "PostgreSQL must not inherit the UI's MSSql default");
		_saved["installType"].Value<int>().Should().Be(1, because: "the default is after-package");
		_saved["dependOnSqlScripts"].Should().BeOfType<JArray>(because: "native save requires a dependency collection");
		_client.Received(1).ExecuteNonReplayablePostRequest(
			Arg.Is<string>(u => u.EndsWith("SqlScriptSchemaDesignerService.svc/SaveSchema")), Arg.Any<string>());
		_client.DidNotReceive().ExecutePostRequest(Arg.Is<string>(u => u.Contains("CreateNewSchema")), Arg.Any<string>());
	}

	[TestCase(0, 0)]
	[TestCase(1, 2)]
	[TestCase(2, 3)]
	[Description("Explicit engine and installation phase override defaults without an environment-info request.")]
	public void TryCreate_ShouldPreserveExplicitOptions_WhenSupplied(int engine, int phase) {
		// Arrange
		SqlSchemaCreateOptions options = new() { SchemaName = "UsrSql", PackageName = "Custom", DbEngineType = engine, InstallType = phase };
		// Act
		bool result = _command.TryCreate(options, out _);
		// Assert
		result.Should().BeTrue(because: "all native engine and phase values are supported");
		_saved["dbEngineType"].Value<int>().Should().Be(engine, because: "explicit dialect selection is authoritative");
		_saved["installType"].Value<int>().Should().Be(phase, because: "installation must preserve the selected phase");
		_client.DidNotReceive().ExecutePostRequest(Arg.Is<string>(u => u.EndsWith("GetSystemEnvironmentInfo")), Arg.Any<string>());
	}

	[TestCase(null, null)]
	[TestCase("1Bad", "Custom")]
	[TestCase("UsrSql", null)]
	[Description("Invalid names are rejected before a network request.")]
	public void TryCreate_ShouldRejectNames_WhenInvalid(string name, string package) {
		// Arrange
		SqlSchemaCreateOptions options = new() { SchemaName = name, PackageName = package };
		_client.ClearReceivedCalls();
		// Act
		bool result = _command.TryCreate(options, out _);
		// Assert
		result.Should().BeFalse(because: "invalid names cannot be persisted");
		_client.ReceivedCalls().Should().BeEmpty(because: "input validation is local");
	}

	[TestCase(-1, 1)]
	[TestCase(3, 1)]
	[TestCase(2, -1)]
	[TestCase(2, 4)]
	[Description("Out-of-range native enum values fail before networking.")]
	public void TryCreate_ShouldRejectEnums_WhenInvalid(int engine, int phase) {
		// Arrange
		SqlSchemaCreateOptions options = new() { SchemaName = "UsrSql", PackageName = "Custom", DbEngineType = engine, InstallType = phase };
		_client.ClearReceivedCalls();
		// Act
		bool result = _command.TryCreate(options, out _);
		// Assert
		result.Should().BeFalse(because: "undefined native enum values cannot be sent");
		_client.ReceivedCalls().Should().BeEmpty(because: "input validation is local");
	}

	[Test]
	[Description("Unsupported schema captions are not silently reported as persisted package SQL metadata.")]
	public void TryCreate_ShouldRejectCaption_WhenSupplied() {
		// Arrange
		SqlSchemaCreateOptions options = new() { SchemaName = "UsrSql", PackageName = "Custom", Caption = "Caption" };
		// Act
		bool result = _command.TryCreate(options, out SqlSchemaCreateResponse response);
		// Assert
		result.Should().BeFalse(because: "the SQL DTO has no caption member");
		response.Error.Should().Contain("no caption", because: "the caller needs an actionable compatibility message");
		_saved.Should().BeNull(because: "unsupported metadata must not cause a partial create");
	}

	[TestCase("[{\"UId\":\"existing\"}]", "already exists")]
	[TestCase("[{\"UId\":\"one\"},{\"UId\":\"two\"}]", "ambiguous")]
	[Description("Existing or ambiguous names cannot cause an overwrite.")]
	public void TryCreate_ShouldRefuseName_WhenAlreadyPresent(string rows, string expected) {
		// Arrange
		_scriptRows = rows;
		// Act
		bool result = _command.TryCreate(new() { SchemaName = "UsrSql", PackageName = "Custom" }, out SqlSchemaCreateResponse response);
		// Assert
		result.Should().BeFalse(because: "create may only use a free name");
		response.Error.Should().Contain(expected, because: "the reason must distinguish ambiguity from a duplicate");
		_saved.Should().BeNull(because: "a failed preflight must not save");
	}

	[TestCase("")]
	[TestCase("<html>stub-session-token</html>")]
	[Description("Unusable native save responses remain named, sanitized failures when readback finds no script.")]
	public void TryCreate_ShouldReportNamedFailure_WhenSaveCannotBeVerified(string responseBody) {
		// Arrange
		_saveResponse = responseBody;
		// Act
		bool result = _command.TryCreate(new() { SchemaName = "UsrSql", PackageName = "Custom" }, out SqlSchemaCreateResponse response);
		// Assert
		result.Should().BeFalse(because: "readback did not find a saved script");
		response.Error.Should().Contain("SqlScriptSchemaDesignerService SaveSchema", because: "the failed native operation must be identifiable");
		response.Error.Should().NotContain("stub-session-token", because: "response markup may contain credentials");
	}
	[TestCase(true)]
	[TestCase(false)]
	[Description("Unknown save success requires this attempt's generated UId, not an unrelated same-named script.")]
	public void TryCreate_ShouldVerifyIdentity_WhenSaveResponseIsLost(bool sameIdentity) {
		// Arrange
		_saveResponse = string.Empty;
		_client.ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(call => {
			_saved = JObject.Parse(call.ArgAt<string>(1));
			string uid = sameIdentity ? _saved["uId"].Value<string>() : Guid.NewGuid().ToString();
			_scriptRows = new JArray(new JObject { ["UId"] = uid }).ToString();
			return string.Empty;
		});
		// Act
		bool result = _command.TryCreate(new() { SchemaName = "UsrSql", PackageName = "Custom" }, out _);
		// Assert
		result.Should().Be(sameIdentity, because: "only this save's script proves the create committed");
	}

	[TestCase(false)]
	[TestCase(true)]
	[Description("An unanswered lookup aborts preflight or reports unknown readback without asserting absence.")]
	public void TryCreate_ShouldPreserveUncertainty_WhenLookupFails(bool afterSave) {
		// Arrange
		_saveResponse = string.Empty;
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Is<string>(s => s.Contains("VwSysSqlScriptInPackage")))
			.Returns(_ => afterSave && _saved is null ? "{\"success\":true,\"rows\":[]}"
				: "{\"success\":false,\"errorInfo\":{\"message\":\"permission denied\"}}");
		// Act
		bool result = _command.TryCreate(new() { SchemaName = "UsrSql", PackageName = "Custom" }, out SqlSchemaCreateResponse response);
		// Assert
		result.Should().BeFalse(because: "an unanswered query cannot prove absence or success");
		response.Error.Should().Contain("permission denied", because: "the actual read failure must remain visible");
		(_saved is not null).Should().Be(afterSave, because: "failed preflight must not write while failed readback follows the single save");
	}

	[Test]
	[Description("A transport exception at save still triggers identity readback rather than a replay.")]
	public void TryCreate_ShouldReadBack_WhenTransportThrowsAfterSave() {
		// Arrange
		_client.ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(call => {
			_saved = JObject.Parse(call.ArgAt<string>(1));
			_scriptRows = new JArray(new JObject { ["UId"] = _saved["uId"] }).ToString();
			throw new System.Net.Http.HttpRequestException("connection reset");
		});
		// Act
		bool result = _command.TryCreate(new() { SchemaName = "UsrSql", PackageName = "Custom" }, out _);
		// Assert
		result.Should().BeTrue(because: "the readback proves the original save committed");
		_client.Received(1).ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("A second connection failure during readback preserves the uncertain save outcome.")]
	public void TryCreate_ShouldPreserveSaveWarning_WhenSaveAndReadbackThrow() {
		// Arrange
		_client.ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(call => {
			_saved = JObject.Parse(call.ArgAt<string>(1));
			throw new System.Net.Http.HttpRequestException("save connection lost");
		});
		_client.ExecutePostRequest(Arg.Any<string>(), Arg.Is<string>(s => s.Contains("VwSysSqlScriptInPackage")))
			.Returns(_ => _saved is null ? "{\"success\":true,\"rows\":[]}"
				: throw new System.Net.Http.HttpRequestException("read connection lost"));
		// Act
		bool result = _command.TryCreate(new() { SchemaName = "UsrSql", PackageName = "Custom" }, out SqlSchemaCreateResponse response);
		// Assert
		result.Should().BeFalse(because: "neither response proves the saved state");
		response.Error.Should().Contain("SaveSchema transport failed", because: "the initial uncertain write must remain visible");
		response.Error.Should().Contain("exists before retrying", because: "creation may already have committed");
	}

}
