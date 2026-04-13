using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using Clio.Tests.Infrastructure;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
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
		IFileDesignModePackages fileDesignModePackages = Substitute.For<IFileDesignModePackages>();
		IUserTaskMetadataDirectionApplier metadataDirectionApplier = Substitute.For<IUserTaskMetadataDirectionApplier>();
		IUserTaskLookupSchemaResolver lookupSchemaResolver = Substitute.For<IUserTaskLookupSchemaResolver>();
		EnvironmentSettings settings = new() {
			Uri = "https://localhost",
			IsNetCore = false
		};
		Guid packageUId = Guid.Parse("a00051f4-cde3-4f3f-b08e-c5ad1a5c735a");
		Guid schemaUId = Guid.Parse("8ab0dd66-18a8-4dc5-a452-74e0dc325bcf");
		Guid schemaId = Guid.Parse("937d9454-ef79-49e3-b098-4340bca01fd8");
		Guid editPageSchemaUId = Guid.Parse("c748bf4e-e4e1-454e-8f5b-4f65f91d8396");
		Guid dcmEditPageSchemaUId = Guid.Parse("d748bf4e-e4e1-454e-8f5b-4f65f91d8396");
		string packagePath = TestFileSystem.GetRootedPath("workspace", "packages", "MyPackage");
		string descriptorPath = Path.Combine(packagePath, "descriptor.json");
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
			new(applicationClient, settings, serviceUrlBuilder, workspacePathBuilder, jsonConverter, fileSystem,
				fileDesignModePackages, metadataDirectionApplier, lookupSchemaResolver);
		CreateUserTaskOptions options = new() {
			Package = "MyPackage",
			Code = "UsrMyUserTask",
			Title = "My user task",
			Description = "Default description",
			Culture = "en-US",
			TitleLocalizations = ["fr-FR=Tache utilisateur"],
			DescriptionLocalizations = ["fr-FR=Description FR"],
			Parameters = [
				"code=IsError;title=Is error;type=Boolean;direction=Out",
				"code=ResultMessage;title=Result message;type=Text;required=true;resulting=false;serializable=false",
				"code=MyList;title=My list;type=Serializable list of composite values"
			],
			ParameterItems = [
				"parent=MyList;code=Bool1;title=Bool1;type=Boolean"
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
			applicationClient.ExecutePostRequest(
				BuildPackageUrl,
				Arg.Is<string>(body => HasPropertyWithValue(body, "packageName", "MyPackage")),
				100000,
				3,
				1);
		});
		metadataDirectionApplier.Received(1).ApplyDirections(
			"MyPackage",
			"UsrMyUserTask",
			Arg.Is<IReadOnlyDictionary<string, int>>(directions =>
				directions.Count == 1 && directions["IsError"] == 1));
		fileDesignModePackages.Received(1).LoadPackagesToDb();
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
		IFileDesignModePackages fileDesignModePackages = Substitute.For<IFileDesignModePackages>();
		IUserTaskMetadataDirectionApplier metadataDirectionApplier = Substitute.For<IUserTaskMetadataDirectionApplier>();
		IUserTaskLookupSchemaResolver lookupSchemaResolver = Substitute.For<IUserTaskLookupSchemaResolver>();
		EnvironmentSettings settings = new();
		string packagePath = TestFileSystem.GetRootedPath("workspace", "packages", "Custom");
		string descriptorPath = Path.Combine(packagePath, "descriptor.json");
		workspacePathBuilder.IsWorkspace.Returns(true);
		workspacePathBuilder.BuildPackagePath("Custom").Returns(packagePath);
		fileSystem.ExistsDirectory(packagePath).Returns(false);
		fileSystem.ExistsFile(descriptorPath).Returns(false);

		CreateUserTaskCommand command =
			new(applicationClient, settings, serviceUrlBuilder, workspacePathBuilder, jsonConverter, fileSystem,
				fileDesignModePackages, metadataDirectionApplier, lookupSchemaResolver);
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
		IFileDesignModePackages fileDesignModePackages = Substitute.For<IFileDesignModePackages>();
		IUserTaskMetadataDirectionApplier metadataDirectionApplier = Substitute.For<IUserTaskMetadataDirectionApplier>();
		IUserTaskLookupSchemaResolver lookupSchemaResolver = Substitute.For<IUserTaskLookupSchemaResolver>();
		EnvironmentSettings settings = new();
		workspacePathBuilder.IsWorkspace.Returns(false);

		CreateUserTaskCommand command =
			new(applicationClient, settings, serviceUrlBuilder, workspacePathBuilder, jsonConverter, fileSystem,
				fileDesignModePackages, metadataDirectionApplier, lookupSchemaResolver);
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
	[Description("Returns an error when the user task code does not start with the required Usr prefix.")]
	[Category("Unit")]
	public void Execute_Should_Return_Error_When_Code_Does_Not_Start_With_Usr() {
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
		Guid packageUId = Guid.Parse("a00051f4-cde3-4f3f-b08e-c5ad1a5c735a");
		string packagePath = TestFileSystem.GetRootedPath("workspace", "packages", "MyPackage");
		string descriptorPath = Path.Combine(packagePath, "descriptor.json");
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

		CreateUserTaskCommand command =
			new(applicationClient, settings, serviceUrlBuilder, workspacePathBuilder, jsonConverter, fileSystem,
				fileDesignModePackages, metadataDirectionApplier, lookupSchemaResolver);
		CreateUserTaskOptions options = new() {
			Package = "MyPackage",
			Code = "MyFinalTask",
			Title = "My final task"
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1);
		applicationClient.DidNotReceiveWithAnyArgs()
			.ExecutePostRequest(default, default, default, default, default);
	}

	[Test]
	[Description("Builds a lookup parameter by resolving the requested entity schema and serializing the designer lookup payload shape.")]
	[Category("Unit")]
	public void BuildParameters_Should_Create_Lookup_Parameter_With_Resolved_Schema() {
		// Arrange
		Guid accountSchemaUId = Guid.Parse("25d7c1ab-1de0-4501-b402-02e0e5a72d6e");
		UserTaskLookupSchema resolvedLookup = new() {
			Name = "Account",
			UId = accountSchemaUId,
			Caption = "Account"
		};

		// Act
		List<UserTaskParameterDto> parameters = UserTaskSchemaSupport.BuildParameters(
			"en-US",
			["code=AccountRef;title=Account reference;type=Lookup;lookup=Account"],
			lookupValue => {
				lookupValue.Should().Be("Account");
				return resolvedLookup;
			});

		// Assert
		parameters.Should().HaveCount(1);
		UserTaskParameterDto parameter = parameters[0];
		parameter.Name.Should().Be("AccountRef");
		parameter.Caption.Should().ContainSingle();
		parameter.Caption[0].Value.Should().Be("Account reference");
		parameter.Type.Should().Be(10);
		parameter.ReferenceSchemaUId.Should().Be(accountSchemaUId);
		parameter.ReferenceSchemaName.Should().BeNull();
		parameter.Icon.Should().Be("data-type-lookup-icon.svg");
		parameter.Direction.Should().Be(2, "because new parameters default to Variable unless direction is specified");
	}

	[Test]
	[Description("Accepts the designer label Unique identifier as an alias for Guid.")]
	[Category("Unit")]
	public void BuildParameters_Should_Accept_UniqueIdentifier_Alias() {
		// Act
		List<UserTaskParameterDto> parameters = UserTaskSchemaSupport.BuildParameters(
			"en-US",
			["code=ForeignId;title=Foreign ID;type=Unique identifier"]);

		// Assert
		parameters.Should().HaveCount(1);
		parameters[0].Name.Should().Be("ForeignId");
		parameters[0].Type.Should().Be(11);
		parameters[0].Icon.Should().Be("data-type-guid-icon.svg");
	}

	[Test]
	[Description("Attaches child item properties to a serializable composite list parameter.")]
	[Category("Unit")]
	public void AttachParameterItems_Should_Add_ItemProperties_To_Composite_List_Parameter() {
		// Arrange
		List<UserTaskParameterDto> parameters = UserTaskSchemaSupport.BuildParameters(
			"en-US",
			["code=MyList;title=My list;type=Serializable list of composite values"]);
		List<UserTaskParameterItemDefinition> parameterItems = UserTaskSchemaSupport.BuildParameterItems(
			"en-US",
			["parent=MyList;code=Bool1;title=Bool1;type=Boolean"]);

		// Act
		UserTaskSchemaSupport.AttachParameterItems(parameters, parameterItems);

		// Assert
		parameters.Should().HaveCount(1);
		parameters[0].Type.Should().Be(39, "because the parent parameter is a serializable composite list");
		parameters[0].ItemProperties.Should().ContainSingle();
		parameters[0].ItemProperties[0].Name.Should().Be("Bool1");
		parameters[0].ItemProperties[0].Type.Should().Be(12);
		parameters[0].ItemProperties[0].Icon.Should().Be("data-type-boolean-icon.svg");
	}

	[Test]
	[Description("Serializes composite list Unique identifier items with the same type code used by the designer SaveSchema payload.")]
	[Category("Unit")]
	public void BuildParameterItems_Should_Create_UniqueIdentifier_Item_For_Composite_List() {
		// Act
		List<UserTaskParameterItemDefinition> parameterItems = UserTaskSchemaSupport.BuildParameterItems(
			"en-US",
			["parent=MyList;code=ForeignId;title=Foreign ID;type=Unique identifier"]);

		// Assert
		parameterItems.Should().HaveCount(1);
		parameterItems[0].ParentParameterName.Should().Be("MyList");
		parameterItems[0].Parameter.Name.Should().Be("ForeignId");
		parameterItems[0].Parameter.Type.Should().Be(0, "because the live designer posts composite-list Unique identifier items with type=0");
		parameterItems[0].Parameter.Icon.Should().Be("data-type-guid-icon.svg");
	}

	[Test]
	[Description("Returns an error when a lookup parameter definition does not include a lookup schema reference.")]
	[Category("Unit")]
	public void Execute_Should_Return_Error_When_Lookup_Parameter_Does_Not_Include_Lookup_Value() {
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
		Guid packageUId = Guid.Parse("a00051f4-cde3-4f3f-b08e-c5ad1a5c735a");
		Guid schemaUId = Guid.Parse("8ab0dd66-18a8-4dc5-a452-74e0dc325bcf");
		Guid schemaId = Guid.Parse("937d9454-ef79-49e3-b098-4340bca01fd8");
		Guid editPageSchemaUId = Guid.Parse("c748bf4e-e4e1-454e-8f5b-4f65f91d8396");
		Guid dcmEditPageSchemaUId = Guid.Parse("d748bf4e-e4e1-454e-8f5b-4f65f91d8396");
		string packagePath = TestFileSystem.GetRootedPath("workspace", "packages", "MyPackage");
		string descriptorPath = Path.Combine(packagePath, "descriptor.json");
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
			new(applicationClient, settings, serviceUrlBuilder, workspacePathBuilder, jsonConverter, fileSystem,
				fileDesignModePackages, metadataDirectionApplier, lookupSchemaResolver);
		CreateUserTaskOptions options = new() {
			Package = "MyPackage",
			Code = "UsrMyUserTask",
			Title = "My user task",
			Parameters = ["code=Broken;title=Broken parameter;type=Lookup"]
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "because lookup parameters need an entity schema name or schema UId");
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

	[Test]
	[Description("Returns an error when a parameter definition uses an unsupported direction value.")]
	[Category("Unit")]
	public void Execute_Should_Return_Error_When_Parameter_Direction_Is_Unsupported() {
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
		Guid packageUId = Guid.Parse("a00051f4-cde3-4f3f-b08e-c5ad1a5c735a");
		string packagePath = TestFileSystem.GetRootedPath("workspace", "packages", "MyPackage");
		string descriptorPath = Path.Combine(packagePath, "descriptor.json");
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
		CreateUserTaskCommand command =
			new(applicationClient, settings, serviceUrlBuilder, workspacePathBuilder, jsonConverter, fileSystem,
				fileDesignModePackages, metadataDirectionApplier, lookupSchemaResolver);
		CreateUserTaskOptions options = new() {
			Package = "MyPackage",
			Code = "UsrMyUserTask",
			Title = "My user task",
			Parameters = ["code=Broken;title=Broken parameter;type=Boolean;direction=Sideways"]
		};

		// Act
		int result = command.Execute(options);

		// Assert
		result.Should().Be(1, "because only verified direction values should be accepted");
		applicationClient.DidNotReceiveWithAnyArgs()
			.ExecutePostRequest(default, default, default, default, default);
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
		JsonElement myListParameter = parameters[2];
		JsonElement bool1Item = myListParameter.GetProperty("itemProperties")[0];

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
			&& parameters.GetArrayLength() == 3
			&& isErrorParameter.GetProperty("name").GetString() == "IsError"
			&& isErrorParameter.GetProperty("caption")[0].GetProperty("value").GetString() == "Is error"
			&& isErrorParameter.GetProperty("type").GetInt32() == 12
			&& isErrorParameter.GetProperty("direction").GetInt32() == 1
			&& isErrorParameter.GetProperty("resulting").GetBoolean()
			&& isErrorParameter.GetProperty("serializable").GetBoolean()
			&& isErrorParameter.GetProperty("icon").GetString() == "data-type-boolean-icon.svg"
			&& resultMessageParameter.GetProperty("name").GetString() == "ResultMessage"
			&& resultMessageParameter.GetProperty("caption")[0].GetProperty("value").GetString() == "Result message"
			&& resultMessageParameter.GetProperty("type").GetInt32() == 1
			&& resultMessageParameter.GetProperty("direction").GetInt32() == 2
			&& resultMessageParameter.GetProperty("required").GetBoolean()
			&& !resultMessageParameter.GetProperty("resulting").GetBoolean()
			&& !resultMessageParameter.GetProperty("serializable").GetBoolean()
			&& resultMessageParameter.GetProperty("icon").GetString() == "data-type-text-icon.svg"
			&& myListParameter.GetProperty("name").GetString() == "MyList"
			&& myListParameter.GetProperty("caption")[0].GetProperty("value").GetString() == "My list"
			&& myListParameter.GetProperty("type").GetInt32() == 39
			&& myListParameter.GetProperty("icon").GetString() == "data-type-other-icon.svg"
			&& myListParameter.GetProperty("itemProperties").GetArrayLength() == 1
			&& bool1Item.GetProperty("name").GetString() == "Bool1"
			&& bool1Item.GetProperty("caption")[0].GetProperty("value").GetString() == "Bool1"
			&& bool1Item.GetProperty("type").GetInt32() == 12
			&& bool1Item.GetProperty("icon").GetString() == "data-type-boolean-icon.svg"
			&& schema.GetProperty("managerName").GetString() == "ProcessUserTaskSchemaManager"
			&& schema.GetProperty("uId").GetString() == schemaUId.ToString()
			&& schema.GetProperty("name").GetString() == "UsrMyUserTask"
			&& schema.GetProperty("packageUId").GetString() == packageUId.ToString()
			&& schema.GetProperty("color").GetString() == "#839DC3"
			&& schema.GetProperty("parameters").GetArrayLength() == 0;
	}

}
