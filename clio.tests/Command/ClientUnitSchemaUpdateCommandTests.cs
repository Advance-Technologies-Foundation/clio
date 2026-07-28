namespace Clio.Tests.Command;

using System;
using System.IO;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

/// <summary>
/// Command-level tests for <see cref="ClientUnitSchemaUpdateCommand"/> exercising the real
/// resolve -> load -> set-body -> save orchestration (not a Fake that overrides TryUpdateSchema). The point is
/// the deterministic top-layer write fix: a multi-layer client-unit schema must resolve to, load, and SAVE the
/// TOP (most-derived) layer's UId — a regression to a DB-order-dependent single-row pick would corrupt a base
/// layer with top-layer content and pass every Fake-based test.
/// </summary>
[TestFixture]
[Property("Module", "Command")]
internal class ClientUnitSchemaUpdateCommandTests : BaseCommandTests<ClientUnitSchemaUpdateOptions> {

	private const string TestBase = "http://test";
	private const string SelectQueryUrl = TestBase + "/DataService/json/SyncReply/SelectQuery";
	private const string GetSchemaUrl = TestBase + "/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema";
	private const string SaveSchemaUrl = TestBase + "/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema";

	private const string BaseUId = "aa000000-0000-0000-0000-0000000000ba";
	private const string TopUId = "aa000000-0000-0000-0000-0000000000t0";

	// A two-layer schema: base (HierarchyLevel 0) and top (HierarchyLevel 2). Ordered base->top, the LAST wins.
	private static string TwoLayerRowsJson =>
		"{\"success\": true, \"rows\": [" +
		"{\"UId\": \"" + BaseUId + "\", \"Name\": \"UsrHelper\", \"PackageName\": \"Base\", \"HierarchyLevel\": 0}," +
		"{\"UId\": \"" + TopUId + "\", \"Name\": \"UsrHelper\", \"PackageName\": \"Custom\", \"HierarchyLevel\": 2}" +
		"]}";

	private static string GetSchemaJson(string uId) =>
		"{\"success\": true, \"schema\": {\"uId\": \"" + uId +
		"\", \"name\": \"UsrHelper\", \"body\": \"define('old');\", \"package\": {\"name\": \"Custom\"}}}";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;
	private ILogger _logger;
	private ClientUnitSchemaUpdateCommand _command;
	private string _getSchemaRequestBody;
	private string _saveRequestBody;
	private string _saveResponseJson;

	public override void Setup() {
		base.Setup();
		_getSchemaRequestBody = null;
		_saveRequestBody = null;
		_saveResponseJson = """{"success": true}""";
		_serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		_serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema").Returns(GetSchemaUrl);
		_serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema").Returns(SaveSchemaUrl);
		_applicationClient.ExecutePostRequest(default, default).ReturnsForAnyArgs(ci => Route(
			ci.ArgAt<string>(0), ci.ArgAt<string>(1)));
		_command = Container.GetRequiredService<ClientUnitSchemaUpdateCommand>();
	}

