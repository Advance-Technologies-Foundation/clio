using System;
using System.Collections.Generic;
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
internal sealed class DataBindingDbCommandTests : BaseClioModuleTests {
	private const string PackageName = "TestPkg";
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
	}

	public override void TearDown() {
		base.TearDown();
		_applicationClient.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		_packageListProvider.ClearReceivedCalls();
	}

	protected override MockFileSystem CreateFs() {
		return new MockFileSystem(new Dictionary<string, MockFileData>(), currentDirectory: @"C:\workspace");
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

	private string BuildApplicationClientResponse(string url, string requestBody) {
		if (url.Contains("RuntimeEntitySchemaRequest", StringComparison.Ordinal)) {
			return SchemaResponseJson;
		}

		if (url.Contains("SelectQuery", StringComparison.Ordinal) &&
			requestBody.Contains("\"rootSchemaName\":\"SysPackageSchemaData\"", StringComparison.Ordinal)) {
			return _bindingLookupResponseJson;
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

	private static string BuildBindingLookupResponse(string schemaName) {
		return $$"""
		{
		  "rows": [
		    {
		      "Id": "{{ExistingBindingUId}}",
		      "UId": "{{ExistingBindingUId}}",
		      "Name": "SysSettings",
		      "EntitySchemaName": "{{schemaName}}"
		    }
		  ]
		}
		""";
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
