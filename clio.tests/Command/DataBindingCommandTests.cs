using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Clio.Command;
using Clio.Common;
using Clio.Workspaces;
using CommandLine;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
internal sealed class CreateDataBindingCommandTests : BaseCommandTests<CreateDataBindingOptions> {
	private static readonly string WorkspaceRoot = Path.Combine(Path.GetTempPath(), $"clio-create-data-binding-command-{Guid.NewGuid():N}");
	private const string PackageName = "TestPkg";
	private const string BindingName = "SysSettings";

	private CreateDataBindingCommand _command = null!;
	private IApplicationClient _applicationClient = null!;
	private ILogger _logger = null!;
	private IWorkspacePathBuilder _workspacePathBuilder = null!;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<CreateDataBindingCommand>();
	}

	public override void TearDown() {
		base.TearDown();
		_applicationClient.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		_workspacePathBuilder.ClearReceivedCalls();
	}

	protected override MockFileSystem CreateFs() {
		return new MockFileSystem(new Dictionary<string, MockFileData> {
			[WorkspacePath(".clio", "workspaceSettings.json")] = new("{}"),
			[WorkspacePath("packages", PackageName, "descriptor.json")] = new("{}"),
			[WorkspacePath("assets", "icon.png")] = new(new byte[] { 1, 2, 3 }),
			[Path.Combine(Path.GetTempPath(), "outside", "icon.png")] = new(new byte[] { 9, 9, 9 })
		}, WorkspaceRoot);
	}

	private static string WorkspacePath(params string[] segments) {
		string path = WorkspaceRoot;
		foreach (string segment in segments) {
			path = Path.Combine(path, segment);
		}
		return path;
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(callInfo => BuildApplicationClientResponse(
				callInfo.ArgAt<string>(0),
				callInfo.ArgAt<string>(1)));
		_logger = Substitute.For<ILogger>();
		_workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		_workspacePathBuilder.RootPath.Returns(WorkspaceRoot);

		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest)
			.Returns("http://localhost/0/DataService/json/SyncReply/RuntimeEntitySchemaRequest");
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select)
			.Returns("http://localhost/0/DataService/json/SyncReply/SelectQuery");

		containerBuilder.AddTransient(_ => _applicationClient);
		containerBuilder.AddTransient(_ => _logger);
		containerBuilder.AddTransient(_ => _workspacePathBuilder);
		containerBuilder.AddTransient(_ => serviceUrlBuilder);
	}

	[Test]
	[Description("Creates descriptor, data, filter, and default localization files for a built-in templated schema without requiring Creatio access.")]
	public void Execute_Should_Create_Template_Files_From_Offline_Template_When_Values_Are_Not_Provided() {
		// Arrange
		CreateDataBindingOptions options = new() {
			PackageName = PackageName,
			SchemaName = "SysSettings"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "template generation should succeed for a valid workspace package and runtime schema");
		FileSystem.FileExists(WorkspacePath("packages", PackageName, "Data", BindingName, "descriptor.json")).Should().BeTrue(
			because: "create-data-binding should write the descriptor file");
		FileSystem.FileExists(WorkspacePath("packages", PackageName, "Data", BindingName, "data.json")).Should().BeTrue(
			because: "create-data-binding should write the package data file");
		FileSystem.FileExists(WorkspacePath("packages", PackageName, "Data", BindingName, "filter.json")).Should().BeTrue(
			because: "create-data-binding should always create filter.json");
		FileSystem.FileExists(WorkspacePath("packages", PackageName, "Data", BindingName, "Localization", "data.en-US.json")).Should().BeTrue(
			because: "template mode should scaffold the default localization file");

		string descriptorJson = FileSystem.File.ReadAllText(WorkspacePath("packages", PackageName, "Data", BindingName, "descriptor.json"));
		string dataJson = FileSystem.File.ReadAllText(WorkspacePath("packages", PackageName, "Data", BindingName, "data.json"));
		descriptorJson.Should().Contain("\"Name\": \"SysSettings\"",
			because: "the generated descriptor should use the default binding folder name");
		descriptorJson.Should().Contain("\"ColumnName\": \"ReferenceSchemaUId\"",
			because: "template mode should include all columns defined by the built-in template metadata");
		descriptorJson.Should().Contain("\"ColumnName\": \"IsSSPAvailable\"",
			because: "template mode should include all columns defined by the built-in template metadata");
		descriptorJson.Should().Contain("\"UId\": \"27aeadd6-d508-4572-8061-5b55b667c902\"",
			because: "the generated descriptor should use the built-in template schema identity");
		descriptorJson.Should().NotContain("\"ColumnName\": \"CreatedOn\"",
			because: "offline template mode should not depend on runtime-only mock columns");
		dataJson.Should().Contain("\"SchemaColumnUId\": \"ae0e45ca-c495-4fe7-a39d-3ab7278e1617\"",
			because: "template mode should include the primary key column row entry");
		dataJson.Should().Contain("\"Value\": \"\"",
			because: "template mode should create empty placeholder values");
		FileSystem.File.ReadAllText(WorkspacePath("packages", PackageName, "Data", BindingName, "filter.json")).Should().BeEmpty(
			because: "filter.json should be created as an empty file");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!, default, default, default);
	}

	[Test]
	[Description("Creates a binding from explicit values and localizations using the built-in template metadata, auto-generating a GUID primary key when the payload omits it while keeping only the requested columns plus the primary key in the descriptor and data files.")]
	public void Execute_Should_Create_Binding_From_Explicit_Values() {
		// Arrange
		CreateDataBindingOptions options = new() {
			PackageName = PackageName,
			SchemaName = "SysSettings",
			ValuesJson = """{"Code":"UsrTestSetting","Name":"Test setting"}""",
			LocalizationsJson = """{"ru-RU":{"Name":"Тестовая настройка"}}"""
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "explicit row generation should succeed for known columns");
		string descriptorJson = FileSystem.File.ReadAllText(WorkspacePath("packages", PackageName, "Data", BindingName, "descriptor.json"));
		string dataJson = FileSystem.File.ReadAllText(WorkspacePath("packages", PackageName, "Data", BindingName, "data.json"));
		string localizationJson = FileSystem.File.ReadAllText(WorkspacePath("packages", PackageName, "Data", BindingName, "Localization", "data.ru-RU.json"));
		string? generatedId = null;
		foreach (JsonElement rowValue in JsonDocument.Parse(dataJson).RootElement.GetProperty("PackageData")[0].GetProperty("Row").EnumerateArray()) {
			if (rowValue.GetProperty("SchemaColumnUId").GetString() == "ae0e45ca-c495-4fe7-a39d-3ab7278e1617") {
				generatedId = rowValue.GetProperty("Value").GetString();
				break;
			}
		}
		string? localizedName = JsonDocument.Parse(localizationJson)
			.RootElement.GetProperty("PackageData")[0]
			.GetProperty("Row")[1]
			.GetProperty("Value")
			.GetString();

		descriptorJson.Should().Contain("\"ColumnName\": \"Id\"",
			because: "the explicit descriptor should still include the primary key column");
		descriptorJson.Should().Contain("\"ColumnName\": \"Code\"",
			because: "the explicit descriptor should include requested business columns");
		descriptorJson.Should().Contain("\"ColumnName\": \"Name\"",
			because: "the explicit descriptor should include requested business columns");
		descriptorJson.Should().NotContain("\"ColumnName\": \"CreatedOn\"",
			because: "explicit value mode should not silently add unrelated runtime schema columns");
		Guid.TryParse(generatedId, out _).Should().BeTrue(
			because: "create-data-binding should auto-generate a GUID primary key when the values payload omits it");
		dataJson.Should().Contain("UsrTestSetting",
			because: "the row payload should preserve provided values");
		localizedName.Should().Be("Тестовая настройка",
			because: "provided localized values should be written to the culture-specific file");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!, default, default, default);
	}

	[Test]
	[Description("Rejects payload columns that are not present in the built-in template instead of writing a partially invalid binding.")]
	public void Execute_Should_Fail_When_Values_Contain_Unknown_Column() {
		// Arrange
		CreateDataBindingOptions options = new() {
			PackageName = PackageName,
			SchemaName = "SysSettings",
			ValuesJson = """{"Id":"4f41bcc2-7ed0-45e8-a1fd-474918966d15","Missing":"x"}"""
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "unknown runtime schema columns should be rejected before files are written");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("Column 'Missing'")));
	}

	[Test]
	[Description("Creates a SysModule binding from the built-in offline template and preserves the checked-in image data-type identifiers for Image16 and Image20 without calling Creatio.")]
	public void Execute_Should_Create_SysModule_Template_With_Image_DataType_Guids() {
		// Arrange
		CreateDataBindingOptions options = new() {
			PackageName = PackageName,
			SchemaName = "SysModule"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "the built-in SysModule template should allow offline binding creation");
		string descriptorJson = FileSystem.File.ReadAllText(WorkspacePath("packages", PackageName, "Data", "SysModule", "descriptor.json"));
		descriptorJson.Should().Contain("\"ColumnName\": \"Image16\"",
			because: "the SysModule template should include the small image column");
		descriptorJson.Should().Contain("\"ColumnName\": \"Image20\"",
			because: "the SysModule template should include the medium image column");
		descriptorJson.Should().Contain("\"DataTypeValueUId\": \"fa6e6e49-b996-475e-a77e-73904e4c5a88\"",
			because: "Image16 and Image20 must keep the filesystem-compatible image-content data type identifier from the checked-in binding");
		descriptorJson.Should().Contain("\"DataTypeValueUId\": \"b039feb0-ee7c-4884-8aa6-d6d45d84316f\"",
			because: "Logo and Image32 must keep the checked-in image-reference data type identifier from the checked-in binding");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!, default, default, default);
	}

	[Test]
	[Description("Writes the caller-provided DisplayValue for lookup and image-reference columns when create-data-binding receives the structured object payload shape.")]
	public void Execute_Should_Write_Caller_Provided_DisplayValue_For_Lookup_And_ImageReference_Columns() {
		// Arrange
		CreateDataBindingOptions options = new() {
			PackageName = PackageName,
			SchemaName = "SysModule",
			ValuesJson =
				"""{"Code":"UsrModule","FolderMode":{"value":"b659d704-3955-e011-981f-00155d043204","displayValue":"Provided folder mode"},"Logo":{"value":"1171d0f0-63eb-4bd1-a50b-001ecbaf0001","displayValue":"Provided module logo"}}"""
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "structured lookup and image-reference payloads with explicit displayValue should be accepted");
		string dataJson = FileSystem.File.ReadAllText(WorkspacePath("packages", PackageName, "Data", "SysModule", "data.json"));
		dataJson.Should().Contain("\"DisplayValue\": \"Provided folder mode\"",
			because: "lookup columns should preserve the caller-supplied display value");
		dataJson.Should().Contain("\"DisplayValue\": \"Provided module logo\"",
			because: "image-reference columns should preserve the caller-supplied display value");
	}

	[Test]
	[Description("Resolves DisplayValue from Creatio for lookup columns during create-data-binding when the caller supplies only the lookup identifier and runtime access is available.")]
	public void Execute_Should_Resolve_DisplayValue_For_Lookup_Columns_When_Runtime_Access_Is_Available() {
		// Arrange
		CreateDataBindingOptions options = new() {
			Environment = "dev",
			PackageName = PackageName,
			SchemaName = "UsrLookupBinding",
			ValuesJson =
				"""{"Name":"Lookup row","StatusId":"5d4f7d77-286a-4f02-9fa0-4cb4d1c0d111"}"""
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "create-data-binding should backfill display values when the runtime schema and select query are available");
		string dataJson = FileSystem.File.ReadAllText(WorkspacePath("packages", PackageName, "Data", "UsrLookupBinding", "data.json"));
		dataJson.Should().Contain("\"DisplayValue\": \"Resolved status\"",
			because: "lookup rows should serialize the resolved display text alongside the identifier");
	}

	[Test]
	[Description("Rejects non-null lookup payloads without DisplayValue during create-data-binding when no runtime lookup data is available to resolve it.")]
	public void Execute_Should_Fail_When_Lookup_DisplayValue_Is_Missing_Offline() {
		// Arrange
		CreateDataBindingOptions options = new() {
			PackageName = PackageName,
			SchemaName = "SysModule",
			ValuesJson =
				"""{"Code":"UsrModule","FolderMode":"b659d704-3955-e011-981f-00155d043204"}"""
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "offline create-data-binding cannot infer lookup display text from local template metadata alone");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("FolderMode") &&
			message.Contains("requires displayValue")));
	}

	[Test]
	[Description("Base64-encodes a local file path when create-data-binding writes an image-content column instead of requiring the caller to supply an already encoded string.")]
	public void Execute_Should_Encode_Image_File_For_ImageContent_Column() {
		// Arrange
		CreateDataBindingOptions options = new() {
			PackageName = PackageName,
			SchemaName = "SysModule",
			ValuesJson = "{\"Code\":\"UsrImageModule\",\"Image16\":\"" + Path.Combine("assets", "icon.png") + "\"}"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "create-data-binding should accept a local image file path for image-content columns");
		string dataJson = FileSystem.File.ReadAllText(WorkspacePath("packages", PackageName, "Data", "SysModule", "data.json"));
		dataJson.Should().Contain("\"SchemaColumnUId\": \"6d827ba7-a622-47cc-8f11-b40b91c7441a\"",
			because: "the selected image-content column should be written to the row payload");
		dataJson.Should().Contain("\"Value\": \"AQID\"",
			because: "the command should base64-encode the file bytes for image-content values");
	}

	[Test]
	[Description("Normalizes SysModule IconBackground to the predefined palette value when create-data-binding receives an allowed color.")]
	public void Execute_Should_Normalize_Allowed_SysModule_IconBackground_Color() {
		// Arrange
		CreateDataBindingOptions options = new() {
			PackageName = PackageName,
			SchemaName = "SysModule",
			ValuesJson = """{"Code":"UsrModule","IconBackground":"#a6de00"}"""
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "allowed SysModule colors should pass validation during create-data-binding");
		string dataJson = FileSystem.File.ReadAllText(WorkspacePath("packages", PackageName, "Data", "SysModule", "data.json"));
		dataJson.Should().Contain("\"Value\": \"#A6DE00\"",
			because: "allowed SysModule colors should be normalized to the predefined palette value");
	}

	[Test]
	[Description("Rejects SysModule IconBackground values that are not part of the predefined 16-color palette.")]
	public void Execute_Should_Reject_Invalid_SysModule_IconBackground_Color() {
		// Arrange
		CreateDataBindingOptions options = new() {
			PackageName = PackageName,
			SchemaName = "SysModule",
			ValuesJson = """{"Code":"UsrModule","IconBackground":"#123456"}"""
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "create-data-binding should reject SysModule IconBackground colors outside the allowed palette");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("must use one of the predefined colors")));
	}

	[Test]
	[Description("Rejects image-content file paths that point outside the resolved workspace so data-binding cannot read arbitrary local files.")]
	public void Execute_Should_Reject_Image_File_Outside_Workspace() {
		// Arrange
		CreateDataBindingOptions options = new() {
			PackageName = PackageName,
			SchemaName = "SysModule",
			ValuesJson = "{\"Code\":\"UsrImageModule\",\"Image16\":\"" + Path.Combine(Path.GetTempPath(), "outside", "icon.png") + "\"}"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "image-content file input must stay inside the resolved workspace");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("must stay inside the workspace")));
	}

	[Test]
	[Description("Requires --environment or --uri for schemas that are not covered by the built-in offline template catalog.")]
	public void Execute_Should_Fail_Without_Environment_For_NonTemplated_Schema() {
		// Arrange
		CreateDataBindingOptions options = new() {
			PackageName = PackageName,
			SchemaName = "UsrOfflineOnly"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "non-templated schemas still need a runtime schema source from Creatio");
		_logger.Received(1).WriteError("create-data-binding requires --environment or --uri.");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!, default, default, default);
	}

	[Test]
	[Description("Parses the lowercase --environment alias for create-data-binding so the CLI contract matches the documented command plan instead of relying on the legacy inherited long option name.")]
	public void Parse_Should_Map_Lowercase_Environment_Alias() {
		// Arrange
		string[] arguments = [
			"--environment", "dev",
			"--package", PackageName,
			"--schema", "SysSettings"
		];
		CreateDataBindingOptions? parsedOptions = null;

		// Act
		ParserResult<CreateDataBindingOptions> parseResult = Parser.Default.ParseArguments<CreateDataBindingOptions>(arguments)
			.WithParsed(result => parsedOptions = result);

		// Assert
		parseResult.Tag.Should().Be(ParserResultType.Parsed,
			because: "the documented lowercase --environment alias should be accepted by the command-line parser");
		parsedOptions.Should().NotBeNull(
			because: "a successful parse should produce create-data-binding options");
		parsedOptions!.Environment.Should().Be("dev",
			because: "the lowercase alias should populate the inherited environment option used by command execution");
	}

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
	        "e80190a5-03b2-4095-90f7-a193a960adee": {
	          "uId": "e80190a5-03b2-4095-90f7-a193a960adee",
	          "name": "CreatedOn",
	          "dataValueType": 7
	        },
	        "736c30a7-c0ec-4fa9-b034-2552b319b633": {
	          "uId": "736c30a7-c0ec-4fa9-b034-2552b319b633",
	          "name": "Name",
	          "dataValueType": 28
	        },
	        "13aad544-ec30-4e76-a373-f0cff3202e24": {
	          "uId": "13aad544-ec30-4e76-a373-f0cff3202e24",
	          "name": "Code",
	          "dataValueType": 27
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

		return SchemaResponseJson;
	}
}