	public override void TearDown() {
		_applicationClient.ClearReceivedCalls();
		_serviceUrlBuilder.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_applicationClient);
		containerBuilder.AddSingleton(_serviceUrlBuilder);
		containerBuilder.AddSingleton(_logger);
	}

	// Name-aware fake Creatio: routes each POST by URL, capturing the GetSchema/SaveSchema request payloads.
	private string Route(string url, string body) {
		if (url == SelectQueryUrl) {
			return TwoLayerRowsJson;
		}
		if (url == GetSchemaUrl) {
			_getSchemaRequestBody = body;
			return GetSchemaJson(TopUId);
		}
		if (url == SaveSchemaUrl) {
			_saveRequestBody = body;
			return _saveResponseJson;
		}
		return """{"success": false}""";
	}

	[Test]
	[Description("TryUpdateSchema resolves a multi-layer schema to the top layer and loads + saves that same top-layer UId, so a base layer is never overwritten with top-layer content.")]
	public void TryUpdateSchema_ShouldResolveLoadAndSave_TheTopLayerUId_ForMultiLayerSchema() {
		// Arrange
		var options = new ClientUnitSchemaUpdateOptions { SchemaName = "UsrHelper", Body = "define('new');" };

		// Act
		bool result = _command.TryUpdateSchema(options, out ClientUnitSchemaUpdateResponse response);

		// Assert — the load targeted the resolved TOP layer, not the base
		result.Should().BeTrue(because: "a resolvable multi-layer schema updates successfully");
		_getSchemaRequestBody.Should().Contain(TopUId,
			because: "the load must target the deterministic top layer");
		_getSchemaRequestBody.Should().NotContain(BaseUId,
			because: "loading the base layer is exactly the corruption this resolution prevents");

		// Assert — the save wrote the new body back to that same top layer
		_saveRequestBody.Should().NotBeNull(because: "a non-dry-run update must call SaveSchema");
		JObject saved = JObject.Parse(_saveRequestBody);
		saved["uId"]!.ToString().Should().Be(TopUId,
			because: "the save target is the same top-layer UId that was resolved and loaded");
		saved["body"]!.ToString().Should().Be("define('new');",
			because: "the new body is written onto the loaded top-layer schema");
	}

	[Test]
	[Description("TryUpdateSchema in dry-run resolves the schema but never calls SaveSchema.")]
	public void TryUpdateSchema_ShouldNotSave_WhenDryRun() {
		// Arrange
		var options = new ClientUnitSchemaUpdateOptions { SchemaName = "UsrHelper", Body = "define('new');", DryRun = true };

		// Act
		bool result = _command.TryUpdateSchema(options, out ClientUnitSchemaUpdateResponse response);

		// Assert
		result.Should().BeTrue(because: "a dry-run resolves and validates without saving");
		response.DryRun.Should().BeTrue(because: "the response records the dry-run mode");
		_saveRequestBody.Should().BeNull(because: "dry-run must not reach SaveSchema");
	}

	[Test]
	[Description("TryUpdateSchema reads the body from --body-file, which takes precedence over --body.")]
	public void TryUpdateSchema_ShouldPreferBodyFile_OverInlineBody() {
		// Arrange — a real temp file (the command reads it via System.IO.File)
		string bodyFile = Path.Combine(Path.GetTempPath(), $"cus-update-{Guid.NewGuid():N}.js");
		File.WriteAllText(bodyFile, "define('from-file');");
		try {
			var options = new ClientUnitSchemaUpdateOptions {
				SchemaName = "UsrHelper", Body = "define('inline');", BodyFile = bodyFile
			};

			// Act
			bool result = _command.TryUpdateSchema(options, out ClientUnitSchemaUpdateResponse response);

			// Assert
			result.Should().BeTrue(because: "the schema resolves and the file body is saved");
			JObject saved = JObject.Parse(_saveRequestBody);
			saved["body"]!.ToString().Should().Be("define('from-file');",
				because: "body-file content takes precedence over the inline --body");
		}
		finally {
			File.Delete(bodyFile);
		}
	}

	[Test]
	[Description("TryUpdateSchema propagates a resolution failure and never loads or saves when the schema does not exist.")]
	public void TryUpdateSchema_ShouldFail_WhenSchemaNotFound() {
		// Arrange — SelectQuery returns no rows
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns("""{"success": true, "rows": []}""");
		var options = new ClientUnitSchemaUpdateOptions { SchemaName = "UsrMissing", Body = "define('new');" };

		// Act
		bool result = _command.TryUpdateSchema(options, out ClientUnitSchemaUpdateResponse response);

		// Assert
		result.Should().BeFalse(because: "an unresolvable schema cannot be updated");
		response.Error.Should().Contain("UsrMissing", because: "the failure names the schema that could not be resolved");
		_saveRequestBody.Should().BeNull(because: "a resolution failure must short-circuit before any save");
	}

	[Test]
	[Description("TryUpdateSchema surfaces a SaveSchema failure reason instead of reporting success.")]
	public void TryUpdateSchema_ShouldFail_WhenSaveReportsError() {
		// Arrange — SaveSchema reports a failure with a reason
		_saveResponseJson = """{"success": false, "errorInfo": {"message": "package is locked"}}""";
		var options = new ClientUnitSchemaUpdateOptions { SchemaName = "UsrHelper", Body = "define('new');" };

		// Act
		bool result = _command.TryUpdateSchema(options, out ClientUnitSchemaUpdateResponse response);

		// Assert
		result.Should().BeFalse(because: "a save failure must not be reported as success");
		response.Error.Should().Contain("package is locked",
			because: "the designer service's own save-failure reason is carried to the caller");
	}
}
