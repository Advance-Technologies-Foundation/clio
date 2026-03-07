using System;
using System.Text.Json;
using Clio.Command;
using Clio.Common;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class DeleteSchemaCommandTests : BaseCommandTests<DeleteSchemaOptions> {

	private const string DeleteUrl =
		"https://localhost/0/ServiceModel/WorkspaceExplorerService.svc/Delete";
	private const string GetWorkspaceItemsUrl =
		"https://localhost/0/ServiceModel/WorkspaceExplorerService.svc/GetWorkspaceItems";
	private const string WorkspaceRootPath = @"C:\workspace";
	private const string WorkspaceSettingsPath = @"C:\workspace\.clio\workspaceSettings.json";

	[Test]
	[Category("Unit")]
	public void Execute_Should_Delete_Schema_From_Workspace_Package() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		IJsonConverter jsonConverter = Substitute.For<IJsonConverter>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		EnvironmentSettings settings = new() {
			Uri = "https://localhost",
			IsNetCore = false
		};
		Guid itemId = Guid.Parse("16cd93aa-c7ce-445c-9418-c46439708abe");
		Guid itemUId = Guid.Parse("2d3946f3-28d5-4560-bb34-f13d14572e96");
		Guid packageUId = Guid.Parse("1d07fd0e-2ca4-4d20-93b4-eb5a795ea03f");

		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetWorkspaceItems)
			.Returns(GetWorkspaceItemsUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.DeleteWorkspaceItem)
			.Returns(DeleteUrl);
		workspacePathBuilder.RootPath.Returns(WorkspaceRootPath);
		workspacePathBuilder.WorkspaceSettingsPath.Returns(WorkspaceSettingsPath);
		workspacePathBuilder.IsWorkspace.Returns(true);
		fileSystem.ExistsDirectory(WorkspaceRootPath).Returns(true);
		jsonConverter.DeserializeObjectFromFile<WorkspaceSettings>(WorkspaceSettingsPath).Returns(new WorkspaceSettings {
			Packages = ["MyPackage"]
		});
		applicationClient.ExecutePostRequest(GetWorkspaceItemsUrl, string.Empty, Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(GetWorkspaceItemsResponse(itemId, itemUId, packageUId));
		applicationClient.ExecutePostRequest(DeleteUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("""{"rowsAffected":1,"success":true,"errorInfo":null}""");

		DeleteSchemaCommand command = new(applicationClient, settings, serviceUrlBuilder, workspacePathBuilder,
			jsonConverter, fileSystem);
		DeleteSchemaOptions options = new() {
			SchemaName = "UsrSendInvoice"
		};

		int result = command.Execute(options);

		result.Should().Be(0);
		Received.InOrder(() => {
			applicationClient.ExecutePostRequest(GetWorkspaceItemsUrl, string.Empty, 100000, 3, 1);
			applicationClient.ExecutePostRequest(DeleteUrl,
				Arg.Is<string>(body => MatchesDeleteRequest(body, itemId, itemUId, packageUId)),
				100000, 3, 1);
		});
	}

	[Test]
	[Category("Unit")]
	public void Execute_Should_Use_Explicit_Workspace_Path_When_Provided() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		IJsonConverter jsonConverter = Substitute.For<IJsonConverter>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		EnvironmentSettings settings = new();
		const string explicitWorkspacePath = @"D:\alt-workspace";
		const string explicitWorkspaceSettingsPath = @"D:\alt-workspace\.clio\workspaceSettings.json";

		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetWorkspaceItems)
			.Returns(GetWorkspaceItemsUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.DeleteWorkspaceItem)
			.Returns(DeleteUrl);
		workspacePathBuilder.WorkspaceSettingsPath.Returns(explicitWorkspaceSettingsPath);
		workspacePathBuilder.IsWorkspace.Returns(true);
		fileSystem.ExistsDirectory(explicitWorkspacePath).Returns(true);
		jsonConverter.DeserializeObjectFromFile<WorkspaceSettings>(explicitWorkspaceSettingsPath).Returns(
			new WorkspaceSettings {
				Packages = ["MyPackage"]
			});
		applicationClient.ExecutePostRequest(GetWorkspaceItemsUrl, string.Empty, Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("""{"items":[{"id":"16cd93aa-c7ce-445c-9418-c46439708abe","uId":"2d3946f3-28d5-4560-bb34-f13d14572e96","name":"UsrSendInvoice","title":"Send invoice","packageUId":"1d07fd0e-2ca4-4d20-93b4-eb5a795ea03f","packageName":"MyPackage","type":8,"modifiedOn":"2026-03-07T05:50:52.434Z","isChanged":true,"isLocked":true,"isReadOnly":false}]}""");
		applicationClient.ExecutePostRequest(DeleteUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("""{"rowsAffected":1,"success":true,"errorInfo":null}""");

		DeleteSchemaCommand command = new(applicationClient, settings, serviceUrlBuilder, workspacePathBuilder,
			jsonConverter, fileSystem);
		DeleteSchemaOptions options = new() {
			SchemaName = "UsrSendInvoice",
			WorkspacePath = explicitWorkspacePath
		};

		int result = command.Execute(options);

		result.Should().Be(0);
		workspacePathBuilder.Received().RootPath = explicitWorkspacePath;
	}

	[Test]
	[Category("Unit")]
	public void Execute_Should_Return_Error_When_Schema_Is_Not_In_Current_Workspace() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		IJsonConverter jsonConverter = Substitute.For<IJsonConverter>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		EnvironmentSettings settings = new();

		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetWorkspaceItems)
			.Returns(GetWorkspaceItemsUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.DeleteWorkspaceItem)
			.Returns(DeleteUrl);
		workspacePathBuilder.RootPath.Returns(WorkspaceRootPath);
		workspacePathBuilder.WorkspaceSettingsPath.Returns(WorkspaceSettingsPath);
		workspacePathBuilder.IsWorkspace.Returns(true);
		fileSystem.ExistsDirectory(WorkspaceRootPath).Returns(true);
		jsonConverter.DeserializeObjectFromFile<WorkspaceSettings>(WorkspaceSettingsPath).Returns(new WorkspaceSettings {
			Packages = ["MyPackage"]
		});
		applicationClient.ExecutePostRequest(GetWorkspaceItemsUrl, string.Empty, Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("""{"items":[{"id":"16cd93aa-c7ce-445c-9418-c46439708abe","uId":"2d3946f3-28d5-4560-bb34-f13d14572e96","name":"UsrSendInvoice","packageUId":"1d07fd0e-2ca4-4d20-93b4-eb5a795ea03f","packageName":"OtherPackage","type":8,"modifiedOn":"2026-03-07T05:50:52.434Z","isChanged":true,"isLocked":true,"isReadOnly":false}]}""");

		DeleteSchemaCommand command = new(applicationClient, settings, serviceUrlBuilder, workspacePathBuilder,
			jsonConverter, fileSystem);
		DeleteSchemaOptions options = new() {
			SchemaName = "UsrSendInvoice"
		};

		int result = command.Execute(options);

		result.Should().Be(1);
		applicationClient.DidNotReceive()
			.ExecutePostRequest(DeleteUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	public void Execute_Should_Return_Error_When_Current_Directory_Is_Not_A_Workspace() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		IJsonConverter jsonConverter = Substitute.For<IJsonConverter>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		EnvironmentSettings settings = new();

		workspacePathBuilder.RootPath.Returns(WorkspaceRootPath);
		workspacePathBuilder.IsWorkspace.Returns(false);
		fileSystem.ExistsDirectory(WorkspaceRootPath).Returns(true);

		DeleteSchemaCommand command = new(applicationClient, settings, serviceUrlBuilder, workspacePathBuilder,
			jsonConverter, fileSystem);
		DeleteSchemaOptions options = new() {
			SchemaName = "UsrSendInvoice"
		};

		int result = command.Execute(options);

		result.Should().Be(1);
		applicationClient.DidNotReceiveWithAnyArgs()
			.ExecutePostRequest(default, default, default, default, default);
	}

	private static string GetWorkspaceItemsResponse(Guid itemId, Guid itemUId, Guid packageUId) {
		var response = new {
			items = new[] {
				new {
					id = itemId,
					uId = itemUId,
					name = "UsrSendInvoice",
					title = "Send invoice",
					packageUId,
					packageName = "MyPackage",
					type = 8,
					modifiedOn = "2026-03-07T05:50:52.434Z",
					isChanged = true,
					isLocked = true,
					isReadOnly = false
				},
				new {
					id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
					uId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
					name = "Account",
					title = "Account",
					packageUId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
					packageName = "CrtBase",
					type = 3,
					modifiedOn = "2026-03-04T23:17:20.000Z",
					isChanged = false,
					isLocked = false,
					isReadOnly = true
				}
			}
		};
		return JsonSerializer.Serialize(response);
	}

	private static bool MatchesDeleteRequest(string json, Guid itemId, Guid itemUId, Guid packageUId) {
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement item = document.RootElement[0];
		return document.RootElement.GetArrayLength() == 1
			&& item.GetProperty("id").GetString() == itemId.ToString()
			&& item.GetProperty("uId").GetString() == itemUId.ToString()
			&& item.GetProperty("name").GetString() == "UsrSendInvoice"
			&& item.GetProperty("title").GetString() == "Send invoice"
			&& item.GetProperty("packageUId").GetString() == packageUId.ToString()
			&& item.GetProperty("packageName").GetString() == "MyPackage"
			&& item.GetProperty("type").GetInt32() == 8
			&& item.GetProperty("modifiedOn").GetString() == "2026-03-07T05:50:52.434Z"
			&& item.GetProperty("isChanged").GetBoolean()
			&& item.GetProperty("isLocked").GetBoolean()
			&& !item.GetProperty("isReadOnly").GetBoolean();
	}
}