[TestFixture]
internal sealed class AddDataBindingRowCommandTests : BaseCommandTests<AddDataBindingRowOptions> {
	private static readonly string WorkspaceRoot = Path.Combine(Path.GetTempPath(), $"clio-add-data-binding-row-command-{Guid.NewGuid():N}");
	private const string PackageName = "TestPkg";
	private const string BindingName = "SysSettings";

	private AddDataBindingRowCommand _command = null!;
	private ILogger _logger = null!;
	private IWorkspacePathBuilder _workspacePathBuilder = null!;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<AddDataBindingRowCommand>();
	}

	public override void TearDown() {
		base.TearDown();
		_logger.ClearReceivedCalls();
		_workspacePathBuilder.ClearReceivedCalls();
	}

	protected override MockFileSystem CreateFs() {
		return new MockFileSystem(new Dictionary<string, MockFileData> {
			[WorkspacePath(".clio", "workspaceSettings.json")] = new("{}"),
			[WorkspacePath("packages", PackageName, "descriptor.json")] = new("{}"),
			[WorkspacePath("assets", "icon.png")] = new(new byte[] { 1, 2, 3 }),
			[WorkspacePath("packages", PackageName, "Data", BindingName, "descriptor.json")] = new("""
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
			"""),
			[WorkspacePath("packages", PackageName, "Data", BindingName, "data.json")] = new("""
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
			          "Value": "Old name"
			        }
			      ]
			    }
			  ]
			}
			"""),
			[WorkspacePath("packages", PackageName, "Data", BindingName, "Localization", "data.en-US.json")] = new("""
			{
			  "PackageData": [
			    {
			      "Row": [
			        {
			          "SchemaColumnUId": "ae0e45ca-c495-4fe7-a39d-3ab7278e1617",
			          "ColumnName": "Id",
			          "Value": "4f41bcc2-7ed0-45e8-a1fd-474918966d15"
			        },
			        {
			          "SchemaColumnUId": "736c30a7-c0ec-4fa9-b034-2552b319b633",
			          "ColumnName": "Name",
			          "Value": "Old name"
			        }
			      ]
			    }
			  ]
			}
			""")
			,
			[WorkspacePath("packages", PackageName, "Data", "SysModule", "descriptor.json")] = new("""
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
			        "ColumnUId": "6d827ba7-a622-47cc-8f11-b40b91c7441a",
			        "IsForceUpdate": false,
			        "IsKey": false,
			        "ColumnName": "Image16",
			        "DataTypeValueUId": "fa6e6e49-b996-475e-a77e-73904e4c5a88"
			      },
			      {
			        "ColumnUId": "48ed5be5-6dcd-44ba-6294-a29c8daef880",
			        "IsForceUpdate": false,
			        "IsKey": false,
			        "ColumnName": "IconBackground",
			        "DataTypeValueUId": "325a73b8-0f47-44a0-8412-7606f78003ac"
			      },
			      {
			        "ColumnUId": "d3afc924-2d21-4c0e-b2f3-9f8c180221f9",
			        "IsForceUpdate": false,
			        "IsKey": false,
			        "ColumnName": "FolderMode",
			        "DataTypeValueUId": "b295071f-7ea9-4e62-8d1a-919bf3732ff2"
			      },
			      {
			        "ColumnUId": "380d55b9-487c-429b-9aff-e04101ffc307",
			        "IsForceUpdate": false,
			        "IsKey": false,
			        "ColumnName": "Logo",
			        "DataTypeValueUId": "b039feb0-ee7c-4884-8aa6-d6d45d84316f"
			      }
			    ]
			  }
			}
			"""),
			[WorkspacePath("packages", PackageName, "Data", "SysModule", "data.json")] = new("""
			{
			  "PackageData": []
			}
			""")
		}, WorkspaceRoot);
	}

	private static string WorkspacePath(params string[] segments) {
		string path = WorkspaceRoot;
		foreach (string segment in segments) {
			path = Path.Combine(path, segment);
		}
		return path;
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_logger = Substitute.For<ILogger>();
		_workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		_workspacePathBuilder.RootPath.Returns(WorkspaceRoot);

		containerBuilder.AddTransient(_ => _logger);
		containerBuilder.AddTransient(_ => _workspacePathBuilder);
	}

	[Test]
	[Description("Replaces the existing row that has the same primary-key value instead of appending a duplicate row.")]
	public void Execute_Should_Upsert_Row_By_Primary_Key() {
		// Arrange
		AddDataBindingRowOptions options = new() {
			PackageName = PackageName,
			BindingName = BindingName,
			ValuesJson = """{"Id":"4f41bcc2-7ed0-45e8-a1fd-474918966d15","Name":"New name"}""",
			LocalizationsJson = """{"en-US":{"Name":"Localized name"}}"""
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "add-data-binding-row should replace existing rows when the primary key already exists");
		string dataJson = FileSystem.File.ReadAllText(WorkspacePath("packages", PackageName, "Data", BindingName, "data.json"));
		string localizationJson = FileSystem.File.ReadAllText(WorkspacePath("packages", PackageName, "Data", BindingName, "Localization", "data.en-US.json"));
		dataJson.Should().Contain("New name",
			because: "the existing binding row should be updated with the new payload");
		dataJson.Should().NotContain("Old name",
			because: "upsert should replace the previous row instead of appending a second row");
		localizationJson.Should().Contain("Localized name",
			because: "culture-specific rows should be updated together with the main data row");
	}

	[Test]
	[Description("Generates a GUID primary key for add-data-binding-row when the payload omits it, then appends the new row to the binding data file.")]
	public void Execute_Should_Generate_Primary_Key_When_Adding_Row_Without_Id() {
		// Arrange
		AddDataBindingRowOptions options = new() {
			PackageName = PackageName,
			BindingName = BindingName,
			ValuesJson = """{"Name":"Generated key row"}"""
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "add-data-binding-row should generate a GUID primary key when the descriptor primary key is omitted");
		string dataJson = FileSystem.File.ReadAllText(WorkspacePath("packages", PackageName, "Data", BindingName, "data.json"));
		dataJson.Should().Contain("Generated key row",
			because: "the newly added row should preserve the requested business-column payload");
		int guidCount = 0;
		foreach (JsonElement row in JsonDocument.Parse(dataJson).RootElement.GetProperty("PackageData").EnumerateArray()) {
			foreach (JsonElement rowValue in row.GetProperty("Row").EnumerateArray()) {
				if (rowValue.GetProperty("SchemaColumnUId").GetString() == "ae0e45ca-c495-4fe7-a39d-3ab7278e1617") {
					Guid.TryParse(rowValue.GetProperty("Value").GetString(), out _).Should().BeTrue(
						because: "every row should keep a valid GUID primary key after add-data-binding-row completes");
					guidCount++;
				}
			}
		}
		guidCount.Should().Be(2,
			because: "the command should append one additional keyed row rather than dropping the existing one");
	}

	[Test]
	[Description("Treats a null primary key in add-data-binding-row as missing and generates a GUID instead of writing an empty key.")]
	public void Execute_Should_Generate_Primary_Key_When_Adding_Row_With_Null_Id() {
		// Arrange
		AddDataBindingRowOptions options = new() {
			PackageName = PackageName,
			BindingName = BindingName,
			ValuesJson = """{"Id":null,"Name":"Null key row"}"""
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a null GUID primary key should be treated as missing and regenerated");
		string dataJson = FileSystem.File.ReadAllText(WorkspacePath("packages", PackageName, "Data", BindingName, "data.json"));
		dataJson.Should().Contain("Null key row",
			because: "the row payload should still be written after the generated key is injected");
		dataJson.Should().NotContain("\"Value\": null",
			because: "the primary key should not be serialized as a null value");
	}

	[Test]
	[Description("Base64-encodes a local file path when add-data-binding-row writes an image-content column instead of requiring a pre-encoded string.")]
	public void Execute_Should_Encode_Image_File_For_ImageContent_Column() {
		// Arrange
		AddDataBindingRowOptions options = new() {
			PackageName = PackageName,
			BindingName = "SysModule",
			ValuesJson = "{\"Code\":\"UsrImageModule\",\"Image16\":\"" + Path.Combine("assets", "icon.png") + "\"}"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "add-data-binding-row should accept a local image file path for image-content columns");
		string dataJson = FileSystem.File.ReadAllText(WorkspacePath("packages", PackageName, "Data", "SysModule", "data.json"));
		dataJson.Should().Contain("\"Value\": \"AQID\"",
			because: "the command should base64-encode the file bytes before writing the binding row");
	}

	[Test]
	[Description("Rejects SysModule IconBackground values outside the predefined 16-color palette when add-data-binding-row updates a local binding.")]
	public void Execute_Should_Reject_Invalid_SysModule_IconBackground_Color() {
		// Arrange
		AddDataBindingRowOptions options = new() {
			PackageName = PackageName,
			BindingName = "SysModule",
			ValuesJson = """{"Code":"UsrModule","IconBackground":"#123456"}"""
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "add-data-binding-row should reject SysModule IconBackground colors outside the allowed palette");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("must use one of the predefined colors")));
	}

	[Test]
	[Description("Writes caller-provided DisplayValue for local lookup and image-reference columns when add-data-binding-row receives the structured object payload shape.")]
	public void Execute_Should_Write_Caller_Provided_DisplayValue_For_Local_Lookup_And_ImageReference_Columns() {
		// Arrange
		AddDataBindingRowOptions options = new() {
			PackageName = PackageName,
			BindingName = "SysModule",
			ValuesJson =
				"""{"Code":"UsrModule","FolderMode":{"value":"b659d704-3955-e011-981f-00155d043204","displayValue":"Folder mode display"},"Logo":{"value":"1171d0f0-63eb-4bd1-a50b-001ecbaf0001","displayValue":"Logo display"}}"""
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "local add-data-binding-row should keep explicit display values for lookup and image-reference columns");
		string dataJson = FileSystem.File.ReadAllText(WorkspacePath("packages", PackageName, "Data", "SysModule", "data.json"));
		dataJson.Should().Contain("\"DisplayValue\": \"Folder mode display\"",
			because: "lookup rows should serialize the provided display text");
		dataJson.Should().Contain("\"DisplayValue\": \"Logo display\"",
			because: "image-reference rows should serialize the provided display text");
	}

	[Test]
	[Description("Rejects non-null local lookup payloads without DisplayValue during add-data-binding-row because the command works only from local binding files and cannot resolve display text remotely.")]
	public void Execute_Should_Fail_When_Local_Lookup_DisplayValue_Is_Missing() {
		// Arrange
		AddDataBindingRowOptions options = new() {
			PackageName = PackageName,
			BindingName = "SysModule",
			ValuesJson = """{"Code":"UsrModule","FolderMode":"b659d704-3955-e011-981f-00155d043204"}"""
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "local add-data-binding-row cannot infer lookup display text from the descriptor alone");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("FolderMode") &&
			message.Contains("requires displayValue")));
	}
}

