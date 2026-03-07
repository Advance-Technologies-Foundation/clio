using System;
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
public class CreateUserTaskCommandTests : BaseCommandTests<CreateUserTaskOptions> {

	private const string CreateNewSchemaUrl =
		"https://localhost/0/ServiceModel/ProcessUserTaskSchemaDesignerService.svc/CreateNewSchema";
	private const string SaveSchemaUrl =
		"https://localhost/0/ServiceModel/ProcessUserTaskSchemaDesignerService.svc/SaveSchema";
	private const string BuildPackageUrl =
		"https://localhost/0/ServiceModel/WorkspaceExplorerService.svc/BuildPackage";

	[Test]
	[Description("Creates a user task schema, serializes requested parameters into the save payload, and builds the target package.")]
	[Category("Unit")]
	public void Execute_Should_Create_And_Save_UserTask_Schema() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		IJsonConverter jsonConverter = Substitute.For<IJsonConverter>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		EnvironmentSettings settings = new() {
			Uri = "https://localhost",
			IsNetCore = false
		};
		Guid packageUId = Guid.Parse("a00051f4-cde3-4f3f-b08e-c5ad1a5c735a");
		Guid schemaUId = Guid.Parse("8ab0dd66-18a8-4dc5-a452-74e0dc325bcf");
		Guid schemaId = Guid.Parse("937d9454-ef79-49e3-b098-4340bca01fd8");
		Guid editPageSchemaUId = Guid.Parse("c748bf4e-e4e1-454e-8f5b-4f65f91d8396");
		Guid dcmEditPageSchemaUId = Guid.Parse("d748bf4e-e4e1-454e-8f5b-4f65f91d8396");
		string packagePath = @"C:\workspace\packages\MyPackage";
		string descriptorPath = $"{packagePath}\\descriptor.json";
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.CreateUserTaskSchema)
			.Returns(CreateNewSchemaUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SaveUserTaskSchema)
			.Returns(SaveSchemaUrl);
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.BuildPackage)
			.Returns(BuildPackageUrl);
		workspacePathBuilder.IsWorkspace.Returns(true);
		workspacePathBuilder.BuildPackagePath("MyPackage").Returns(packagePath);
		fileSystem.ExistsDirectory(packagePath).Returns(true);
		fileSystem.ExistsFile(descriptorPath).Returns(true);
		jsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(descriptorPath).Returns(new PackageDescriptorDto {
			Descriptor = new PackageDescriptor {
				Name = "MyPackage",
				UId = packageUId,
				Type = PackageType.Assembly
			}
		});
		applicationClient
			.ExecutePostRequest(CreateNewSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(CreateNewSchemaResponse(schemaUId, schemaId, packageUId, editPageSchemaUId, dcmEditPageSchemaUId));
		applicationClient
			.ExecutePostRequest(SaveSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success":true,"schemaUid":"{{schemaUId}}","validationErrors":[]}""");
		applicationClient
			.ExecutePostRequest(BuildPackageUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{}");

		CreateUserTaskCommand command =
			new(applicationClient, settings, serviceUrlBuilder, workspacePathBuilder, jsonConverter, fileSystem);
		CreateUserTaskOptions options = new() {
			Package = "MyPackage",
			Code = "UsrMyUserTask",
			Title = "My user task",
			Description = "Default description",
			Culture = "en-US",
			TitleLocalizations = ["fr-FR=Tache utilisateur"],
			DescriptionLocalizations = ["fr-FR=Description FR"],
			Parameters = [
				"code=IsError;title=Is error;type=Boolean",
				"code=ResultMessage;title=Result message;type=Text;required=true;resulting=false;serializable=false"
			]
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(0, "because a workspace package and valid parameter definitions were provided");

		Received.InOrder(() => {
			applicationClient.ExecutePostRequest(
				CreateNewSchemaUrl,
				Arg.Is<string>(body => HasPropertyWithValue(body, "packageUId", packageUId.ToString())),
				100000,
				3,
				1);
			applicationClient.ExecutePostRequest(
				SaveSchemaUrl,
				Arg.Is<string>(body => MatchesSaveRequest(body, schemaUId, packageUId, editPageSchemaUId,
					dcmEditPageSchemaUId)),
				100000,
				3,
				1);
			applicationClient.ExecutePostRequest(
				BuildPackageUrl,
				Arg.Is<string>(body => HasPropertyWithValue(body, "packageName", "MyPackage")),
				100000,
				3,
				1);
		});
	}

	[Test]
	[Description("Returns an error when the requested package is not present in the current workspace.")]
	[Category("Unit")]
	public void Execute_Should_Return_Error_When_Package_Is_Not_In_Workspace() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		IJsonConverter jsonConverter = Substitute.For<IJsonConverter>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		EnvironmentSettings settings = new();
		string packagePath = @"C:\workspace\packages\Custom";
		string descriptorPath = $"{packagePath}\\descriptor.json";
		workspacePathBuilder.IsWorkspace.Returns(true);
		workspacePathBuilder.BuildPackagePath("Custom").Returns(packagePath);
		fileSystem.ExistsDirectory(packagePath).Returns(false);
		fileSystem.ExistsFile(descriptorPath).Returns(false);

		CreateUserTaskCommand command =
			new(applicationClient, settings, serviceUrlBuilder, workspacePathBuilder, jsonConverter, fileSystem);
		CreateUserTaskOptions options = new() {
			Package = "Custom",
			Code = "UsrMyUserTask",
			Title = "My user task"
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "because only workspace packages are allowed");
		applicationClient.DidNotReceiveWithAnyArgs()
			.ExecutePostRequest(default, default, default, default, default);
	}

	[Test]
	[Description("Returns an error when the command is executed outside a workspace directory.")]
	[Category("Unit")]
	public void Execute_Should_Return_Error_When_Current_Directory_Is_Not_A_Workspace() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		IJsonConverter jsonConverter = Substitute.For<IJsonConverter>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		EnvironmentSettings settings = new();
		workspacePathBuilder.IsWorkspace.Returns(false);

		CreateUserTaskCommand command =
			new(applicationClient, settings, serviceUrlBuilder, workspacePathBuilder, jsonConverter, fileSystem);
		CreateUserTaskOptions options = new() {
			Package = "MyPackage",
			Code = "UsrMyUserTask",
			Title = "My user task"
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "because the command resolves packages from the current workspace");
		applicationClient.DidNotReceiveWithAnyArgs()
			.ExecutePostRequest(default, default, default, default, default);
	}

	[Test]
	[Description("Returns an error when a parameter definition uses an unsupported type name.")]
	[Category("Unit")]
	public void Execute_Should_Return_Error_When_Parameter_Type_Is_Unsupported() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IWorkspacePathBuilder workspacePathBuilder = Substitute.For<IWorkspacePathBuilder>();
		IJsonConverter jsonConverter = Substitute.For<IJsonConverter>();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		EnvironmentSettings settings = new() {
			Uri = "https://localhost",
			IsNetCore = false
		};
		Guid packageUId = Guid.Parse("a00051f4-cde3-4f3f-b08e-c5ad1a5c735a");
		Guid schemaUId = Guid.Parse("8ab0dd66-18a8-4dc5-a452-74e0dc325bcf");
		Guid schemaId = Guid.Parse("937d9454-ef79-49e3-b098-4340bca01fd8");
		Guid editPageSchemaUId = Guid.Parse("c748bf4e-e4e1-454e-8f5b-4f65f91d8396");
		Guid dcmEditPageSchemaUId = Guid.Parse("d748bf4e-e4e1-454e-8f5b-4f65f91d8396");
		string packagePath = @"C:\workspace\packages\MyPackage";
		string descriptorPath = $"{packagePath}\\descriptor.json";
		serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.CreateUserTaskSchema)
			.Returns(CreateNewSchemaUrl);
		workspacePathBuilder.IsWorkspace.Returns(true);
		workspacePathBuilder.BuildPackagePath("MyPackage").Returns(packagePath);
		fileSystem.ExistsDirectory(packagePath).Returns(true);
		fileSystem.ExistsFile(descriptorPath).Returns(true);
		jsonConverter.DeserializeObjectFromFile<PackageDescriptorDto>(descriptorPath).Returns(new PackageDescriptorDto {
			Descriptor = new PackageDescriptor {
				Name = "MyPackage",
				UId = packageUId,
				Type = PackageType.Assembly
			}
		});
		applicationClient
			.ExecutePostRequest(CreateNewSchemaUrl, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(CreateNewSchemaResponse(schemaUId, schemaId, packageUId, editPageSchemaUId, dcmEditPageSchemaUId));

		CreateUserTaskCommand command =
			new(applicationClient, settings, serviceUrlBuilder, workspacePathBuilder, jsonConverter, fileSystem);
		CreateUserTaskOptions options = new() {
			Package = "MyPackage",
			Code = "UsrMyUserTask",
			Title = "My user task",
			Parameters = ["code=Broken;title=Broken parameter;type=Lookup"]
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "because only parameter types with verified designer payload mappings are supported");
		applicationClient.Received(1).ExecutePostRequest(
			CreateNewSchemaUrl,
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
		applicationClient.DidNotReceive().ExecutePostRequest(
			SaveSchemaUrl,
			Arg.Any<string>(),
			Arg.Any<int>(),
			Arg.Any<int>(),
			Arg.Any<int>());
	}

	private static string CreateNewSchemaResponse(Guid schemaUId, Guid schemaId, Guid packageUId,
		Guid editPageSchemaUId, Guid dcmEditPageSchemaUId) {
		var response = new {
			success = true,
			schema = new {
				uId = schemaUId,
				id = schemaId,
				name = "UsrProcessUserTask_8f7460c",
				body = "",
				metaData = "{}",
				isReadOnly = false,
				useFullHierarchy = false,
				userLevelSchema = false,
				addonTypes = (string[])null,
				caption = new[] {
					new { cultureName = "en-US", value = "ProcessUserTask_8f7460c" }
				},
				localizableStrings = Array.Empty<object>(),
				description = Array.Empty<object>(),
				parameters = Array.Empty<object>(),
				partial = false,
				userTask = true,
				customEventHandler = false,
				editPageSchemaUId,
				dcmEditPageSchemaUId,
				smallSvgImage = new { _isChanged = false },
				largeSvgImage = new { _isChanged = false },
				titleSvgImage = new { _isChanged = false },
				dcmSmallSvgImage = new { _isChanged = false },
				color = "#839DC3",
				serializeToDB = true,
				optionalProperties = Array.Empty<object>(),
				package = new { name = "Custom", uId = packageUId, type = 0 },
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

	private static bool MatchesSaveRequest(string json, Guid schemaUId, Guid packageUId, Guid editPageSchemaUId,
		Guid dcmEditPageSchemaUId) {
		using JsonDocument document = JsonDocument.Parse(json);
		JsonElement root = document.RootElement;

		string name = root.GetProperty("name").GetString();
		string title = root.GetProperty("caption")[0].GetProperty("value").GetString();
		string description = root.GetProperty("description")[0].GetProperty("value").GetString();
		string frTitle = root.GetProperty("caption")[1].GetProperty("value").GetString();
		string frDescription = root.GetProperty("description")[1].GetProperty("value").GetString();
		bool userTask = root.GetProperty("userTask").GetBoolean();
		bool serializeToDb = root.GetProperty("serializeToDB").GetBoolean();
		bool customEventHandler = root.GetProperty("customEventHandler").GetBoolean();
		string packageUIdValue = root.GetProperty("package").GetProperty("uId").GetString();
		int packageType = root.GetProperty("package").GetProperty("type").GetInt32();
		string editPageValue = root.GetProperty("editPageSchemaUId").GetString();
		string dcmEditPageValue = root.GetProperty("dcmEditPageSchemaUId").GetString();
		JsonElement parameters = root.GetProperty("parameters");
		JsonElement isErrorParameter = parameters[0];
		JsonElement resultMessageParameter = parameters[1];

		using JsonDocument metaData = JsonDocument.Parse(root.GetProperty("metaData").GetString());
		JsonElement schema = metaData.RootElement.GetProperty("metaData").GetProperty("schema");

		return name == "UsrMyUserTask"
			&& title == "My user task"
			&& description == "Default description"
			&& frTitle == "Tache utilisateur"
			&& frDescription == "Description FR"
			&& userTask
			&& serializeToDb
			&& !customEventHandler
			&& packageUIdValue == packageUId.ToString()
			&& packageType == 1
			&& editPageValue == editPageSchemaUId.ToString()
			&& dcmEditPageValue == dcmEditPageSchemaUId.ToString()
			&& parameters.GetArrayLength() == 2
			&& isErrorParameter.GetProperty("name").GetString() == "IsError"
			&& isErrorParameter.GetProperty("caption")[0].GetProperty("value").GetString() == "Is error"
			&& isErrorParameter.GetProperty("type").GetInt32() == 12
			&& isErrorParameter.GetProperty("resulting").GetBoolean()
			&& isErrorParameter.GetProperty("serializable").GetBoolean()
			&& isErrorParameter.GetProperty("icon").GetString() == "data-type-boolean-icon.svg"
			&& resultMessageParameter.GetProperty("name").GetString() == "ResultMessage"
			&& resultMessageParameter.GetProperty("caption")[0].GetProperty("value").GetString() == "Result message"
			&& resultMessageParameter.GetProperty("type").GetInt32() == 1
			&& resultMessageParameter.GetProperty("required").GetBoolean()
			&& !resultMessageParameter.GetProperty("resulting").GetBoolean()
			&& !resultMessageParameter.GetProperty("serializable").GetBoolean()
			&& resultMessageParameter.GetProperty("icon").GetString() == "data-type-text-icon.svg"
			&& schema.GetProperty("managerName").GetString() == "ProcessUserTaskSchemaManager"
			&& schema.GetProperty("uId").GetString() == schemaUId.ToString()
			&& schema.GetProperty("name").GetString() == "UsrMyUserTask"
			&& schema.GetProperty("packageUId").GetString() == packageUId.ToString()
			&& schema.GetProperty("color").GetString() == "#839DC3"
			&& schema.GetProperty("parameters").GetArrayLength() == 0;
	}
}
