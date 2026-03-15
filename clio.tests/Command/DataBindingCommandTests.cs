using System;
using System.Collections.Generic;
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
	private const string WorkspaceRoot = @"C:\workspace";
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
			[$@"{WorkspaceRoot}\.clio\workspaceSettings.json"] = new("{}"),
			[$@"{WorkspaceRoot}\packages\{PackageName}\descriptor.json"] = new("{}")
		}, WorkspaceRoot);
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(SchemaResponseJson);
		_logger = Substitute.For<ILogger>();
		_workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		_workspacePathBuilder.RootPath.Returns(WorkspaceRoot);

		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest)
			.Returns("http://localhost/0/DataService/json/SyncReply/RuntimeEntitySchemaRequest");

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
		FileSystem.FileExists($@"{WorkspaceRoot}\packages\{PackageName}\Data\{BindingName}\descriptor.json").Should().BeTrue(
			because: "create-data-binding should write the descriptor file");
		FileSystem.FileExists($@"{WorkspaceRoot}\packages\{PackageName}\Data\{BindingName}\data.json").Should().BeTrue(
			because: "create-data-binding should write the package data file");
		FileSystem.FileExists($@"{WorkspaceRoot}\packages\{PackageName}\Data\{BindingName}\filter.json").Should().BeTrue(
			because: "create-data-binding should always create filter.json");
		FileSystem.FileExists($@"{WorkspaceRoot}\packages\{PackageName}\Data\{BindingName}\Localization\data.en-US.json").Should().BeTrue(
			because: "template mode should scaffold the default localization file");

		string descriptorJson = FileSystem.File.ReadAllText($@"{WorkspaceRoot}\packages\{PackageName}\Data\{BindingName}\descriptor.json");
		string dataJson = FileSystem.File.ReadAllText($@"{WorkspaceRoot}\packages\{PackageName}\Data\{BindingName}\data.json");
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
		FileSystem.File.ReadAllText($@"{WorkspaceRoot}\packages\{PackageName}\Data\{BindingName}\filter.json").Should().BeEmpty(
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
		string descriptorJson = FileSystem.File.ReadAllText($@"{WorkspaceRoot}\packages\{PackageName}\Data\{BindingName}\descriptor.json");
		string dataJson = FileSystem.File.ReadAllText($@"{WorkspaceRoot}\packages\{PackageName}\Data\{BindingName}\data.json");
		string localizationJson = FileSystem.File.ReadAllText($@"{WorkspaceRoot}\packages\{PackageName}\Data\{BindingName}\Localization\data.ru-RU.json");
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
}

[TestFixture]
internal sealed class AddDataBindingRowCommandTests : BaseCommandTests<AddDataBindingRowOptions> {
	private const string WorkspaceRoot = @"C:\workspace";
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
			[$@"{WorkspaceRoot}\.clio\workspaceSettings.json"] = new("{}"),
			[$@"{WorkspaceRoot}\packages\{PackageName}\descriptor.json"] = new("{}"),
			[$@"{WorkspaceRoot}\packages\{PackageName}\Data\{BindingName}\descriptor.json"] = new("""
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
			[$@"{WorkspaceRoot}\packages\{PackageName}\Data\{BindingName}\data.json"] = new("""
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
			[$@"{WorkspaceRoot}\packages\{PackageName}\Data\{BindingName}\Localization\data.en-US.json"] = new("""
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
		string dataJson = FileSystem.File.ReadAllText($@"{WorkspaceRoot}\packages\{PackageName}\Data\{BindingName}\data.json");
		string localizationJson = FileSystem.File.ReadAllText($@"{WorkspaceRoot}\packages\{PackageName}\Data\{BindingName}\Localization\data.en-US.json");
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
		string dataJson = FileSystem.File.ReadAllText($@"{WorkspaceRoot}\packages\{PackageName}\Data\{BindingName}\data.json");
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
		string dataJson = FileSystem.File.ReadAllText($@"{WorkspaceRoot}\packages\{PackageName}\Data\{BindingName}\data.json");
		dataJson.Should().Contain("Null key row",
			because: "the row payload should still be written after the generated key is injected");
		dataJson.Should().NotContain("\"Value\": null",
			because: "the primary key should not be serialized as a null value");
	}
}

[TestFixture]
internal sealed class RemoveDataBindingRowCommandTests : BaseCommandTests<RemoveDataBindingRowOptions> {
	private const string WorkspaceRoot = @"C:\workspace";
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
			[$@"{WorkspaceRoot}\.clio\workspaceSettings.json"] = new("{}"),
			[$@"{WorkspaceRoot}\packages\{PackageName}\descriptor.json"] = new("{}"),
			[$@"{WorkspaceRoot}\packages\{PackageName}\Data\{BindingName}\descriptor.json"] = new("""
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
			[$@"{WorkspaceRoot}\packages\{PackageName}\Data\{BindingName}\data.json"] = new("""
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
			[$@"{WorkspaceRoot}\packages\{PackageName}\Data\{BindingName}\Localization\data.en-US.json"] = new("""
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
		string dataJson = FileSystem.File.ReadAllText($@"{WorkspaceRoot}\packages\{PackageName}\Data\{BindingName}\data.json");
		string localizationJson = FileSystem.File.ReadAllText($@"{WorkspaceRoot}\packages\{PackageName}\Data\{BindingName}\Localization\data.en-US.json");
		dataJson.Should().NotContain("4f41bcc2-7ed0-45e8-a1fd-474918966d15",
			because: "the main data file should no longer contain the removed primary-key row");
		localizationJson.Should().NotContain("Old name",
			because: "localized rows for the removed primary key should be deleted too");
	}
}
