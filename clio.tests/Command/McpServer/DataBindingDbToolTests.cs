using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class DataBindingDbToolTests : BaseClioModuleTests {
	private const string PackageName = "TestPkg";
	private static readonly string WorkspaceRoot = Path.Combine(Path.GetTempPath(), $"clio-data-binding-db-tool-{Guid.NewGuid():N}");
	private static readonly Guid PackageUId = Guid.Parse("1d07fd0e-2ca4-4d20-93b4-eb5a795ea03f");
	private static readonly Guid ExistingRowId = Guid.Parse("4f41bcc2-7ed0-45e8-a1fd-474918966d15");

	private IApplicationClient _applicationClient = null!;
	private IApplicationPackageListProvider _packageListProvider = null!;

	public override void Setup() {
		base.Setup();
	}

	public override void TearDown() {
		base.TearDown();
		_applicationClient.ClearReceivedCalls();
		_packageListProvider.ClearReceivedCalls();
	}

	protected override MockFileSystem CreateFs() {
		return new MockFileSystem(new Dictionary<string, MockFileData>(), currentDirectory: WorkspaceRoot);
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(callInfo => BuildApplicationClientResponse(callInfo.ArgAt<string>(0)));
		_packageListProvider = Substitute.For<IApplicationPackageListProvider>();
		_packageListProvider.GetPackages().Returns([
			new PackageInfo(new PackageDescriptor { Name = PackageName, UId = PackageUId },
				string.Empty, Enumerable.Empty<string>())
		]);

		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest)
			.Returns("http://localhost/0/DataService/json/SyncReply/RuntimeEntitySchemaRequest");
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select)
			.Returns("http://localhost/0/DataService/json/SyncReply/SelectQuery");
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Delete)
			.Returns("http://localhost/0/DataService/json/SyncReply/DeleteQuery");
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Insert)
			.Returns("http://localhost/0/DataService/json/SyncReply/InsertQuery");
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SaveSchemaData)
			.Returns("http://localhost/0/ServiceModel/SchemaDataDesignerService.svc/SaveSchema");
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.DeletePackageSchemaData)
			.Returns("http://localhost/0/DataService/json/SyncReply/DeletePackageSchemaDataRequest");
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetBoundSchemaData)
			.Returns("http://localhost/0/ServiceModel/SchemaDataDesignerService.svc/GetBoundSchemaData");

		containerBuilder.AddTransient(_ => _applicationClient);
		containerBuilder.AddTransient(_ => _packageListProvider);
		containerBuilder.AddTransient(_ => serviceUrlBuilder);
	}

	[Test]
	[Description("Advertises stable MCP tool names for the DB-first create/upsert/remove data-binding MCP surface so tests and callers reuse the production constants.")]
	public void DataBindingDbTools_Should_Advertise_Stable_Tool_Names() {
		// Arrange & Act
		string[] toolNames = [
			CreateDataBindingDbTool.CreateDataBindingDbToolName,
			UpsertDataBindingRowDbTool.UpsertDataBindingRowDbToolName,
			RemoveDataBindingRowDbTool.RemoveDataBindingRowDbToolName
		];

		// Assert
		toolNames[0].Should().Be("create-data-binding-db",
			because: "the MCP DB tool name should remain stable for prompts, tests, and external callers");
		toolNames[1].Should().Be("upsert-data-binding-row-db",
			because: "the MCP DB tool name should remain stable for prompts, tests, and external callers");
		toolNames[2].Should().Be("remove-data-binding-row-db",
			because: "the MCP DB tool name should remain stable for prompts, tests, and external callers");
	}

	[Test]
	[Description("Marks every DB-first data-binding MCP method as destructive so MCP clients can enforce safety checks before remote DB mutations.")]
	[TestCase(nameof(CreateDataBindingDbTool.CreateDataBindingDb))]
	[TestCase(nameof(UpsertDataBindingRowDbTool.UpsertDataBindingRowDb))]
	[TestCase(nameof(RemoveDataBindingRowDbTool.RemoveDataBindingRowDb))]
	public void DataBindingDbTools_Should_Be_Marked_As_Destructive(string methodName) {
		// Arrange
		Type toolType = methodName switch {
			nameof(CreateDataBindingDbTool.CreateDataBindingDb) => typeof(CreateDataBindingDbTool),
			nameof(UpsertDataBindingRowDbTool.UpsertDataBindingRowDb) => typeof(UpsertDataBindingRowDbTool),
			nameof(RemoveDataBindingRowDbTool.RemoveDataBindingRowDb) => typeof(RemoveDataBindingRowDbTool),
			_ => throw new InvalidOperationException($"Unsupported method: {methodName}")
		};
		System.Reflection.MethodInfo method = toolType.GetMethod(methodName)!;
		McpServerToolAttribute attribute = method
			.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false)
			.Cast<McpServerToolAttribute>()
			.Single();

		// Act
		bool destructive = attribute.Destructive;

		// Assert
		destructive.Should().BeTrue(
			because: "all DB-first data-binding tools mutate a remote Creatio database");
	}

	[Test]
	[Description("Returns failure result from create-data-binding-db when neither environment-name is provided, matching the file-first tool's environment guard.")]
	public void CreateDataBindingDb_Should_Return_Failure_Without_Environment() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateDataBindingDbCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(Container.GetRequiredService<CreateDataBindingDbCommand>());
		CreateDataBindingDbTool tool = new(
			Container.GetRequiredService<CreateDataBindingDbCommand>(),
			Container.GetRequiredService<ILogger>(),
			commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateDataBindingDb(new CreateDataBindingDbArgs(
			EnvironmentName: string.Empty,
			PackageName: PackageName,
			SchemaName: "SysSettings"));

		// Assert
		result.ExitCode.Should().Be(1,
			because: "create-data-binding-db requires a remote environment and should fail gracefully without one");
	}

	[Test]
	[Description("Calls SchemaDataDesignerService.svc/SaveSchema through the MCP wrapper and resolves the command via the environment-aware command resolver.")]
	public void CreateDataBindingDb_Should_Call_SaveSchema() {
		// Arrange
		CreateDataBindingDbCommand resolvedCommand = Container.GetRequiredService<CreateDataBindingDbCommand>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateDataBindingDbCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		CreateDataBindingDbTool tool = new(
			Container.GetRequiredService<CreateDataBindingDbCommand>(),
			Container.GetRequiredService<ILogger>(),
			commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateDataBindingDb(new CreateDataBindingDbArgs(
			EnvironmentName: "dev",
			PackageName: PackageName,
			SchemaName: "SysSettings",
			RowsJson: """[{"values":{"Name":"MCP row"}}]"""));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "create-data-binding-db should succeed when environment is provided and the schema is resolvable");
		commandResolver.Received(1).Resolve<CreateDataBindingDbCommand>(Arg.Is<EnvironmentOptions>(opts =>
			opts.Environment == "dev"));
		_applicationClient.Received(1).ExecutePostRequest(
			"http://localhost/0/ServiceModel/SchemaDataDesignerService.svc/SaveSchema",
			Arg.Is<string>(body =>
				body.Contains(PackageUId.ToString()) &&
				body.Contains("\"entitySchemaName\":\"SysSettings\"")),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Prompt guidance for DB-first data-binding tools mentions environment-name and restore-workspace for workspace sync.")]
	public void DataBindingDbPrompt_Should_Mention_Key_Concepts() {
		// Arrange & Act
		string createPrompt = DataBindingDbPrompt.CreateDataBindingDb("dev", PackageName, "SysSettings");
		string upsertPrompt = DataBindingDbPrompt.UpsertDataBindingRowDb("dev", PackageName, "SysSettings", """{"Name":"x"}""");
		string removePrompt = DataBindingDbPrompt.RemoveDataBindingRowDb("dev", PackageName, "SysSettings", ExistingRowId.ToString());

		// Assert
		createPrompt.Should().Contain(CreateDataBindingDbTool.CreateDataBindingDbToolName,
			because: "the create prompt should reference the exact production MCP tool name");
		createPrompt.Should().Contain("environment-name",
			because: "the create prompt should highlight the required environment-name argument");
		createPrompt.Should().Contain("restore-workspace",
			because: "the create prompt should guide users to restore-workspace for syncing");
		createPrompt.Should().Contain("sync-schemas",
			because: "the create prompt should advertise sync-schemas as the canonical batched path");
		createPrompt.Should().Contain("explicit fallback",
			because: "the create prompt should frame create-data-binding-db as explicit fallback or standalone work");
		createPrompt.Should().Contain("prefer this tool over dropping to direct SQL commands",
			because: "the prompt should steer standalone lookup seeding back to the supported MCP path");
		createPrompt.Should().Contain("values",
			because: "the create prompt should explain the required row wrapper shape");
		upsertPrompt.Should().Contain(UpsertDataBindingRowDbTool.UpsertDataBindingRowDbToolName,
			because: "the upsert prompt should reference the exact production MCP tool name");
		upsertPrompt.Should().Contain("environment-name",
			because: "the upsert prompt should reference the environment-name argument");
		upsertPrompt.Should().Contain("must already exist",
			because: "the upsert prompt should state the existing-binding precondition");
		upsertPrompt.Should().Contain(CreateDataBindingDbTool.CreateDataBindingDbToolName,
			because: "the upsert prompt should tell callers to create the binding first when needed");
		removePrompt.Should().Contain(RemoveDataBindingRowDbTool.RemoveDataBindingRowDbToolName,
			because: "the remove prompt should reference the exact production MCP tool name");
		removePrompt.Should().Contain("package schema data record",
			because: "the remove prompt should mention the cleanup of orphaned package schema data");
	}

	private static string BuildApplicationClientResponse(string url) {
		if (url.Contains("RuntimeEntitySchemaRequest", StringComparison.Ordinal)) {
			return SchemaResponseJson;
		}

		if (url.Contains("SelectQuery", StringComparison.Ordinal)) {
			return BindingLookupResponseJson;
		}

		if (url.Contains("GetBoundSchemaData", StringComparison.Ordinal)) {
			string itemsJson = JsonSerializer.Serialize(new[] {
				new Dictionary<string, object?> { ["Id"] = ExistingRowId, ["Name"] = "Existing row" }
			});
			return $$"""{"success":true,"items":{{JsonSerializer.Serialize(itemsJson)}}}""";
		}

		return """{"success":true,"rowsAffected":1}""";
	}

	private static string BindingLookupResponseJson => $$"""
		{
		  "success": true,
		  "rows": [
		    {
		      "Id": "{{Guid.NewGuid()}}",
		      "UId": "{{Guid.NewGuid()}}",
		      "Name": "SysSettings",
		      "EntitySchemaName": "SysSettings"
		    }
		  ]
		}
		""";

	private const string SchemaResponseJson = """
		{
		  "schema": {
		    "columns": {
		      "Items": {
		        "ae0e45ca-c495-4fe7-a39d-3ab7278e1617": {
		          "uId": "ae0e45ca-c495-4fe7-a39d-3ab7278e1617",
		          "name": "Id",
		          "dataValueType": 0
		        },
		        "736c30a7-c0ec-4fa9-b034-2552b319b633": {
		          "uId": "736c30a7-c0ec-4fa9-b034-2552b319b633",
		          "name": "Name",
		          "dataValueType": 28
		        }
		      }
		    },
		    "primaryColumnUId": "ae0e45ca-c495-4fe7-a39d-3ab7278e1617",
		    "uId": "27aeadd6-d508-4572-8061-5b55b667c902",
		    "name": "SysSettings"
		  },
		  "success": true
		}
		""";
}
