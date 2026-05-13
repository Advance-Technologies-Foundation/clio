using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
internal sealed class DataBindingDbCommandTests : BaseClioModuleTests {
	private const string PackageName = "TestPkg";
	private static readonly string WorkspaceRoot = Path.Combine(Path.GetTempPath(), $"clio-data-binding-db-command-{Guid.NewGuid():N}");
	private static readonly Guid PackageUId = Guid.Parse("1d07fd0e-2ca4-4d20-93b4-eb5a795ea03f");
	private static readonly Guid ExistingRowId = Guid.Parse("4f41bcc2-7ed0-45e8-a1fd-474918966d15");
	private static readonly Guid ExistingBindingUId = Guid.Parse("c653d44c-9c7c-125d-e269-b9257b353ff9");

	private CreateDataBindingDbCommand _createCommand = null!;
	private UpsertDataBindingRowDbCommand _upsertCommand = null!;
	private RemoveDataBindingRowDbCommand _removeCommand = null!;
	private IApplicationClient _applicationClient = null!;
	private ILogger _logger = null!;
	private IApplicationPackageListProvider _packageListProvider = null!;
	private string _bindingLookupResponseJson = string.Empty;
	private string _boundSchemaDataItemsJson = "[]";
	private string _existingEntityNamesJson = """{"rows":[],"success":true}""";
	private string _schemaResponseJson = string.Empty;

	public override void Setup() {
		base.Setup();
		_createCommand = Container.GetRequiredService<CreateDataBindingDbCommand>();
		_upsertCommand = Container.GetRequiredService<UpsertDataBindingRowDbCommand>();
		_removeCommand = Container.GetRequiredService<RemoveDataBindingRowDbCommand>();
		_bindingLookupResponseJson = BuildBindingLookupResponse("SysSettings");
		_boundSchemaDataItemsJson = JsonSerializer.Serialize(new[] {
			new Dictionary<string, object?> {
				["Id"] = ExistingRowId,
				["Name"] = "Existing row"
			}
		});
		_schemaResponseJson = SchemaResponseJson;
	}

	public override void TearDown() {
		base.TearDown();
		_applicationClient.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		_packageListProvider.ClearReceivedCalls();
	}