[TestFixture]
internal sealed class RemoveDataBindingRowCommandTests : BaseCommandTests<RemoveDataBindingRowOptions> {
	private static readonly string WorkspaceRoot = Path.Combine(Path.GetTempPath(), $"clio-remove-data-binding-row-command-{Guid.NewGuid():N}");
	private const string PackageName = "TestPkg";
	private const string BindingName = "SysSettings";

	private RemoveDataBindingRowCommand _command = null!;
	private ILogger _logger = null!;
	private IWorkspacePathBuilder _workspacePathBuilder = null!;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<RemoveDataBindingRowCommand>();
	}

	public override void TearDown() {
		base.TearDown();
		_logger.ClearReceivedCalls();
		_workspacePathBuilder.ClearReceivedCalls();
	}

	protected override MockFileSystem CreateFs() {
		return new MockFileSystem(new Dictionary<string, MockFileData> {
			[WorkspacePath(".clio", "workspaceSettings.json")] = new("{}"),
			[WorkspacePath("packages", PackageName, "descriptor.json")] = new("{}"),
			[WorkspacePath("packages", PackageName, "Data", BindingName, "descriptor.json")] = new("""
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
			"""),
			[WorkspacePath("packages", PackageName, "Data", BindingName, "data.json")] = new("""
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
			          "Value": "Old name"
			        }
			      ]
			    }
			  ]
			}
			"""),
			[WorkspacePath("packages", PackageName, "Data", BindingName, "Localization", "data.en-US.json")] = new("""
			{
			  "PackageData": [
			    {
			      "Row": [
			        {
			          "SchemaColumnUId": "ae0e45ca-c495-4fe7-a39d-3ab7278e1617",
			          "ColumnName": "Id",
			          "Value": "4f41bcc2-7ed0-45e8-a1fd-474918966d15"
			        },
			        {
			          "SchemaColumnUId": "736c30a7-c0ec-4fa9-b034-2552b319b633",
			          "ColumnName": "Name",
			          "Value": "Old name"
			        }
			      ]
			    }
			  ]
			}
			""")
		}, WorkspaceRoot);
	}

	private static string WorkspacePath(params string[] segments) {
		string path = WorkspaceRoot;
		foreach (string segment in segments) {
			path = Path.Combine(path, segment);
		}
		return path;
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_logger = Substitute.For<ILogger>();
		_workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		_workspacePathBuilder.RootPath.Returns(WorkspaceRoot);

		containerBuilder.AddTransient(_ => _logger);
		containerBuilder.AddTransient(_ => _workspacePathBuilder);
	}

	[Test]
	[Description("Deletes the matching row from both the main data file and every localization file that carries the same primary key.")]
	public void Execute_Should_Remove_Row_And_Localizations() {
		// Arrange
		RemoveDataBindingRowOptions options = new() {
			PackageName = PackageName,
			BindingName = BindingName,
			KeyValue = "4f41bcc2-7ed0-45e8-a1fd-474918966d15"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "remove-data-binding-row should delete rows that match the supplied primary key");
		string dataJson = FileSystem.File.ReadAllText(WorkspacePath("packages", PackageName, "Data", BindingName, "data.json"));
		string localizationJson = FileSystem.File.ReadAllText(WorkspacePath("packages", PackageName, "Data", BindingName, "Localization", "data.en-US.json"));
		dataJson.Should().NotContain("4f41bcc2-7ed0-45e8-a1fd-474918966d15",
			because: "the main data file should no longer contain the removed primary-key row");
		localizationJson.Should().NotContain("Old name",
			because: "localized rows for the removed primary key should be deleted too");
	}
}
