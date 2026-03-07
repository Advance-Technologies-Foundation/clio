using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class ModifyUserTaskParametersCommandTests : BaseCommandTests<ModifyUserTaskParametersOptions> {
	private const string GetWorkspaceItemsUrl =
		"https://localhost/0/ServiceModel/WorkspaceExplorerService.svc/GetWorkspaceItems";
	private const string GetSchemaUrl =
		"https://localhost/0/ServiceModel/ProcessUserTaskSchemaDesignerService.svc/GetSchema";
	private const string SaveSchemaUrl =
		"https://localhost/0/ServiceModel/ProcessUserTaskSchemaDesignerService.svc/SaveSchema";
	private const string BuildPackageUrl =
		"https://localhost/0/ServiceModel/WorkspaceExplorerService.svc/BuildPackage";
	private const string WorkspaceRootPath = @"C:\workspace";
	private const string WorkspaceSettingsPath = @"C:\workspace\.clio\workspaceSettings.json";

	[Test]
	[Description("Loads an existing workspace user task, removes requested parameters, adds new ones, saves it, and builds the owning package.")]
	[Category("Unit")]
	public void Execute_Should_Add_And_Remove_Parameters_On_Existing_UserTask() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		IJsonConverter jsonConverter = Substitute.For<IJsonConverter>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		IFileDesignModePackages fileDesignModePackages = Substitute.For<IFileDesignModePackages>();
		IUserTaskMetadataDirectionApplier metadataDirectionApplier = Substitute.For<IUserTaskMetadataDirectionApplier>();
		IUserTaskLookupSchemaResolver lookupSchemaResolver = Substitute.For<IUserTaskLookupSchemaResolver>();
		EnvironmentSettings settings = new() {
			Uri = "https://localhost",
			IsNetCore = false
		};
		Guid itemId = Guid.Parse("16cd93aa-c7ce-445c-9418-c46439708abe");
		Guid schemaUId = Guid.Parse("2d3946f3-28d5-4560-bb34-f13d14572e96");
		Guid packageUId = Guid.Parse("1d07fd0e-2ca4-4d20-93b4-eb5a795ea03f");
		Guid schemaId = Guid.Parse("937d9454-ef79-49e3-b098-4340bca01fd8");

		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetWorkspaceItems).Returns(GetWorkspaceItemsUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetUserTaskSchema).Returns(GetSchemaUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SaveUserTaskSchema).Returns(SaveSchemaUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.BuildPackage).Returns(BuildPackageUrl);
		workspacePathBuilder.RootPath.Returns(WorkspaceRootPath);
		workspacePathBuilder.WorkspaceSettingsPath.Returns(WorkspaceSettingsPath);
		workspacePathBuilder.IsWorkspace.Returns(true);
		fileSystem.ExistsDirectory(WorkspaceRootPath).Returns(true);
		jsonConverter.DeserializeObjectFromFile<WorkspaceSettings>(WorkspaceSettingsPath).Returns(new WorkspaceSettings {
			Packages = ["MyPackage"]
		});
		applicationClient.ExecutePostRequest(GetWorkspaceItemsUrl, string.Empty, Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(GetWorkspaceItemsResponse(itemId, schemaUId, packageUId));
		applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(GetSchemaResponse(schemaUId, schemaId, packageUId));
		applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns($$"""{"success":true,"schemaUid":"{{schemaUId}}","validationErrors":[]}""");
		applicationClient.ExecutePostRequest(BuildPackageUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("{}");

		ModifyUserTaskParametersCommand command = new(applicationClient, settings, serviceUrlBuilder,
			workspacePathBuilder, jsonConverter, fileSystem, fileDesignModePackages, metadataDirectionApplier,
			lookupSchemaResolver);
		ModifyUserTaskParametersOptions options = new() {
			UserTaskName = "UsrSendInvoice",
			Culture = "en-US",
			AddParameters = ["code=IsError;title=Is error;type=Boolean;direction=In"],
			RemoveParameters = ["ObsoleteFlag"]
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "because the user task exists in the workspace and the parameter changes are valid");
		Received.InOrder(() => {
			applicationClient.ExecutePostRequest(GetWorkspaceItemsUrl, string.Empty, 100000, 3, 1);
			applicationClient.ExecutePostRequest(GetSchemaUrl,
				Arg.Is<string>(body => HasPropertyWithValue(body, "schemaUId", schemaUId.ToString())),
				100000, 3, 1);
			applicationClient.ExecutePostRequest(SaveSchemaUrl,
				Arg.Is<string>(body => MatchesSaveRequest(body, schemaUId, packageUId)),
				100000, 3, 1);
			applicationClient.ExecutePostRequest(BuildPackageUrl,
				Arg.Is<string>(body => HasPropertyWithValue(body, "packageName", "MyPackage")),
				100000, 3, 1);
			applicationClient.ExecutePostRequest(BuildPackageUrl,
				Arg.Is<string>(body => HasPropertyWithValue(body, "packageName", "MyPackage")),
				100000, 3, 1);
		});
		metadataDirectionApplier.Received(1).ApplyDirections(
			"MyPackage",
			"UsrSendInvoice",
			Arg.Is<IReadOnlyDictionary<string, int>>(directions =>
				directions.Count == 1 && directions["IsError"] == 0));
		fileDesignModePackages.Received(1).LoadPackagesToDb();
	}

	[Test]
	[Description("Loads an existing workspace user task, updates parameter direction, saves it, and builds the owning package.")]
	[Category("Unit")]
	public void Execute_Should_Update_Direction_On_Existing_Parameter() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		IJsonConverter jsonConverter = Substitute.For<IJsonConverter>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		IFileDesignModePackages fileDesignModePackages = Substitute.For<IFileDesignModePackages>();
		IUserTaskMetadataDirectionApplier metadataDirectionApplier = Substitute.For<IUserTaskMetadataDirectionApplier>();
		IUserTaskLookupSchemaResolver lookupSchemaResolver = Substitute.For<IUserTaskLookupSchemaResolver>();
		EnvironmentSettings settings = new() {
			Uri = "https://localhost",
			IsNetCore = false
		};
		Guid itemId = Guid.Parse("16cd93aa-c7ce-445c-9418-c46439708abe");
		Guid schemaUId = Guid.Parse("2d3946f3-28d5-4560-bb34-f13d14572e96");
		Guid packageUId = Guid.Parse("1d07fd0e-2ca4-4d20-93b4-eb5a795ea03f");
		Guid schemaId = Guid.Parse("937d9454-ef79-49e3-b098-4340bca01fd8");

		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetWorkspaceItems).Returns(GetWorkspaceItemsUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetUserTaskSchema).Returns(GetSchemaUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SaveUserTaskSchema).Returns(SaveSchemaUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.BuildPackage).Returns(BuildPackageUrl);
		workspacePathBuilder.RootPath.Returns(WorkspaceRootPath);
		workspacePathBuilder.WorkspaceSettingsPath.Returns(WorkspaceSettingsPath);
		workspacePathBuilder.IsWorkspace.Returns(true);
		fileSystem.ExistsDirectory(WorkspaceRootPath).Returns(true);
		jsonConverter.DeserializeObjectFromFile<WorkspaceSettings>(WorkspaceSettingsPath).Returns(new WorkspaceSettings {
			Packages = ["MyPackage"]
		});
		applicationClient.ExecutePostRequest(GetWorkspaceItemsUrl, string.Empty, Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(GetWorkspaceItemsResponse(itemId, schemaUId, packageUId));
		applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(GetSchemaResponse(schemaUId, schemaId, packageUId));
		applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns($$"""{"success":true,"schemaUid":"{{schemaUId}}","validationErrors":[]}""");
		applicationClient.ExecutePostRequest(BuildPackageUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("{}");

		ModifyUserTaskParametersCommand command = new(applicationClient, settings, serviceUrlBuilder,
			workspacePathBuilder, jsonConverter, fileSystem, fileDesignModePackages, metadataDirectionApplier,
			lookupSchemaResolver);
		ModifyUserTaskParametersOptions options = new() {
			UserTaskName = "UsrSendInvoice",
			SetDirections = ["ExistingText=Out"]
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "because direction changes on an existing parameter should be saved");
		applicationClient.Received(1).ExecutePostRequest(SaveSchemaUrl,
			Arg.Is<string>(body => MatchesDirectionUpdateSaveRequest(body, schemaUId, packageUId, "ExistingText", 1)),
			100000, 3, 1);
		metadataDirectionApplier.Received(1).ApplyDirections(
			"MyPackage",
			"UsrSendInvoice",
			Arg.Is<IReadOnlyDictionary<string, int>>(directions =>
				directions.Count == 1 && directions["ExistingText"] == 1));
		fileDesignModePackages.Received(1).LoadPackagesToDb();
	}

	[Test]
	[Description("Loads an existing workspace user task, adds a lookup parameter, resolves the entity schema, saves it, and builds the owning package.")]
	[Category("Unit")]
	public void Execute_Should_Add_Lookup_Parameter_To_Existing_UserTask() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		IJsonConverter jsonConverter = Substitute.For<IJsonConverter>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		IFileDesignModePackages fileDesignModePackages = Substitute.For<IFileDesignModePackages>();
		IUserTaskMetadataDirectionApplier metadataDirectionApplier = Substitute.For<IUserTaskMetadataDirectionApplier>();
		IUserTaskLookupSchemaResolver lookupSchemaResolver = Substitute.For<IUserTaskLookupSchemaResolver>();
		EnvironmentSettings settings = new() {
			Uri = "https://localhost",
			IsNetCore = false
		};
		Guid itemId = Guid.Parse("16cd93aa-c7ce-445c-9418-c46439708abe");
		Guid schemaUId = Guid.Parse("2d3946f3-28d5-4560-bb34-f13d14572e96");
		Guid packageUId = Guid.Parse("1d07fd0e-2ca4-4d20-93b4-eb5a795ea03f");
		Guid schemaId = Guid.Parse("937d9454-ef79-49e3-b098-4340bca01fd8");
		Guid accountSchemaUId = Guid.Parse("25d7c1ab-1de0-4501-b402-02e0e5a72d6e");

		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetWorkspaceItems).Returns(GetWorkspaceItemsUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetUserTaskSchema).Returns(GetSchemaUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SaveUserTaskSchema).Returns(SaveSchemaUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.BuildPackage).Returns(BuildPackageUrl);
		workspacePathBuilder.RootPath.Returns(WorkspaceRootPath);
		workspacePathBuilder.WorkspaceSettingsPath.Returns(WorkspaceSettingsPath);
		workspacePathBuilder.IsWorkspace.Returns(true);
		fileSystem.ExistsDirectory(WorkspaceRootPath).Returns(true);
		jsonConverter.DeserializeObjectFromFile<WorkspaceSettings>(WorkspaceSettingsPath).Returns(new WorkspaceSettings {
			Packages = ["MyPackage"]
		});
		lookupSchemaResolver.Resolve(Arg.Any<Guid>(), "Account").Returns(new UserTaskLookupSchema {
			Name = "Account",
			UId = accountSchemaUId,
			Caption = "Account"
		});
		applicationClient.ExecutePostRequest(GetWorkspaceItemsUrl, string.Empty, Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(GetWorkspaceItemsResponse(itemId, schemaUId, packageUId));
		applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(GetSchemaResponse(schemaUId, schemaId, packageUId));
		applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns($$"""{"success":true,"schemaUid":"{{schemaUId}}","validationErrors":[]}""");
		applicationClient.ExecutePostRequest(BuildPackageUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("{}");

		ModifyUserTaskParametersCommand command = new(applicationClient, settings, serviceUrlBuilder,
			workspacePathBuilder, jsonConverter, fileSystem, fileDesignModePackages, metadataDirectionApplier,
			lookupSchemaResolver);
		ModifyUserTaskParametersOptions options = new() {
			UserTaskName = "UsrSendInvoice",
			AddParameters = ["code=AccountRef;title=Account reference;type=Lookup;lookup=Account"]
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "because lookup parameters should resolve to the same designer payload as Creatio");
		lookupSchemaResolver.Received(1).Resolve(packageUId, "Account");
		applicationClient.Received(1).ExecutePostRequest(SaveSchemaUrl,
			Arg.Is<string>(body => MatchesLookupAddSaveRequest(body, schemaUId, packageUId, accountSchemaUId)),
			100000, 3, 1);
	}

	[Test]
	[Description("Loads an existing workspace user task, adds an item to an existing composite list parameter, saves it, and builds the owning package.")]
	[Category("Unit")]
	public void Execute_Should_Add_Item_To_Existing_Composite_List_Parameter() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		IJsonConverter jsonConverter = Substitute.For<IJsonConverter>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		IFileDesignModePackages fileDesignModePackages = Substitute.For<IFileDesignModePackages>();
		IUserTaskMetadataDirectionApplier metadataDirectionApplier = Substitute.For<IUserTaskMetadataDirectionApplier>();
		IUserTaskLookupSchemaResolver lookupSchemaResolver = Substitute.For<IUserTaskLookupSchemaResolver>();
		EnvironmentSettings settings = new() {
			Uri = "https://localhost",
			IsNetCore = false
		};
		Guid itemId = Guid.Parse("16cd93aa-c7ce-445c-9418-c46439708abe");
		Guid schemaUId = Guid.Parse("2d3946f3-28d5-4560-bb34-f13d14572e96");
		Guid packageUId = Guid.Parse("1d07fd0e-2ca4-4d20-93b4-eb5a795ea03f");
		Guid schemaId = Guid.Parse("937d9454-ef79-49e3-b098-4340bca01fd8");

		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetWorkspaceItems).Returns(GetWorkspaceItemsUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetUserTaskSchema).Returns(GetSchemaUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SaveUserTaskSchema).Returns(SaveSchemaUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.BuildPackage).Returns(BuildPackageUrl);
		workspacePathBuilder.RootPath.Returns(WorkspaceRootPath);
		workspacePathBuilder.WorkspaceSettingsPath.Returns(WorkspaceSettingsPath);
		workspacePathBuilder.IsWorkspace.Returns(true);
		fileSystem.ExistsDirectory(WorkspaceRootPath).Returns(true);
		jsonConverter.DeserializeObjectFromFile<WorkspaceSettings>(WorkspaceSettingsPath).Returns(new WorkspaceSettings {
			Packages = ["MyPackage"]
		});
		applicationClient.ExecutePostRequest(GetWorkspaceItemsUrl, string.Empty, Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(GetWorkspaceItemsResponse(itemId, schemaUId, packageUId));
		applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(GetSchemaResponseWithCompositeList(schemaUId, schemaId, packageUId));
		applicationClient.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns($$"""{"success":true,"schemaUid":"{{schemaUId}}","validationErrors":[]}""");
		applicationClient.ExecutePostRequest(BuildPackageUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("{}");

		ModifyUserTaskParametersCommand command = new(applicationClient, settings, serviceUrlBuilder,
			workspacePathBuilder, jsonConverter, fileSystem, fileDesignModePackages, metadataDirectionApplier,
			lookupSchemaResolver);
		ModifyUserTaskParametersOptions options = new() {
			UserTaskName = "UsrSendInvoice",
			AddParameterItems = ["parent=ExistingList;code=Bool1;title=Bool1;type=Boolean"]
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "because child items can be added to an existing composite list parameter");
		applicationClient.Received(1).ExecutePostRequest(SaveSchemaUrl,
			Arg.Is<string>(body => MatchesCompositeListItemSaveRequest(body, schemaUId, packageUId)),
			100000, 3, 1);
		fileDesignModePackages.DidNotReceive().LoadPackagesToDb();
	}

	[Test]
	[Description("Returns an error when a requested parameter removal does not exist on the current user task.")]
	[Category("Unit")]
	public void Execute_Should_Return_Error_When_Parameter_To_Remove_Does_Not_Exist() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		IJsonConverter jsonConverter = Substitute.For<IJsonConverter>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		IFileDesignModePackages fileDesignModePackages = Substitute.For<IFileDesignModePackages>();
		IUserTaskMetadataDirectionApplier metadataDirectionApplier = Substitute.For<IUserTaskMetadataDirectionApplier>();
		IUserTaskLookupSchemaResolver lookupSchemaResolver = Substitute.For<IUserTaskLookupSchemaResolver>();
		EnvironmentSettings settings = new() {
			Uri = "https://localhost",
			IsNetCore = false
		};
		Guid itemId = Guid.Parse("16cd93aa-c7ce-445c-9418-c46439708abe");
		Guid schemaUId = Guid.Parse("2d3946f3-28d5-4560-bb34-f13d14572e96");
		Guid packageUId = Guid.Parse("1d07fd0e-2ca4-4d20-93b4-eb5a795ea03f");
		Guid schemaId = Guid.Parse("937d9454-ef79-49e3-b098-4340bca01fd8");

		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetWorkspaceItems).Returns(GetWorkspaceItemsUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetUserTaskSchema).Returns(GetSchemaUrl);
		workspacePathBuilder.RootPath.Returns(WorkspaceRootPath);
		workspacePathBuilder.WorkspaceSettingsPath.Returns(WorkspaceSettingsPath);
		workspacePathBuilder.IsWorkspace.Returns(true);
		fileSystem.ExistsDirectory(WorkspaceRootPath).Returns(true);
		jsonConverter.DeserializeObjectFromFile<WorkspaceSettings>(WorkspaceSettingsPath).Returns(new WorkspaceSettings {
			Packages = ["MyPackage"]
		});
		applicationClient.ExecutePostRequest(GetWorkspaceItemsUrl, string.Empty, Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(GetWorkspaceItemsResponse(itemId, schemaUId, packageUId));
		applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(GetSchemaResponse(schemaUId, schemaId, packageUId));

		ModifyUserTaskParametersCommand command = new(applicationClient, settings, serviceUrlBuilder,
			workspacePathBuilder, jsonConverter, fileSystem, fileDesignModePackages, metadataDirectionApplier,
			lookupSchemaResolver);
		ModifyUserTaskParametersOptions options = new() {
			UserTaskName = "UsrSendInvoice",
			RemoveParameters = ["MissingParameter"]
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "because removing a non-existent parameter should fail fast");
		applicationClient.DidNotReceive().ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Returns an error when the requested user task does not belong to any package in the current workspace.")]
	[Category("Unit")]
	public void Execute_Should_Return_Error_When_UserTask_Is_Not_In_Current_Workspace() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		IJsonConverter jsonConverter = Substitute.For<IJsonConverter>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		IFileDesignModePackages fileDesignModePackages = Substitute.For<IFileDesignModePackages>();
		IUserTaskMetadataDirectionApplier metadataDirectionApplier = Substitute.For<IUserTaskMetadataDirectionApplier>();
		IUserTaskLookupSchemaResolver lookupSchemaResolver = Substitute.For<IUserTaskLookupSchemaResolver>();
		EnvironmentSettings settings = new();

		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetWorkspaceItems).Returns(GetWorkspaceItemsUrl);
		workspacePathBuilder.RootPath.Returns(WorkspaceRootPath);
		workspacePathBuilder.WorkspaceSettingsPath.Returns(WorkspaceSettingsPath);
		workspacePathBuilder.IsWorkspace.Returns(true);
		fileSystem.ExistsDirectory(WorkspaceRootPath).Returns(true);
		jsonConverter.DeserializeObjectFromFile<WorkspaceSettings>(WorkspaceSettingsPath).Returns(new WorkspaceSettings {
			Packages = ["MyPackage"]
		});
		applicationClient.ExecutePostRequest(GetWorkspaceItemsUrl, string.Empty, Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("""{"items":[{"id":"16cd93aa-c7ce-445c-9418-c46439708abe","uId":"2d3946f3-28d5-4560-bb34-f13d14572e96","name":"UsrSendInvoice","title":"Send invoice","packageUId":"1d07fd0e-2ca4-4d20-93b4-eb5a795ea03f","packageName":"OtherPackage","type":8,"modifiedOn":"2026-03-07T05:50:52.434Z","isChanged":true,"isLocked":true,"isReadOnly":false}]}""");

		ModifyUserTaskParametersCommand command = new(applicationClient, settings, serviceUrlBuilder,
			workspacePathBuilder, jsonConverter, fileSystem, fileDesignModePackages, metadataDirectionApplier,
			lookupSchemaResolver);
		ModifyUserTaskParametersOptions options = new() {
			UserTaskName = "UsrSendInvoice",
			AddParameters = ["code=IsError;title=Is error;type=Boolean"]
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "because only workspace-owned user tasks can be modified");
		applicationClient.DidNotReceive().ExecutePostRequest(GetSchemaUrl, Arg.Any<string>(), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("Returns an error when a direction update references a parameter that does not exist on the user task.")]
	[Category("Unit")]
	public void Execute_Should_Return_Error_When_Direction_Update_Target_Does_Not_Exist() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		IJsonConverter jsonConverter = Substitute.For<IJsonConverter>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		IFileDesignModePackages fileDesignModePackages = Substitute.For<IFileDesignModePackages>();
		IUserTaskMetadataDirectionApplier metadataDirectionApplier = Substitute.For<IUserTaskMetadataDirectionApplier>();
		IUserTaskLookupSchemaResolver lookupSchemaResolver = Substitute.For<IUserTaskLookupSchemaResolver>();
		EnvironmentSettings settings = new() {
			Uri = "https://localhost",
			IsNetCore = false
		};
		Guid itemId = Guid.Parse("16cd93aa-c7ce-445c-9418-c46439708abe");
		Guid schemaUId = Guid.Parse("2d3946f3-28d5-4560-bb34-f13d14572e96");
		Guid packageUId = Guid.Parse("1d07fd0e-2ca4-4d20-93b4-eb5a795ea03f");
		Guid schemaId = Guid.Parse("937d9454-ef79-49e3-b098-4340bca01fd8");

		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetWorkspaceItems).Returns(GetWorkspaceItemsUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetUserTaskSchema).Returns(GetSchemaUrl);
		workspacePathBuilder.RootPath.Returns(WorkspaceRootPath);
		workspacePathBuilder.WorkspaceSettingsPath.Returns(WorkspaceSettingsPath);
		workspacePathBuilder.IsWorkspace.Returns(true);
		fileSystem.ExistsDirectory(WorkspaceRootPath).Returns(true);
		jsonConverter.DeserializeObjectFromFile<WorkspaceSettings>(WorkspaceSettingsPath).Returns(new WorkspaceSettings {
			Packages = ["MyPackage"]
		});
		applicationClient.ExecutePostRequest(GetWorkspaceItemsUrl, string.Empty, Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(GetWorkspaceItemsResponse(itemId, schemaUId, packageUId));
		applicationClient.ExecutePostRequest(GetSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(GetSchemaResponse(schemaUId, schemaId, packageUId));

		ModifyUserTaskParametersCommand command = new(applicationClient, settings, serviceUrlBuilder,
			workspacePathBuilder, jsonConverter, fileSystem, fileDesignModePackages, metadataDirectionApplier,
			lookupSchemaResolver);
		ModifyUserTaskParametersOptions options = new() {
			UserTaskName = "UsrSendInvoice",
			SetDirections = ["MissingParameter=Out"]
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "because direction cannot be changed on a missing parameter");
		applicationClient.DidNotReceive().ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>());
	}

	private static string GetWorkspaceItemsResponse(Guid itemId, Guid schemaUId, Guid packageUId) {
		var response = new {
			items = new[] {
				new {
					id = itemId,
					uId = schemaUId,
					name = "UsrSendInvoice",
					title = "Send invoice",
					packageUId,
					packageName = "MyPackage",
					type = 8,
					modifiedOn = "2026-03-07T05:50:52.434Z",
					isChanged = true,
					isLocked = true,
					isReadOnly = false
				}
			}
		};
		return JsonSerializer.Serialize(response);
	}

	private static string GetSchemaResponse(Guid schemaUId, Guid schemaId, Guid packageUId) {
		var response = new {
			success = true,
			schema = new {
				uId = schemaUId,
				id = schemaId,
				name = "UsrSendInvoice",
				body = "",
				metaData = "{}",
				isReadOnly = false,
				useFullHierarchy = false,
				userLevelSchema = false,
				addonTypes = Array.Empty<string>(),
				caption = new[] {
					new { cultureName = "en-US", value = "Send invoice" }
				},
				localizableStrings = Array.Empty<object>(),
				description = new[] {
					new { cultureName = "en-US", value = "Existing task" }
				},
				parameters = new object[] {
					new {
						uId = Guid.Parse("678f06a4-7c84-49e9-9af4-c0f3600ecc41"),
						name = "ExistingText",
						caption = new[] {
							new { cultureName = "en-US", value = "Existing text" }
						},
						itemProperties = Array.Empty<object>(),
						type = 1,
						resulting = true,
						serializable = true,
						icon = "data-type-text-icon.svg"
					},
					new {
						uId = Guid.Parse("778f06a4-7c84-49e9-9af4-c0f3600ecc42"),
						name = "ObsoleteFlag",
						caption = new[] {
							new { cultureName = "en-US", value = "Obsolete flag" }
						},
						itemProperties = Array.Empty<object>(),
						type = 12,
						resulting = true,
						serializable = true,
						icon = "data-type-boolean-icon.svg"
					}
				},
				partial = false,
				userTask = true,
				customEventHandler = false,
				editPageSchemaUId = Guid.Empty,
				dcmEditPageSchemaUId = Guid.Empty,
				smallSvgImage = new { _isChanged = false },
				largeSvgImage = new { _isChanged = false },
				titleSvgImage = new { _isChanged = false },
				dcmSmallSvgImage = new { _isChanged = false },
				color = "#839DC3",
				serializeToDB = true,
				optionalProperties = Array.Empty<object>(),
				package = new { name = "MyPackage", uId = packageUId, type = 1 },
				dependencies = (object)null,
				forceSave = false,
				isFullHierarchyDesignSchema = false
			}
		};
		return JsonSerializer.Serialize(response);
	}

	private static string GetSchemaResponseWithCompositeList(Guid schemaUId, Guid schemaId, Guid packageUId) {
		var response = new {
			success = true,
			schema = new {
				uId = schemaUId,
				id = schemaId,
				name = "UsrSendInvoice",
				body = "",
				metaData = "{}",
				isReadOnly = false,
				useFullHierarchy = false,
				userLevelSchema = false,
				addonTypes = Array.Empty<string>(),
				caption = new[] {
					new { cultureName = "en-US", value = "Send invoice" }
				},
				localizableStrings = Array.Empty<object>(),
				description = new[] {
					new { cultureName = "en-US", value = "Existing task" }
				},
				parameters = new object[] {
					new {
						uId = Guid.Parse("878f06a4-7c84-49e9-9af4-c0f3600ecc43"),
						name = "ExistingList",
						caption = new[] {
							new { cultureName = "en-US", value = "Existing list" }
						},
						itemProperties = Array.Empty<object>(),
						type = 39,
						resulting = false,
						serializable = false,
						icon = "data-type-other-icon.svg"
					}
				},
				partial = false,
				userTask = true,
				customEventHandler = false,
				editPageSchemaUId = Guid.Empty,
				dcmEditPageSchemaUId = Guid.Empty,
				smallSvgImage = new { _isChanged = false },
				largeSvgImage = new { _isChanged = false },
				titleSvgImage = new { _isChanged = false },
				dcmSmallSvgImage = new { _isChanged = false },
				color = "#839DC3",
				serializeToDB = true,
				optionalProperties = Array.Empty<object>(),
				package = new { name = "MyPackage", uId = packageUId, type = 1 },
				dependencies = (object)null,
				forceSave = false,
				isFullHierarchyDesignSchema = false
			}
		};
		return JsonSerializer.Serialize(response);
	}

	private static bool HasPropertyWithValue(string json, string propertyName, string expectedValue) {
		using JsonDocument document = JsonDocument.Parse(json);
		return document.RootElement.GetProperty(propertyName).GetString() == expectedValue;
	}

	private static bool MatchesSaveRequest(string json, Guid schemaUId, Guid packageUId) {
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement root = document.RootElement;
		JsonElement parameters = root.GetProperty("parameters");

		return root.GetProperty("uId").GetString() == schemaUId.ToString()
			&& root.GetProperty("name").GetString() == "UsrSendInvoice"
			&& root.GetProperty("package").GetProperty("uId").GetString() == packageUId.ToString()
			&& parameters.GetArrayLength() == 2
			&& parameters[0].GetProperty("name").GetString() == "ExistingText"
			&& parameters[1].GetProperty("name").GetString() == "IsError"
			&& !HasParameter(parameters, "ObsoleteFlag")
			&& parameters[1].GetProperty("caption")[0].GetProperty("value").GetString() == "Is error"
			&& parameters[1].GetProperty("type").GetInt32() == 12
			&& parameters[1].GetProperty("direction").GetInt32() == 0
			&& parameters[1].GetProperty("icon").GetString() == "data-type-boolean-icon.svg";
	}

	private static bool MatchesDirectionUpdateSaveRequest(string json, Guid schemaUId, Guid packageUId,
		string parameterName, int expectedDirection) {
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement root = document.RootElement;
		JsonElement parameters = root.GetProperty("parameters");
		JsonElement updatedParameter = parameters.EnumerateArray()
			.First(parameter => string.Equals(parameter.GetProperty("name").GetString(), parameterName,
				StringComparison.OrdinalIgnoreCase));

		return root.GetProperty("uId").GetString() == schemaUId.ToString()
			&& root.GetProperty("package").GetProperty("uId").GetString() == packageUId.ToString()
			&& updatedParameter.GetProperty("direction").GetInt32() == expectedDirection;
	}

	private static bool MatchesLookupAddSaveRequest(string json, Guid schemaUId, Guid packageUId,
		Guid accountSchemaUId) {
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement root = document.RootElement;
		JsonElement addedParameter = root.GetProperty("parameters").EnumerateArray()
			.First(parameter => string.Equals(parameter.GetProperty("name").GetString(), "AccountRef",
				StringComparison.OrdinalIgnoreCase));

		return root.GetProperty("uId").GetString() == schemaUId.ToString()
			&& root.GetProperty("package").GetProperty("uId").GetString() == packageUId.ToString()
			&& addedParameter.GetProperty("type").GetInt32() == 10
			&& addedParameter.GetProperty("lookup").GetString() == accountSchemaUId.ToString()
			&& addedParameter.GetProperty("icon").GetString() == "data-type-lookup-icon.svg"
			&& !addedParameter.TryGetProperty("schema", out _);
	}

	private static bool MatchesCompositeListItemSaveRequest(string json, Guid schemaUId, Guid packageUId) {
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement root = document.RootElement;
		JsonElement compositeList = root.GetProperty("parameters").EnumerateArray()
			.First(parameter => string.Equals(parameter.GetProperty("name").GetString(), "ExistingList",
				StringComparison.OrdinalIgnoreCase));
		JsonElement childItem = compositeList.GetProperty("itemProperties").EnumerateArray()
			.First(parameter => string.Equals(parameter.GetProperty("name").GetString(), "Bool1",
				StringComparison.OrdinalIgnoreCase));

		return root.GetProperty("uId").GetString() == schemaUId.ToString()
			&& root.GetProperty("package").GetProperty("uId").GetString() == packageUId.ToString()
			&& compositeList.GetProperty("type").GetInt32() == 39
			&& childItem.GetProperty("type").GetInt32() == 12
			&& childItem.GetProperty("caption")[0].GetProperty("value").GetString() == "Bool1"
			&& childItem.GetProperty("icon").GetString() == "data-type-boolean-icon.svg";
	}

	private static bool HasParameter(JsonElement parameters, string name) {
		foreach (JsonElement parameter in parameters.EnumerateArray()) {
			if (string.Equals(parameter.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase)) {
				return true;
			}
		}

		return false;
	}
}