	protected override MockFileSystem CreateFs() {
		return new MockFileSystem(new Dictionary<string, MockFileData>(), currentDirectory: WorkspaceRoot);
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(callInfo => BuildApplicationClientResponse(
				callInfo.ArgAt<string>(0),
				callInfo.ArgAt<string>(1)));
		_logger = Substitute.For<ILogger>();
		_packageListProvider = Substitute.For<IApplicationPackageListProvider>();
		_packageListProvider.GetPackages().Returns([
			new PackageInfo(new PackageDescriptor {
				Name = PackageName,
				UId = PackageUId
			}, string.Empty, Enumerable.Empty<string>())
		]);

		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest)
			.Returns("http://localhost/0/DataService/json/SyncReply/RuntimeEntitySchemaRequest");
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select)
			.Returns("http://localhost/0/DataService/json/SyncReply/SelectQuery");
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Update)
			.Returns("http://localhost/0/DataService/json/SyncReply/UpdateQuery");
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
		containerBuilder.AddTransient(_ => _logger);
		containerBuilder.AddTransient(_ => _packageListProvider);
		containerBuilder.AddTransient(_ => serviceUrlBuilder);
	}

	[Test]
	[Description("Fails create-data-binding-db when neither --environment nor --uri is supplied because DB-first binding creation always requires a remote target environment.")]
	public void CreateDataBindingDb_Should_Fail_Without_Environment() {
		// Arrange
		CreateDataBindingDbOptions options = new() {
			PackageName = PackageName,
			SchemaName = "SysSettings",
			RowsJson = """[{"values":{"Name":"Row from db tool"}}]"""
		};

		// Act
		int result = _createCommand.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "DB-first create-data-binding-db cannot execute without a remote environment or uri");
		_logger.Received(1).WriteError("--environment or --uri is required.");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!, default, default, default);
	}

	[Test]
	[Description("Creates a DB-first binding by inserting rows via InsertQuery, then posting SchemaDataDto with boundRecordIds to SaveSchema.")]
	public void CreateDataBindingDb_Should_Save_Remotely() {
		// Arrange
		CreateDataBindingDbOptions options = new() {
			Environment = "dev",
			PackageName = PackageName,
			SchemaName = "SysSettings",
			BindingName = "UsrRemoteBinding",
			RowsJson = """[{"values":{"Name":"Row from db tool"}}]"""
		};

		// Act
		int result = _createCommand.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "DB-first create-data-binding-db should insert rows and save the binding remotely");
		_applicationClient.Received().ExecutePostRequest(
			"http://localhost/0/DataService/json/SyncReply/InsertQuery",
			Arg.Is<string>(body =>
				body.Contains("\"rootSchemaName\":\"SysSettings\"") &&
				body.Contains("\"Name\"")),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
		_applicationClient.Received().ExecutePostRequest(
			"http://localhost/0/ServiceModel/SchemaDataDesignerService.svc/SaveSchema",
			Arg.Is<string>(body =>
				body.Contains("\"name\":\"UsrRemoteBinding\"") &&
				body.Contains("\"entitySchemaName\":\"SysSettings\"") &&
				body.Contains("\"boundRecordIds\":[") &&
				body.Contains("\"isKey\":true")),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
	}

	[Test]
	[Description("Projects SaveSchema metadata to the primary key plus referenced columns so unrelated unsupported runtime columns do not block DB-first create-data-binding-db.")]
	public void CreateDataBindingDb_Should_Project_SaveSchema_To_Referenced_Columns_When_Runtime_Schema_Has_Unsupported_Columns() {
		// Arrange
		_schemaResponseJson = BuildSchemaResponseJson(
			"Account",
			(Guid.Parse("ae0e45ca-c495-4fe7-a39d-3ab7278e1617"), "Id", 0),
			(Guid.Parse("736c30a7-c0ec-4fa9-b034-2552b319b633"), "Name", 28),
			(Guid.Parse("11111111-2222-3333-4444-555555555555"), "UsrUnsupportedBlob", 16));
		_bindingLookupResponseJson = BuildBindingLookupResponse("Account", "UsrAccountBinding");
		_boundSchemaDataItemsJson = JsonSerializer.Serialize(new[] {
			new Dictionary<string, object?> {
				["Id"] = ExistingRowId,
				["Name"] = "Existing account"
			}
		});
		CreateDataBindingDbOptions options = new() {
			Environment = "dev",
			PackageName = PackageName,
			SchemaName = "Account",
			BindingName = "UsrAccountBinding",
			RowsJson = """[{"values":{"Name":"Projected account row"}}]"""
		};

		// Act
		int result = _createCommand.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "DB-first binding creation should ignore unrelated unsupported runtime columns that are not referenced by the rows");
		_applicationClient.Received().ExecutePostRequest(
			"http://localhost/0/ServiceModel/SchemaDataDesignerService.svc/SaveSchema",
			Arg.Is<string>(body =>
				body.Contains("\"name\":\"UsrAccountBinding\"") &&
				body.Contains("\"entitySchemaName\":\"Account\"") &&
				body.Contains("\"Name\"") &&
				body.Contains("\"Id\"") &&
				!body.Contains("UsrUnsupportedBlob")),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
	}

	[Test]
	[Description("Fails create-data-binding-db before remote writes when the requested row values explicitly reference a runtime column with an unsupported dataValueType.")]
	public void CreateDataBindingDb_Should_Fail_When_Rows_Reference_Unsupported_Runtime_Column() {
		// Arrange
		_schemaResponseJson = BuildSchemaResponseJson(
			"Account",
			(Guid.Parse("ae0e45ca-c495-4fe7-a39d-3ab7278e1617"), "Id", 0),
			(Guid.Parse("736c30a7-c0ec-4fa9-b034-2552b319b633"), "Name", 28),
			(Guid.Parse("11111111-2222-3333-4444-555555555555"), "UsrUnsupportedBlob", 16));
		CreateDataBindingDbOptions options = new() {
			Environment = "dev",
			PackageName = PackageName,
			SchemaName = "Account",
			RowsJson = """[{"values":{"UsrUnsupportedBlob":"payload"}}]"""
		};

		// Act
		int result = _createCommand.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "DB-first binding creation must fail fast when the requested row explicitly uses an unsupported runtime column");
		_logger.Received(1).WriteError(
			Arg.Is<string>(message =>
				message.Contains("UsrUnsupportedBlob") &&
				message.Contains("dataValueType '16'")));
		_applicationClient.DidNotReceive().ExecutePostRequest(
			"http://localhost/0/DataService/json/SyncReply/InsertQuery",
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
		_applicationClient.DidNotReceive().ExecutePostRequest(
			"http://localhost/0/ServiceModel/SchemaDataDesignerService.svc/SaveSchema",
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
	}

	[Test]
	[Description("Removes the last bound row through remove-data-binding-row-db, deletes the runtime row, and removes the package schema data record when no bound rows remain.")]
	public void RemoveDataBindingRowDb_Should_Delete_Remote_Row_And_Package_Schema_Data_When_Last_Row_Is_Removed() {
		// Arrange
		_boundSchemaDataItemsJson = JsonSerializer.Serialize(new[] {
			new Dictionary<string, object?> {
				["Id"] = ExistingRowId,
				["Name"] = "Existing row"
			}
		});
		RemoveDataBindingRowDbOptions options = new() {
			Environment = "dev",
			PackageName = PackageName,
			BindingName = "SysSettings",
			KeyValue = ExistingRowId.ToString()
		};

		// Act
		int result = _removeCommand.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "remove-data-binding-row-db should delete the remote row and remove the package schema data record when no rows remain bound");
		_applicationClient.Received().ExecutePostRequest(
			"http://localhost/0/DataService/json/SyncReply/DeleteQuery",
			Arg.Is<string>(body =>
				body.Contains("\"rootSchemaName\":\"SysSettings\"") &&
				body.Contains("\"Terrasoft.Nui.ServiceModel.DataContract.DeleteQuery\"") &&
				body.Contains(ExistingRowId.ToString())),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
		_applicationClient.Received().ExecutePostRequest(
			"http://localhost/0/DataService/json/SyncReply/DeletePackageSchemaDataRequest",
			Arg.Is<string>(body =>
				body.Contains("\"packageSchemaDataName\":\"SysSettings\"") &&
				body.Contains(PackageUId.ToString())),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
	}

	[Test]
	[Description("Removes one bound row when other rows still remain, updates SaveSchema with remaining bound record IDs instead of deleting the binding.")]
	public void RemoveDataBindingRowDb_Should_Call_SaveSchema_With_Remaining_Ids_When_Other_Rows_Exist() {
		// Arrange
		Guid remainingRowId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
		_boundSchemaDataItemsJson = JsonSerializer.Serialize(new[] {
			new Dictionary<string, object?> {
				["Id"] = ExistingRowId,
				["Name"] = "Row to remove"
			},
			new Dictionary<string, object?> {
				["Id"] = remainingRowId,
				["Name"] = "Remaining row"
			}
		});
		RemoveDataBindingRowDbOptions options = new() {
			Environment = "dev",
			PackageName = PackageName,
			BindingName = "SysSettings",
			KeyValue = ExistingRowId.ToString()
		};

		// Act
		int result = _removeCommand.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "remove-data-binding-row-db should succeed when other rows remain");
		_applicationClient.Received().ExecutePostRequest(
			"http://localhost/0/DataService/json/SyncReply/DeleteQuery",
			Arg.Is<string>(body =>
				body.Contains(ExistingRowId.ToString())),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
		_applicationClient.Received().ExecutePostRequest(
			Arg.Is<string>(url => url.Contains("SaveSchema")),
			Arg.Is<string>(body =>
				body.Contains($"\"{remainingRowId}\"") &&
				!body.Contains($"\"{ExistingRowId}\"")),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
		_applicationClient.DidNotReceive().ExecutePostRequest(
			"http://localhost/0/DataService/json/SyncReply/DeletePackageSchemaDataRequest",
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
	}

	[Test]
	[Description("Projects SaveSchema metadata to the columns actually present in the existing bound rows and upsert payload, so unsupported runtime columns outside that subset do not block upsert-data-binding-row-db.")]
	public void UpsertDataBindingRowDb_Should_Project_SaveSchema_To_Bound_And_Requested_Columns() {
		// Arrange
		Guid rowId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
		_schemaResponseJson = BuildSchemaResponseJson(
			"Account",
			(Guid.Parse("ae0e45ca-c495-4fe7-a39d-3ab7278e1617"), "Id", 0),
			(Guid.Parse("736c30a7-c0ec-4fa9-b034-2552b319b633"), "Name", 28),
			(Guid.Parse("11111111-2222-3333-4444-555555555555"), "UsrUnsupportedBlob", 16));
		_bindingLookupResponseJson = BuildBindingLookupResponse("Account", "UsrAccountBinding");
		_boundSchemaDataItemsJson = JsonSerializer.Serialize(new[] {
			new Dictionary<string, object?> {
				["Id"] = rowId,
				["Name"] = "Existing account"
			}
		});
		UpsertDataBindingRowDbOptions options = new() {
			Environment = "dev",
			PackageName = PackageName,
			BindingName = "UsrAccountBinding",
			ValuesJson = $$"""{"Id":"{{rowId}}","Name":"Updated account"}"""
		};

		// Act
		int result = _upsertCommand.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "upsert-data-binding-row-db should continue to work when unsupported runtime columns are not part of the bound/requested subset");
		_applicationClient.Received().ExecutePostRequest(
			"http://localhost/0/ServiceModel/SchemaDataDesignerService.svc/SaveSchema",
			Arg.Is<string>(body =>
				body.Contains("\"entitySchemaName\":\"Account\"") &&
				body.Contains("\"Name\"") &&
				body.Contains("\"Id\"") &&
				!body.Contains("UsrUnsupportedBlob")),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
	}

	[Test]
	[Description("Projects SaveSchema metadata from the remaining bound rows after remove-data-binding-row-db so unrelated unsupported runtime columns do not block removal.")]
	public void RemoveDataBindingRowDb_Should_Project_SaveSchema_From_Remaining_Bound_Rows() {
		// Arrange
		Guid remainingRowId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
		_schemaResponseJson = BuildSchemaResponseJson(
			"Account",
			(Guid.Parse("ae0e45ca-c495-4fe7-a39d-3ab7278e1617"), "Id", 0),
			(Guid.Parse("736c30a7-c0ec-4fa9-b034-2552b319b633"), "Name", 28),
			(Guid.Parse("11111111-2222-3333-4444-555555555555"), "UsrUnsupportedBlob", 16));
		_bindingLookupResponseJson = BuildBindingLookupResponse("Account", "UsrAccountBinding");
		_boundSchemaDataItemsJson = JsonSerializer.Serialize(new[] {
			new Dictionary<string, object?> {
				["Id"] = ExistingRowId,
				["Name"] = "Row to remove"
			},
			new Dictionary<string, object?> {
				["Id"] = remainingRowId,
				["Name"] = "Remaining row"
			}
		});
		RemoveDataBindingRowDbOptions options = new() {
			Environment = "dev",
			PackageName = PackageName,
			BindingName = "UsrAccountBinding",
			KeyValue = ExistingRowId.ToString()
		};

		// Act
		int result = _removeCommand.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "remove-data-binding-row-db should rebuild SaveSchema from the remaining bound rows only");
		_applicationClient.Received().ExecutePostRequest(
			"http://localhost/0/ServiceModel/SchemaDataDesignerService.svc/SaveSchema",
			Arg.Is<string>(body =>
				body.Contains("\"entitySchemaName\":\"Account\"") &&
				body.Contains($"\"{remainingRowId}\"") &&
				!body.Contains("UsrUnsupportedBlob")),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
	}

	[Test]
	[Description("Skips InsertQuery when a row with the same Name already exists, reuses the existing row Id in the binding, and does not report a created row for the duplicate.")]
	public void CreateDataBindingDb_Should_Skip_Insert_And_Reuse_Existing_Id_When_Name_Already_Exists() {
		// Arrange - "New" already in entity table with a known Id
		const string existingNewId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
		_existingEntityNamesJson = $$"""{"rows":[{"Name":"New","Id":"{{existingNewId}}"}],"success":true}""";
		CreateDataBindingDbOptions options = new() {
			Environment = "dev",
			PackageName = PackageName,
			SchemaName = "SysSettings",
			RowsJson = """[{"values":{"Name":"New"}},{"values":{"Name":"Done"}}]"""
		};

		// Act
		int result = _createCommand.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "create-data-binding-db should succeed even when some rows already exist");
		_applicationClient.Received(1).ExecutePostRequest(
			"http://localhost/0/DataService/json/SyncReply/InsertQuery",
			Arg.Is<string>(body => body.Contains("\"Done\"")),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		_applicationClient.DidNotReceive().ExecutePostRequest(
			"http://localhost/0/DataService/json/SyncReply/InsertQuery",
			Arg.Is<string>(body => body.Contains("\"New\"")),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		_applicationClient.Received(1).ExecutePostRequest(
			"http://localhost/0/ServiceModel/SchemaDataDesignerService.svc/SaveSchema",
			Arg.Is<string>(body => body.Contains(existingNewId)),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		_logger.DidNotReceive().WriteInfo(
			Arg.Is<string>(msg => msg.Contains("Created row") && msg.Contains("New")));
	}

	private string BuildApplicationClientResponse(string url, string requestBody) {
		if (url.Contains("RuntimeEntitySchemaRequest", StringComparison.Ordinal)) {
			return _schemaResponseJson;
		}

		if (url.Contains("SelectQuery", StringComparison.Ordinal) &&
			requestBody.Contains("\"rootSchemaName\":\"SysPackageSchemaData\"", StringComparison.Ordinal)) {
			return _bindingLookupResponseJson;
		}

		if (url.Contains("SelectQuery", StringComparison.Ordinal) &&
			!requestBody.Contains("\"rootSchemaName\":\"SysPackageSchemaData\"", StringComparison.Ordinal)) {
			return _existingEntityNamesJson;
		}

		if (url.Contains("GetBoundSchemaData", StringComparison.Ordinal)) {
			return $$"""{"success":true,"items":{{JsonSerializer.Serialize(_boundSchemaDataItemsJson)}}}""";
		}

		if (url.Contains("SaveSchema", StringComparison.Ordinal) ||
			url.Contains("DeletePackageSchemaDataRequest", StringComparison.Ordinal)) {
			return """{"success":true}""";
		}

		if (url.Contains("InsertQuery", StringComparison.Ordinal)) {
			return """{"id":"00000000-0000-0000-0000-000000000000","rowsAffected":1,"nextPrcElReady":false,"success":true}""";
		}

		if (url.Contains("UpdateQuery", StringComparison.Ordinal) ||
			url.Contains("DeleteQuery", StringComparison.Ordinal)) {
			return """{"rowsAffected":1}""";
		}

		return """{"success":true}""";
	}

	private static string BuildBindingLookupResponse(string schemaName, string? bindingName = null) {
		return $$"""
		{
		  "rows": [
		    {
		      "Id": "{{ExistingBindingUId}}",
		      "UId": "{{ExistingBindingUId}}",
		      "Name": "{{bindingName ?? schemaName}}",
		      "EntitySchemaName": "{{schemaName}}"
		    }
		  ]
		}
		""";
	}

	private static string BuildSchemaResponseJson(
		string schemaName,
		params (Guid UId, string Name, int DataValueType)[] columns) {
		(string UId, string Name, int DataValueType) primaryColumn = columns
			.Select(column => (column.UId.ToString(), column.Name, column.DataValueType))
			.First(column => string.Equals(column.Name, "Id", StringComparison.OrdinalIgnoreCase));
		var payload = new {
			schema = new {
				columns = new {
					Items = columns.ToDictionary(
						column => column.UId.ToString(),
						column => new {
							uId = column.UId.ToString(),
							name = column.Name,
							dataValueType = column.DataValueType
						})
				},
				primaryColumnUId = primaryColumn.UId,
				uId = Guid.NewGuid().ToString(),
				name = schemaName
			},
			success = true
		};
		return JsonSerializer.Serialize(payload);
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
