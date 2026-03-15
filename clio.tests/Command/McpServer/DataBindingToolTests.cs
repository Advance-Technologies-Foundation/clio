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
		_mockFileSystem = new MockFileSystem(new Dictionary<string, MockFileData>(), @"C:\");
		return _mockFileSystem;
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(CreateDataBindingCommandTests_SchemaResponseJson);
		_workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		_workspacePathBuilder.RootPath.Returns(_workspaceRoot);
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest)
			.Returns("http://localhost/0/DataService/json/SyncReply/RuntimeEntitySchemaRequest");

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
	[Description("Creates a binding through the MCP wrapper by resolving the environment-aware command and auto-generating the GUID primary key when the initial values omit it.")]
	public void CreateDataBinding_Should_Create_Files_Through_Resolved_Command() {
		// Arrange
		CreateDataBindingCommand resolvedCommand = Container.GetRequiredService<CreateDataBindingCommand>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateDataBindingCommand>(Arg.Any<EnvironmentOptions>()).Returns(resolvedCommand);
		CreateDataBindingTool tool = new(
			Container.GetRequiredService<CreateDataBindingCommand>(),
			Container.GetRequiredService<ILogger>(),
			commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateDataBinding(new CreateDataBindingArgs(
			"dev",
			_packageName,
			"SysSettings",
			_workspaceRoot,
			ValuesJson: """{"Name":"Tool row"}"""));

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the MCP wrapper should pass valid arguments through to the create-data-binding command");
		commandResolver.Received(1).Resolve<CreateDataBindingCommand>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "dev"));
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
	[Description("Prompt guidance for data-binding tools mentions workspace-path and the exact production tool names so agents use the right MCP endpoints.")]
	public void DataBindingPrompt_Should_Mention_Workspace_Path_And_Tool_Names() {
		// Arrange

		// Act
		string createPrompt = DataBindingPrompt.CreateDataBinding("dev", _packageName, "SysSettings", _workspaceRoot);
		string addPrompt = DataBindingPrompt.AddDataBindingRow(_packageName, "SysSettings", _workspaceRoot, """{"Id":"1"}""");
		string removePrompt = DataBindingPrompt.RemoveDataBindingRow(_packageName, "SysSettings", _workspaceRoot, "1");

		// Assert
		createPrompt.Should().Contain(CreateDataBindingTool.CreateDataBindingToolName,
			because: "the create prompt should reference the exact production MCP tool name");
		createPrompt.Should().Contain("workspace-path",
			because: "the create prompt should keep the local workspace requirement visible");
		addPrompt.Should().Contain(AddDataBindingRowTool.AddDataBindingRowToolName,
			because: "the add-row prompt should reference the exact production MCP tool name");
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
}
