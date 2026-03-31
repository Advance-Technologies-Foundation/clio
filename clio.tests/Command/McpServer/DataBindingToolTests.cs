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
using Clio.Workspaces;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;
using ModelContextProtocol.Server;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public sealed class DataBindingToolTests : BaseClioModuleTests {
	private MockFileSystem _mockFileSystem = null!;
	private string _workspaceRoot = null!;
	private string _packageName = "TestPkg";
	private IApplicationClient _applicationClient = null!;
	private IWorkspacePathBuilder _workspacePathBuilder = null!;

	public override void Setup() {
		base.Setup();
		_workspaceRoot = Path.Combine(Path.GetTempPath(), $"data-binding-tool-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_workspaceRoot);
		Directory.CreateDirectory(Path.Combine(_workspaceRoot, ".clio"));
		_mockFileSystem.AddDirectory(_workspaceRoot);
		_mockFileSystem.AddFile(Path.Combine(_workspaceRoot, ".clio", "workspaceSettings.json"), new MockFileData("{}"));
		_mockFileSystem.AddDirectory(Path.Combine(_workspaceRoot, "packages", _packageName));
		_mockFileSystem.AddFile(Path.Combine(_workspaceRoot, "packages", _packageName, "descriptor.json"), new MockFileData("{}"));
		_mockFileSystem.AddDirectory(Path.Combine(_workspaceRoot, "assets"));
		_mockFileSystem.AddFile(Path.Combine(_workspaceRoot, "assets", "icon.png"), new MockFileData(new byte[] { 1, 2, 3 }));
		_mockFileSystem.AddDirectory(Path.Combine(_workspaceRoot, "packages", _packageName, "Data", "SysSettings"));
		_mockFileSystem.AddFile(Path.Combine(_workspaceRoot, "packages", _packageName, "Data", "SysSettings", "descriptor.json"), new MockFileData("""
		{
		  "Descriptor": {
		    "UId": "c653d44c-9c7c-125d-e269-b9257b353ff9",
		    "Name": "SysSettings",
		    "InstallType": 0,
		    "Schema": {
		      "UId": "27aeadd6-d508-4572-8061-5b55b667c902",
		      "Name": "SysSettings"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "ae0e45ca-c495-4fe7-a39d-3ab7278e1617",
		        "IsForceUpdate": false,
		        "IsKey": true,
		        "ColumnName": "Id",
		        "DataTypeValueUId": "23018567-a13c-4320-8687-fd6f9e3699bd"
		      },
		      {
		        "ColumnUId": "736c30a7-c0ec-4fa9-b034-2552b319b633",
		        "IsForceUpdate": false,
		        "IsKey": false,
		        "ColumnName": "Name",
		        "DataTypeValueUId": "ddb3a1ee-07e8-4d62-b7a9-d0e618b00fbd"
		      }
		    ]
		  }
		}
		"""));
		_mockFileSystem.AddFile(Path.Combine(_workspaceRoot, "packages", _packageName, "Data", "SysSettings", "data.json"), new MockFileData("""
		{
		  "PackageData": [
		    {
		      "Row": [
		        {
		          "SchemaColumnUId": "ae0e45ca-c495-4fe7-a39d-3ab7278e1617",
		          "Value": "4f41bcc2-7ed0-45e8-a1fd-474918966d15"
		        },
		        {
		          "SchemaColumnUId": "736c30a7-c0ec-4fa9-b034-2552b319b633",
		          "Value": "Existing row"
		        }
		      ]
		    }
		  ]
		}
		"""));
	}

	public override void TearDown() {
		base.TearDown();
		_applicationClient.ClearReceivedCalls();
		_workspacePathBuilder.ClearReceivedCalls();
		if (Directory.Exists(_workspaceRoot)) {
			Directory.Delete(_workspaceRoot, recursive: true);
		}
	}

	protected override MockFileSystem CreateFs() {
		_mockFileSystem = new MockFileSystem(new Dictionary<string, MockFileData>(), Path.Combine(Path.GetTempPath(), $"clio-data-binding-tool-fs-{Guid.NewGuid():N}"));
		return _mockFileSystem;
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(callInfo => BuildApplicationClientResponse(
				callInfo.ArgAt<string>(0),
				callInfo.ArgAt<string>(1)));
		_workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		_workspacePathBuilder.RootPath.Returns(_workspaceRoot);
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest)
			.Returns("http://localhost/0/DataService/json/SyncReply/RuntimeEntitySchemaRequest");
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select)
			.Returns("http://localhost/0/DataService/json/SyncReply/SelectQuery");

		containerBuilder.AddTransient(_ => _applicationClient);
		containerBuilder.AddTransient(_ => _workspacePathBuilder);
		containerBuilder.AddTransient(_ => serviceUrlBuilder);
	}

	[Test]
	[Description("Advertises stable MCP tool names for the create/add/remove data-binding MCP surface so tests and callers reuse the production constants.")]
	public void DataBindingTools_Should_Advertise_Stable_Tool_Names() {
		// Arrange

		// Act
		string[] toolNames = [
			CreateDataBindingTool.CreateDataBindingToolName,
			AddDataBindingRowTool.AddDataBindingRowToolName,
			RemoveDataBindingRowTool.RemoveDataBindingRowToolName
		];

		// Assert
		toolNames[0].Should().Be("create-data-binding",
			because: "the MCP tool names should remain stable for prompts, tests, and external callers");
		toolNames[1].Should().Be("add-data-binding-row",
			because: "the MCP tool names should remain stable for prompts, tests, and external callers");
		toolNames[2].Should().Be("remove-data-binding-row",
			because: "the MCP tool names should remain stable for prompts, tests, and external callers");
	}

	[Test]
	[Description("Marks every data-binding MCP method as destructive so MCP clients can enforce safety checks before local file mutations.")]
	[TestCase(nameof(CreateDataBindingTool.CreateDataBinding))]
	[TestCase(nameof(AddDataBindingRowTool.AddDataBindingRow))]
	[TestCase(nameof(RemoveDataBindingRowTool.RemoveDataBindingRow))]
	public void DataBinding_Methods_Should_Be_Marked_As_Destructive(string methodName) {
		// Arrange
		Type toolType = methodName switch {
			nameof(CreateDataBindingTool.CreateDataBinding) => typeof(CreateDataBindingTool),
			nameof(AddDataBindingRowTool.AddDataBindingRow) => typeof(AddDataBindingRowTool),
			nameof(RemoveDataBindingRowTool.RemoveDataBindingRow) => typeof(RemoveDataBindingRowTool),
			var _ => throw new InvalidOperationException($"Unsupported method name: {methodName}")
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
			because: "all data-binding tools create or modify local package files");
	}

	[Test]
	[Description("Rejects relative workspace paths for local data-binding tools before command execution so the MCP contract stays explicit.")]
	public void AddDataBindingRow_Should_Reject_Relative_Workspace_Path() {
		// Arrange
		AddDataBindingRowTool tool = new(
			Container.GetRequiredService<AddDataBindingRowCommand>(),
			Container.GetRequiredService<ILogger>());

		// Act
		Action act = () => tool.AddDataBindingRow(new AddDataBindingRowArgs(_packageName, "SysSettings", @"relative\workspace", """{"Id":"1"}"""));

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("Workspace path must be absolute*");
	}

	[Test]
	[Description("Creates a templated binding through the MCP wrapper without requiring environment-based command resolution and auto-generates the GUID primary key when the initial values omit it.")]
	public void CreateDataBinding_Should_Create_Files_Offline_For_Templated_Schema() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateDataBindingTool tool = new(
			Container.GetRequiredService<CreateDataBindingCommand>(),
			Container.GetRequiredService<ILogger>(),
			commandResolver,
			Container.GetRequiredService<IDataBindingTemplateCatalog>());

		// Act
		CommandExecutionResult result = tool.CreateDataBinding(new CreateDataBindingArgs(
			null,
			_packageName,
			"SysSettings",
			_workspaceRoot,
			ValuesJson: """{"Name":"Tool row"}"""));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "templated schemas should be creatable offline through the MCP wrapper");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<CreateDataBindingCommand>(default!);
		_mockFileSystem.File.Exists(Path.Combine(_workspaceRoot, "packages", _packageName, "Data", "SysSettings", "data.json")).Should().BeTrue(
			because: "the resolved create-data-binding command should create binding files in the requested workspace");
		string dataJson = _mockFileSystem.File.ReadAllText(Path.Combine(_workspaceRoot, "packages", _packageName, "Data", "SysSettings", "data.json"));
		string? generatedId = null;
		foreach (JsonElement rowValue in JsonDocument.Parse(dataJson).RootElement.GetProperty("PackageData")[0].GetProperty("Row").EnumerateArray()) {
			if (rowValue.GetProperty("SchemaColumnUId").GetString() == "ae0e45ca-c495-4fe7-a39d-3ab7278e1617") {
				generatedId = rowValue.GetProperty("Value").GetString();
				break;
			}
		}
		Guid.TryParse(generatedId, out _).Should().BeTrue(
			because: "create-data-binding should auto-generate the missing GUID primary key before writing data.json");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!, default, default, default);
	}

	[Test]
	[Description("Encodes a local image file when the MCP create-data-binding payload targets an image-content column so callers can pass a file path instead of pre-encoded base64.")]
	public void CreateDataBinding_Should_Encode_Image_File_For_ImageContent_Column() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateDataBindingTool tool = new(
			Container.GetRequiredService<CreateDataBindingCommand>(),
			Container.GetRequiredService<ILogger>(),
			commandResolver,
			Container.GetRequiredService<IDataBindingTemplateCatalog>());

		// Act
		CommandExecutionResult result = tool.CreateDataBinding(new CreateDataBindingArgs(
			null,
			_packageName,
			"SysModule",
			_workspaceRoot,
			ValuesJson: JsonSerializer.Serialize(new Dictionary<string, string> {
				["Code"] = "UsrImageModule",
				["Image16"] = Path.Combine("assets", "icon.png")
			})));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the MCP create-data-binding wrapper should accept a local image file path for image-content columns");
		string dataJson = _mockFileSystem.File.ReadAllText(Path.Combine(_workspaceRoot, "packages", _packageName, "Data", "SysModule", "data.json"));
		dataJson.Should().Contain("\"Value\": \"AQID\"",
			because: "the create-data-binding MCP flow should base64-encode the referenced image file");
	}

	[Test]
	[Description("Preserves explicit displayValue objects for lookup and image-reference columns when the MCP create-data-binding wrapper targets an offline template.")]
	public void CreateDataBinding_Should_Write_DisplayValue_For_Lookup_And_ImageReference_Columns() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateDataBindingTool tool = new(
			Container.GetRequiredService<CreateDataBindingCommand>(),
			Container.GetRequiredService<ILogger>(),
			commandResolver,
			Container.GetRequiredService<IDataBindingTemplateCatalog>());

		// Act
		CommandExecutionResult result = tool.CreateDataBinding(new CreateDataBindingArgs(
			null,
			_packageName,
			"SysModule",
			_workspaceRoot,
			ValuesJson:
				"""{"Code":"UsrModule","FolderMode":{"value":"b659d704-3955-e011-981f-00155d043204","displayValue":"Prompt folder"},"Logo":{"value":"1171d0f0-63eb-4bd1-a50b-001ecbaf0001","displayValue":"Prompt logo"}}"""));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the MCP wrapper should pass the structured display-value payload through to the command unchanged");
		string dataJson = _mockFileSystem.File.ReadAllText(Path.Combine(_workspaceRoot, "packages", _packageName, "Data", "SysModule", "data.json"));
		dataJson.Should().Contain("\"DisplayValue\": \"Prompt folder\"",
			because: "lookup display values should round-trip through the MCP wrapper");
		dataJson.Should().Contain("\"DisplayValue\": \"Prompt logo\"",
			because: "image-reference display values should round-trip through the MCP wrapper");
	}

	[Test]
	[Description("Resolves the environment-aware create-data-binding command for schemas that are not covered by the built-in offline template catalog.")]
	public void CreateDataBinding_Should_Resolve_Command_For_NonTemplated_Schema() {
		// Arrange
		CreateDataBindingCommand resolvedCommand = Container.GetRequiredService<CreateDataBindingCommand>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateDataBindingCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		CreateDataBindingTool tool = new(
			Container.GetRequiredService<CreateDataBindingCommand>(),
			Container.GetRequiredService<ILogger>(),
			commandResolver,
			Container.GetRequiredService<IDataBindingTemplateCatalog>());

		// Act
		CommandExecutionResult result = tool.CreateDataBinding(new CreateDataBindingArgs(
			"dev",
			_packageName,
			"UsrOfflineOnly",
			_workspaceRoot,
			ValuesJson: """{"Id":"4f41bcc2-7ed0-45e8-a1fd-474918966d15","Name":"Tool row"}"""));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "non-templated schemas should still execute through the environment-aware command resolution path");
		commandResolver.Received(1).Resolve<CreateDataBindingCommand>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "dev"));
		_applicationClient.Received(1).ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Adds a row through the MCP wrapper and generates the GUID primary key when the row payload omits it.")]
	public void AddDataBindingRow_Should_Generate_Primary_Key_When_Missing() {
		// Arrange
		AddDataBindingRowTool tool = new(
			Container.GetRequiredService<AddDataBindingRowCommand>(),
			Container.GetRequiredService<ILogger>());

		// Act
		CommandExecutionResult result = tool.AddDataBindingRow(new AddDataBindingRowArgs(
			_packageName,
			"SysSettings",
			_workspaceRoot,
			"""{"Name":"Generated by tool"}"""));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the MCP wrapper should generate a GUID primary key for add-data-binding-row when the payload omits it");
		string dataJson = _mockFileSystem.File.ReadAllText(Path.Combine(_workspaceRoot, "packages", _packageName, "Data", "SysSettings", "data.json"));
		dataJson.Should().Contain("Generated by tool",
			because: "the new row should preserve the supplied business-column payload");
	}

	[Test]
	[Description("Returns a normal command failure envelope when the MCP add-data-binding-row payload omits DisplayValue for a non-null local lookup column.")]
	public void AddDataBindingRow_Should_Fail_When_Lookup_DisplayValue_Is_Missing() {
		// Arrange
		string sysModuleBindingPath = Path.Combine(_workspaceRoot, "packages", _packageName, "Data", "SysModule");
		_mockFileSystem.AddDirectory(sysModuleBindingPath);
		_mockFileSystem.AddFile(Path.Combine(sysModuleBindingPath, "descriptor.json"), new MockFileData("""
		{
		  "Descriptor": {
		    "UId": "0c75996c-164e-af1b-81c9-c0fa2c3ab0ab",
		    "Name": "SysModule",
		    "InstallType": 0,
		    "Schema": {
		      "UId": "2b2ed767-0b4b-4a7b-9de2-d48e14a2c0c5",
		      "Name": "SysModule"
		    },
		    "Columns": [
		      {
		        "ColumnUId": "ae0e45ca-c495-4fe7-a39d-3ab7278e1617",
		        "IsForceUpdate": false,
		        "IsKey": true,
		        "ColumnName": "Id",
		        "DataTypeValueUId": "23018567-a13c-4320-8687-fd6f9e3699bd"
		      },
		      {
		        "ColumnUId": "e0c474a3-e4bc-457e-bb67-c1ec1b399f60",
		        "IsForceUpdate": false,
		        "IsKey": false,
		        "ColumnName": "Code",
		        "DataTypeValueUId": "ddb3a1ee-07e8-4d62-b7a9-d0e618b00fbd"
		      },
		      {
		        "ColumnUId": "d3afc924-2d21-4c0e-b2f3-9f8c180221f9",
		        "IsForceUpdate": false,
		        "IsKey": false,
		        "ColumnName": "FolderMode",
		        "DataTypeValueUId": "b295071f-7ea9-4e62-8d1a-919bf3732ff2"
		      }
		    ]
		  }
		}
		"""));
		_mockFileSystem.AddFile(Path.Combine(sysModuleBindingPath, "data.json"), new MockFileData("""
		{
		  "PackageData": []
		}
		"""));
		AddDataBindingRowTool tool = new(
			Container.GetRequiredService<AddDataBindingRowCommand>(),
			Container.GetRequiredService<ILogger>());

		// Act
		CommandExecutionResult result = tool.AddDataBindingRow(new AddDataBindingRowArgs(
			_packageName,
			"SysModule",
			_workspaceRoot,
			"""{"Code":"UsrModule","FolderMode":"b659d704-3955-e011-981f-00155d043204"}"""));

		// Assert
		result.ExitCode.Should().Be(1,
			because: "MCP add-data-binding-row should surface lookup display-value validation as a normal command failure");
	}

	[Test]
	[Description("Prompt guidance for data-binding tools mentions workspace-path, the exact production tool names, and the conditional offline-template behavior for create-data-binding.")]
	public void DataBindingPrompt_Should_Mention_Workspace_Path_And_Tool_Names() {
		// Arrange

		// Act
		string createPrompt = DataBindingPrompt.CreateDataBinding(_packageName, "SysSettings", _workspaceRoot, environmentName: null);
		string addPrompt = DataBindingPrompt.AddDataBindingRow(_packageName, "SysSettings", _workspaceRoot, """{"Id":"1"}""");
		string removePrompt = DataBindingPrompt.RemoveDataBindingRow(_packageName, "SysSettings", _workspaceRoot, "1");

		// Assert
		createPrompt.Should().Contain(CreateDataBindingTool.CreateDataBindingToolName,
			because: "the create prompt should reference the exact production MCP tool name");
		createPrompt.Should().Contain("workspace-path",
			because: "the create prompt should keep the local workspace requirement visible");
		createPrompt.Should().Contain("built-in offline template",
			because: "the create prompt should explain when environment-name can be omitted");
		createPrompt.Should().Contain("image-content",
			because: "the create prompt should explain that image-content columns can accept local file paths");
		createPrompt.Should().Contain("displayValue",
			because: "the create prompt should explain the structured lookup and image-reference payload shape");
		createPrompt.Should().Contain("16-color palette",
			because: "the create prompt should mention the SysModule.IconBackground palette restriction");
		addPrompt.Should().Contain(AddDataBindingRowTool.AddDataBindingRowToolName,
			because: "the add-row prompt should reference the exact production MCP tool name");
		addPrompt.Should().Contain("image-content",
			because: "the add-row prompt should explain that image-content columns can accept local file paths");
		addPrompt.Should().Contain("displayValue",
			because: "the add-row prompt should explain that non-null lookup and image-reference values need display text");
		addPrompt.Should().Contain("16-color palette",
			because: "the add-row prompt should mention the SysModule.IconBackground palette restriction");
		removePrompt.Should().Contain(RemoveDataBindingRowTool.RemoveDataBindingRowToolName,
			because: "the remove-row prompt should reference the exact production MCP tool name");
	}

	private const string CreateDataBindingCommandTests_SchemaResponseJson = """
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

	private const string LookupBindingSchemaResponseJson = """
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
	        },
	        "11111111-1111-1111-1111-111111111111": {
	          "uId": "11111111-1111-1111-1111-111111111111",
	          "name": "StatusId",
	          "dataValueType": 10,
	          "referenceSchemaName": "UsrStatus"
	        }
	      }
	    },
	    "primaryColumnUId": "ae0e45ca-c495-4fe7-a39d-3ab7278e1617",
	    "uId": "22222222-2222-2222-2222-222222222222",
	    "name": "UsrLookupBinding"
	  },
	  "success": true
	}
	""";

	private const string StatusSchemaResponseJson = """
	{
	  "schema": {
	    "columns": {
	      "Items": {
	        "33333333-3333-3333-3333-333333333333": {
	          "uId": "33333333-3333-3333-3333-333333333333",
	          "name": "Id",
	          "dataValueType": 0
	        },
	        "44444444-4444-4444-4444-444444444444": {
	          "uId": "44444444-4444-4444-4444-444444444444",
	          "name": "Name",
	          "dataValueType": 28
	        }
	      }
	    },
	    "primaryColumnUId": "33333333-3333-3333-3333-333333333333",
	    "primaryDisplayColumnName": "Name",
	    "uId": "55555555-5555-5555-5555-555555555555",
	    "name": "UsrStatus"
	  },
	  "success": true
	}
	""";

	private const string StatusSelectResponseJson = """
	{
	  "rows": [
	    {
	      "Name": "Resolved status"
	    }
	  ]
	}
	""";

	private static string BuildApplicationClientResponse(string url, string requestBody) {
		if (url.Contains("SelectQuery", StringComparison.Ordinal) &&
			requestBody.Contains("\"rootSchemaName\": \"UsrStatus\"", StringComparison.Ordinal)) {
			return StatusSelectResponseJson;
		}

		if (requestBody.Contains("\"Name\":\"UsrLookupBinding\"", StringComparison.Ordinal)) {
			return LookupBindingSchemaResponseJson;
		}

		if (requestBody.Contains("\"Name\":\"UsrStatus\"", StringComparison.Ordinal)) {
			return StatusSchemaResponseJson;
		}

		return CreateDataBindingCommandTests_SchemaResponseJson;
	}
}
