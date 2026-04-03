using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using ATF.Repository.Providers;
using Clio.Command;
using Clio.Command.PackageCommand;
using Clio.Command.McpServer.Prompts;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;
using Clio.Package;
using Clio.UserEnvironment;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public sealed class ApplicationToolTests {
	[Test]
	[Category("Unit")]
	[Description("Advertises the stable MCP tool name for application-get-list so callers and tests share the same production identifier.")]
	public void ApplicationGetList_Should_Advertise_Stable_Tool_Name() {
		// Arrange
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ApplicationGetListTool)
			.GetMethod(nameof(ApplicationGetListTool.ApplicationGetList))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Act
		string toolName = attribute.Name;

		// Assert
		toolName.Should().Be(ApplicationGetListTool.ApplicationGetListToolName,
			because: "the MCP tool name must stay centralized on the production tool type");
	}

	[Test]
	[Category("Unit")]
	[Description("Calls the list service without application filters and returns a success envelope that preserves the service order.")]
	public void ApplicationGetList_Should_Call_List_Service_Without_Filters_And_Return_Success_Envelope() {
		// Arrange
		IApplicationListService applicationListService = Substitute.For<IApplicationListService>();
		IReadOnlyList<InstalledApplicationListItem> installedApplications = [
			new InstalledApplicationListItem(Guid.NewGuid(), "Beta", "BETA", "2.0.0", "Beta description"),
			new InstalledApplicationListItem(Guid.NewGuid(), "Alpha", "ALPHA", "1.0.0", "Alpha description")
		];
		applicationListService.GetApplications("sandbox", null, null).Returns([.. installedApplications]);
		ApplicationGetListTool tool = new(applicationListService);

		// Act
		ApplicationListResponse result = tool.ApplicationGetList(new ApplicationGetListArgs(
			EnvironmentName: "sandbox"));

		// Assert
		applicationListService.Received(1).GetApplications("sandbox", null, null);
		result.Success.Should().BeTrue(
			because: "a successful list call should be wrapped in a core-style success envelope");
		result.Error.Should().BeNull(
			because: "successful list calls should not include an error payload");
		result.Applications.Should().NotBeNull(
			because: "successful list calls should include the application collection");
		result.Applications!.Select(item => item.Name).Should().Equal(new[] { "Beta", "Alpha" },
			because: "the MCP wrapper should preserve the structured order produced by the application list service");
		result.Applications[0].Code.Should().Be("BETA",
			because: "the MCP tool should preserve the installed application code");
		result.Applications[0].Version.Should().Be("2.0.0",
			because: "the MCP tool should preserve the installed application version");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns an empty success envelope when the installed-application query matches no applications.")]
	public void ApplicationGetList_Should_Return_Empty_Success_Envelope_When_No_Applications_Exist() {
		// Arrange
		IApplicationListService applicationListService = Substitute.For<IApplicationListService>();
		applicationListService.GetApplications("sandbox", null, null).Returns([]);
		ApplicationGetListTool tool = new(applicationListService);

		// Act
		ApplicationListResponse result = tool.ApplicationGetList(new ApplicationGetListArgs(
			EnvironmentName: "sandbox"));

		// Assert
		result.Success.Should().BeTrue(
			because: "zero application matches are still a successful list operation");
		result.Applications.Should().BeEmpty(
			because: "the MCP tool should return an empty structured list instead of treating zero matches as an error");
		result.Error.Should().BeNull(
			because: "successful empty list calls should not report an error");
	}

	[Test]
	[Category("Unit")]
	[Description("Wraps environment resolution failures from application-get-list in a structured error envelope.")]
	public void ApplicationGetList_Should_Return_Error_Envelope_When_Environment_Resolution_Fails() {
		// Arrange
		IApplicationListService applicationListService = Substitute.For<IApplicationListService>();
		applicationListService.GetApplications("missing-env", null, null)
			.Returns(_ => throw new InvalidOperationException("Environment with key 'missing-env' not found."));
		ApplicationGetListTool tool = new(applicationListService);

		// Act
		ApplicationListResponse result = tool.ApplicationGetList(new ApplicationGetListArgs(
			EnvironmentName: "missing-env"));

		// Assert
		result.Success.Should().BeFalse(
			because: "tool failures should now be expressed as structured error payloads");
		result.Applications.Should().BeNull(
			because: "error envelopes should not include a success result collection");
		result.Error.Should().Match("*missing-env*",
			because: "read-style tool failures should still identify the missing environment");
		result.Error.Should().Match("*not found*",
			because: "read-style tool failures should still include readable diagnostics");
	}

	[Test]
	[Category("Unit")]
	[Description("Advertises the stable MCP tool name for application-get-info so callers and tests share the same production identifier.")]
	public void ApplicationGetInfo_Should_Advertise_Stable_Tool_Name() {
		// Arrange
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ApplicationGetInfoTool)
			.GetMethod(nameof(ApplicationGetInfoTool.ApplicationGetInfo))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Act
		string toolName = attribute.Name;

		// Assert
		toolName.Should().Be(ApplicationGetInfoTool.ApplicationGetInfoToolName,
			because: "the MCP tool name must stay centralized on the production tool type");
	}

	[Test]
	[Category("Unit")]
	[Description("Advertises the stable MCP tool name for application-create so callers and tests share the same production identifier.")]
	public void ApplicationCreate_Should_Advertise_Stable_Tool_Name() {
		// Arrange
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ApplicationCreateTool)
			.GetMethod(nameof(ApplicationCreateTool.ApplicationCreate))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Act
		string toolName = attribute.Name;

		// Assert
		toolName.Should().Be(ApplicationCreateTool.ApplicationCreateToolName,
			because: "the MCP tool name must stay centralized on the production tool type");
	}

	[Test]
	[Category("Unit")]
	[Description("Calls the application info service with exactly one MCP identifier and returns the expected success envelope.")]
	public void ApplicationGetInfo_Should_Return_Structured_Success_Envelope() {
		// Arrange
		IApplicationInfoService applicationInfoService = Substitute.For<IApplicationInfoService>();
		applicationInfoService.GetApplicationInfo("sandbox", "app-id", null).Returns(
			new ApplicationInfoResult(
				"pkg-uid",
				"UsrVehicle",
				[
					new ApplicationEntityInfoResult(
						"entity-uid",
						"UsrVehicle",
						"Entity",
						[
							new ApplicationColumnInfoResult(
								"Name",
								"Name",
								"Text",
								null,
								"Const",
								"Default")
						])
				],
				[
					new PageListItem {
						SchemaName = "UsrVehicle_FormPage",
						UId = "page-uid",
						PackageName = "UsrVehicle",
						ParentSchemaName = "BasePage"
					}
				],
				ApplicationId: "app-id",
				ApplicationName: "Vehicle App",
				ApplicationCode: "UsrVehicleApp",
				ApplicationVersion: "8.3.0"));
		ApplicationGetInfoTool tool = new(applicationInfoService);

		// Act
		ApplicationContextResponse result = tool.ApplicationGetInfo(new ApplicationGetInfoArgs(
			EnvironmentName: "sandbox",
			Id: "app-id",
			Code: null));

		// Assert
		applicationInfoService.Received(1).GetApplicationInfo("sandbox", "app-id", null);
		result.Success.Should().BeTrue(
			because: "a successful info call should be wrapped in a core-style success envelope");
		result.Error.Should().BeNull(
			because: "successful info calls should not include an error payload");
		result.PackageUId.Should().Be("pkg-uid",
			because: "the MCP tool should preserve the primary package identifier");
		result.PackageName.Should().Be("UsrVehicle",
			because: "the MCP tool should preserve the primary package name");
		result.CanonicalMainEntityName.Should().Be("UsrVehicle",
			because: "the MCP tool should expose the canonical main entity when it matches the primary package");
		result.ApplicationId.Should().Be("app-id",
			because: "the MCP tool should preserve the installed application identifier for follow-up discovery and navigation");
		result.ApplicationName.Should().Be("Vehicle App",
			because: "the MCP tool should preserve the installed application display name");
		result.ApplicationCode.Should().Be("UsrVehicleApp",
			because: "the MCP tool should preserve the installed application code");
		result.ApplicationVersion.Should().Be("8.3.0",
			because: "the MCP tool should preserve the installed application version");
		result.Entities.Should().ContainSingle(
			because: "the MCP tool should surface the entity metadata returned by the backend service");
		result.Entities![0].Columns[0].DataValueType.Should().Be("Text",
			because: "the Clio response should preserve application column types");
		result.Pages.Should().ContainSingle(
			because: "application-get-info should now return the primary-package page summaries");
		result.Pages![0].SchemaName.Should().Be("UsrVehicle_FormPage",
			because: "application-get-info should expose page identities through schema-name");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured error envelope when get-info omits both installed-application identifiers.")]
	public void ApplicationGetInfo_Should_Return_Error_When_Identifiers_Are_Missing() {
		// Arrange
		IApplicationInfoService applicationInfoService = Substitute.For<IApplicationInfoService>();
		ApplicationGetInfoTool tool = new(applicationInfoService);

		// Act
		ApplicationContextResponse result = tool.ApplicationGetInfo(new ApplicationGetInfoArgs(
			EnvironmentName: "sandbox",
			Id: null,
			Code: null));

		// Assert
		result.Success.Should().BeFalse(
			because: "tool validation failures should now be returned as structured error payloads");
		result.Error.Should().Match("*exactly one*",
			because: "the MCP tool contract should follow the core rule that exactly one identifier is required");
		result.Error.Should().Match("*id*",
			because: "the MCP tool contract should mention the supported identifier names");
		result.Error.Should().Match("*code*",
			because: "the MCP tool contract should mention the supported identifier names");
		applicationInfoService.DidNotReceiveWithAnyArgs().GetApplicationInfo(default!, default, default);
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured error envelope when get-info provides both installed-application identifiers.")]
	public void ApplicationGetInfo_Should_Return_Error_When_Both_Identifiers_Are_Provided() {
		// Arrange
		IApplicationInfoService applicationInfoService = Substitute.For<IApplicationInfoService>();
		ApplicationGetInfoTool tool = new(applicationInfoService);

		// Act
		ApplicationContextResponse result = tool.ApplicationGetInfo(new ApplicationGetInfoArgs(
			EnvironmentName: "sandbox",
			Id: "app-id",
			Code: "APP"));

		// Assert
		result.Success.Should().BeFalse(
			because: "tool validation failures should now be returned as structured error payloads");
		result.Error.Should().Match("*exactly one*",
			because: "the MCP tool contract should reject ambiguous get-info requests that pass both identifiers");
		applicationInfoService.DidNotReceiveWithAnyArgs().GetApplicationInfo(default!, default, default);
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured error envelope when the backend info lookup fails.")]
	public void ApplicationGetInfo_Should_Return_Error_When_Backend_Fails() {
		// Arrange
		IApplicationInfoService applicationInfoService = Substitute.For<IApplicationInfoService>();
		applicationInfoService.GetApplicationInfo("sandbox", null, "missing-app")
			.Returns(_ => throw new InvalidOperationException("Application 'missing-app' not found."));
		ApplicationGetInfoTool tool = new(applicationInfoService);

		// Act
		ApplicationContextResponse result = tool.ApplicationGetInfo(new ApplicationGetInfoArgs(
			EnvironmentName: "sandbox",
			Id: null,
			Code: "missing-app"));

		// Assert
		result.Success.Should().BeFalse(
			because: "backend failures should now be returned as structured error payloads");
		result.Error.Should().Match("*missing-app*",
			because: "error envelopes should preserve the backend diagnostics");
		result.Error.Should().Match("*not found*",
			because: "error envelopes should remain readable for MCP clients");
	}

	[Test]
	[Category("Unit")]
	[Description("Calls the application create service with the core-aligned required arguments and parsed optional-template-data-json.")]
	public void ApplicationCreate_Should_Return_Structured_Success_Envelope() {
		// Arrange
		IApplicationCreateService applicationCreateService = Substitute.For<IApplicationCreateService>();
		applicationCreateService.CreateApplication(
				"sandbox",
				Arg.Any<ApplicationCreateRequest>())
			.Returns(new ApplicationInfoResult(
				"pkg-uid",
				"UsrCodexApp",
				[
					new ApplicationEntityInfoResult(
						"entity-uid",
						"UsrCodexApp",
						"Entity",
						[
							new ApplicationColumnInfoResult(
								"Name",
								"Name",
								"Text",
								null,
								"Const",
								"Default")
						])
				],
				[
					new PageListItem {
						SchemaName = "UsrCodexApp_FormPage",
						UId = "page-uid",
						PackageName = "UsrCodexApp",
						ParentSchemaName = "BasePage"
					}
				],
				ApplicationId: "created-app-id",
				ApplicationName: "Codex App",
				ApplicationCode: "UsrCodexApp",
				ApplicationVersion: "1.0.0"));
		ApplicationCreateTool tool = new(applicationCreateService);

		// Act
		ApplicationContextResponse result = tool.ApplicationCreate(new ApplicationCreateArgs(
			EnvironmentName: "sandbox",
			Name: "Codex App",
			Code: "UsrCodexApp",
			Description: null,
			TemplateCode: "AppFreedomUI",
			IconId: "auto",
			IconBackground: "#112233",
			ClientTypeId: "22222222-2222-2222-2222-222222222222",
			OptionalTemplateDataJson: """
				{"entitySchemaName":"UsrCodexEntity","useExistingEntitySchema":true,"useAIContentGeneration":false,"appSectionDescription":"Section description"}
				"""));

		// Assert
		applicationCreateService.Received(1).CreateApplication(
			"sandbox",
			Arg.Is<ApplicationCreateRequest>(request =>
				request.Name == "Codex App" &&
				request.Code == "UsrCodexApp" &&
				request.Description == null &&
				request.TemplateCode == "AppFreedomUI" &&
				request.IconId == "auto" &&
				request.IconBackground == "#112233" &&
				request.ClientTypeId == "22222222-2222-2222-2222-222222222222" &&
				request.OptionalTemplateData != null &&
				request.OptionalTemplateData.EntitySchemaName == "UsrCodexEntity" &&
				request.OptionalTemplateData.UseExistingEntitySchema == true &&
				request.OptionalTemplateData.UseAiContentGeneration == false &&
				request.OptionalTemplateData.AppSectionDescription == "Section description"));
		result.Success.Should().BeTrue(
			because: "successful create calls should be wrapped in a core-style success envelope");
		result.Error.Should().BeNull(
			because: "successful create calls should not include an error payload");
		result.PackageUId.Should().Be("pkg-uid",
			because: "the create MCP tool should preserve the primary package identifier from the backend service");
		result.CanonicalMainEntityName.Should().Be("UsrCodexApp",
			because: "the create MCP tool should expose the canonical main entity when the created app returns one");
		result.ApplicationId.Should().Be("created-app-id",
			because: "application-create should return the installed application identifier in the same envelope shape as application-get-info");
		result.ApplicationCode.Should().Be("UsrCodexApp",
			because: "application-create should return the created application code in the structured envelope");
		result.Entities![0].Columns[0].DataValueType.Should().Be("Text",
			because: "the create MCP tool should preserve Clio-style column metadata");
		result.Pages.Should().ContainSingle(
			because: "application-create should return the same primary-package page summaries as application-get-info");
		result.Pages![0].SchemaName.Should().Be("UsrCodexApp_FormPage");
	}

	[Test]
	[Category("Unit")]
	[Description("Serializes the application context response using Clio kebab-case field names.")]
	public void ApplicationContextResponse_Should_Serialize_Using_Clio_Field_Names() {
		// Arrange
		ApplicationContextResponse response = new(
			Success: true,
			PackageUId: "pkg-uid",
			PackageName: "Pkg",
			CanonicalMainEntityName: "UsrEntity",
			ApplicationId: "app-id",
			ApplicationName: "Codex App",
			ApplicationCode: "UsrCodexApp",
			ApplicationVersion: "8.3.0",
			Entities: [
				new ApplicationEntityResult(
					UId: "entity-uid",
					Name: "UsrEntity",
					Caption: "Entity",
					Columns: [
						new ApplicationColumnResult(
							Name: "Name",
							Caption: "Name",
							DataValueType: "Text",
							ReferenceSchema: "Contact")
					])
			],
			Pages: [
				new PageListItem {
					SchemaName = "UsrEntity_FormPage",
					UId = "page-uid",
					PackageName = "Pkg",
					ParentSchemaName = "BasePage"
				}
			]);

		// Act
		string json = JsonSerializer.Serialize(response);

		// Assert
		json.Should().Contain("\"package-u-id\":\"pkg-uid\"",
			because: "application context responses should keep Clio kebab-case payload fields");
		json.Should().Contain("\"package-name\":\"Pkg\"",
			because: "application context responses should keep Clio kebab-case payload fields");
		json.Should().Contain("\"canonical-main-entity-name\":\"UsrEntity\"",
			because: "application context responses should expose the canonical main entity when it is known");
		json.Should().Contain("\"application-id\":\"app-id\"",
			because: "application context responses should preserve the installed application identifier");
		json.Should().Contain("\"application-name\":\"Codex App\"",
			because: "application context responses should preserve the installed application display name");
		json.Should().Contain("\"application-code\":\"UsrCodexApp\"",
			because: "application context responses should preserve the installed application code");
		json.Should().Contain("\"application-version\":\"8.3.0\"",
			because: "application context responses should preserve the installed application version");
		json.Should().Contain("\"pages\"",
			because: "application context responses should now surface primary-package page summaries");
		json.Should().Contain("\"schema-name\":\"UsrEntity_FormPage\"",
			because: "application page payloads should use schema-name instead of name");
		json.Should().Contain("\"u-id\":\"entity-uid\"",
			because: "entity payloads should keep Clio kebab-case payload fields");
		json.Should().Contain("\"data-value-type\":\"Text\"",
			because: "column payloads should keep Clio kebab-case payload fields");
		json.Should().Contain("\"reference-schema\":\"Contact\"",
			because: "lookup payloads should keep Clio kebab-case payload fields");
	}

	[Test]
	[Category("Unit")]
	[Description("Serializes the application list response using the Clio envelope shape.")]
	public void ApplicationListResponse_Should_Serialize_Using_Clio_Field_Names() {
		// Arrange
		ApplicationListResponse response = new(
			Success: true,
			Applications: [
				new ApplicationListItemResult(
					Id: "app-id",
					Name: "Codex App",
					Code: "UsrCodexApp",
					Version: "1.0.0")
			]);

		// Act
		string json = JsonSerializer.Serialize(response);

		// Assert
		json.Should().Contain("\"success\":true",
			because: "list responses should still use the structured success envelope");
		json.Should().Contain("\"applications\"",
			because: "list responses should expose the Clio list collection field");
		json.Should().Contain("\"id\":\"app-id\"",
			because: "application list items should preserve their identifiers in the serialized payload");
	}

	[Test]
	[Category("Unit")]
	[Description("Declares the core-aligned required attributes for the application-create fields while keeping description optional.")]
	public void ApplicationCreateArgs_Should_Mark_Core_Required_Fields_As_Required() {
		// Arrange
		Type argsType = typeof(ApplicationCreateArgs);
		string[] requiredProperties = [
			nameof(ApplicationCreateArgs.EnvironmentName),
			nameof(ApplicationCreateArgs.Name),
			nameof(ApplicationCreateArgs.Code),
			nameof(ApplicationCreateArgs.TemplateCode),
			nameof(ApplicationCreateArgs.IconBackground)
		];
		string[] optionalProperties = [
			nameof(ApplicationCreateArgs.IconId),
			nameof(ApplicationCreateArgs.ClientTypeId),
			nameof(ApplicationCreateArgs.Description),
			nameof(ApplicationCreateArgs.OptionalTemplateDataJson)
		];

		// Act
		string[] propertiesWithoutRequired = requiredProperties
			.Where(propertyName => argsType.GetProperty(propertyName)!
				.GetCustomAttributes(typeof(RequiredAttribute), false)
				.Length == 0)
			.ToArray();
		string[] propertiesStillRequired = optionalProperties
			.Where(propertyName => argsType.GetProperty(propertyName)!
				.GetCustomAttributes(typeof(RequiredAttribute), false)
				.Length > 0)
			.ToArray();

		// Assert
		propertiesWithoutRequired.Should().BeEmpty(
			because: "the MCP server should see the core-required create fields as required");
		propertiesStillRequired.Should().BeEmpty(
			because: "description and optional extension fields should remain optional in the MCP contract");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured error envelope when optional-template-data-json is invalid.")]
	public void ApplicationCreate_Should_Return_Error_When_OptionalTemplateDataJson_Is_Invalid() {
		// Arrange
		IApplicationCreateService applicationCreateService = Substitute.For<IApplicationCreateService>();
		ApplicationCreateTool tool = new(applicationCreateService);

		// Act
		ApplicationContextResponse result = tool.ApplicationCreate(new ApplicationCreateArgs(
			EnvironmentName: "sandbox",
			Name: "Codex App",
			Code: "UsrCodexApp",
			Description: null,
			TemplateCode: "AppFreedomUI",
			IconId: null,
			IconBackground: "#112233",
			ClientTypeId: null,
			OptionalTemplateDataJson: "{not-json"));

		// Assert
		result.Success.Should().BeFalse(
			because: "validation failures should now be returned as structured error payloads");
		result.Error.Should().Match("*optional-template-data-json*",
			because: "the create tool should reject malformed template data before calling the backend service");
		applicationCreateService.DidNotReceiveWithAnyArgs().CreateApplication(default!, default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured error envelope when optional-template-data-json requests AI content generation.")]
	public void ApplicationCreate_Should_Return_Error_When_OptionalTemplateDataJson_Requests_AiContentGeneration() {
		// Arrange
		IApplicationCreateService applicationCreateService = Substitute.For<IApplicationCreateService>();
		ApplicationCreateTool tool = new(applicationCreateService);

		// Act
		ApplicationContextResponse result = tool.ApplicationCreate(new ApplicationCreateArgs(
			EnvironmentName: "sandbox",
			Name: "Codex App",
			Code: "UsrCodexApp",
			Description: null,
			TemplateCode: "AppFreedomUI",
			IconId: null,
			IconBackground: "#112233",
			ClientTypeId: null,
			OptionalTemplateDataJson: JsonSerializer.Serialize(new {
				useAIContentGeneration = true
			})));

		// Assert
		result.Success.Should().BeFalse(
			because: "validation failures should now be returned as structured error payloads");
		result.Error.Should().Match("*useAiContentGeneration=true*",
			because: "the create tool should match the core behavior that rejects AI-generated template content");
		applicationCreateService.DidNotReceiveWithAnyArgs().CreateApplication(default!, default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured error envelope when the backend create flow fails.")]
	public void ApplicationCreate_Should_Return_Error_When_Backend_Fails() {
		// Arrange
		IApplicationCreateService applicationCreateService = Substitute.For<IApplicationCreateService>();
		applicationCreateService.CreateApplication("sandbox", Arg.Any<ApplicationCreateRequest>())
			.Returns(_ => throw new InvalidOperationException("Template dependency failed."));
		ApplicationCreateTool tool = new(applicationCreateService);

		// Act
		ApplicationContextResponse result = tool.ApplicationCreate(new ApplicationCreateArgs(
			EnvironmentName: "sandbox",
			Name: "Codex App",
			Code: "UsrCodexApp",
			Description: null,
			TemplateCode: "AppFreedomUI",
			IconId: null,
			IconBackground: "#112233",
			ClientTypeId: null,
			OptionalTemplateDataJson: null));

		// Assert
		result.Success.Should().BeFalse(
			because: "backend failures should now be returned as structured error payloads");
		result.Error.Should().Match("*Template dependency failed*",
			because: "the create error envelope should preserve the backend diagnostics");
	}

	[Test]
	[Category("Unit")]
	[Description("Advertises the stable MCP tool name for application-delete so callers and tests share the same production identifier.")]
	public void ApplicationDelete_Should_Advertise_Stable_Tool_Name() {
		// Arrange
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ApplicationDeleteTool)
			.GetMethod(nameof(ApplicationDeleteTool.DeleteApplication))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Act
		string toolName = attribute.Name;

		// Assert
		toolName.Should().Be(ApplicationDeleteTool.ToolName,
			because: "the MCP tool name must stay centralized on the production tool type");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured error envelope when application-delete omits app-name.")]
	public void ApplicationDelete_Should_Return_Error_When_AppName_Is_Missing() {
		// Arrange
		UninstallAppCommand startupCommand = new(
			Substitute.For<IApplicationClient>(),
			new EnvironmentSettings(),
			Substitute.For<IDataProvider>(),
			new ApplicationManager(
				Substitute.For<IWorkingDirectoriesProvider>(),
				Substitute.For<IDataProvider>(),
				Substitute.For<ISettingsRepository>(),
				Substitute.For<IApplicationClientFactory>(),
				Substitute.For<IApplicationInstaller>()));
		ILogger logger = Substitute.For<ILogger>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		ApplicationDeleteTool tool = new(startupCommand, logger, commandResolver);

		// Act
		ApplicationDeleteResponse response = tool.DeleteApplication(new ApplicationDeleteArgs(
			EnvironmentName: "sandbox",
			AppName: " ",
			Uri: null,
			Login: null,
			Password: null));

		// Assert
		response.Success.Should().BeFalse(
			because: "application-delete should reject calls that do not identify the application to uninstall");
		response.Error.Should().Contain("app-name is required",
			because: "the tool should explain which required MCP argument is missing");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<UninstallAppCommand>(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves application-delete against the current MCP connection arguments instead of the startup-time command instance.")]
	public void ApplicationDelete_Should_Resolve_Command_From_Current_Mcp_Connection() {
		// Arrange
		IApplicationClient startupApplicationClient = Substitute.For<IApplicationClient>();
		UninstallAppCommand startupCommand = new(
			startupApplicationClient,
			new EnvironmentSettings(),
			Substitute.For<IDataProvider>(),
			new ApplicationManager(
				Substitute.For<IWorkingDirectoriesProvider>(),
				Substitute.For<IDataProvider>(),
				Substitute.For<ISettingsRepository>(),
				Substitute.For<IApplicationClientFactory>(),
				Substitute.For<IApplicationInstaller>()));
		ILogger logger = Substitute.For<ILogger>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver
			.When(resolver => resolver.Resolve<UninstallAppCommand>(Arg.Is<EnvironmentOptions>(options =>
				options.Environment == null &&
				options.Uri == "https://sandbox" &&
				options.Login == "Supervisor" &&
				options.Password == "Supervisor")))
			.Do(_ => throw new InvalidOperationException("resolved from current MCP call"));
		ApplicationDeleteTool tool = new(startupCommand, logger, commandResolver);

		// Act
		ApplicationDeleteResponse response = tool.DeleteApplication(new ApplicationDeleteArgs(
			EnvironmentName: null,
			AppName: "11111111-1111-1111-1111-111111111111",
			Uri: "https://sandbox",
			Login: "Supervisor",
			Password: "Supervisor"));

		// Assert
		response.Success.Should().BeFalse(
			because: "the resolver exception should be surfaced as a structured MCP failure");
		response.Error.Should().Be("resolved from current MCP call",
			because: "the tool should resolve the uninstall command from the current MCP request target");
		startupApplicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!, default, default, default);
		commandResolver.Received(1).Resolve<UninstallAppCommand>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == null &&
			options.Uri == "https://sandbox" &&
			options.Login == "Supervisor" &&
			options.Password == "Supervisor"));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns readable diagnostics when application-delete cannot resolve a target environment or explicit URI.")]
	public void ApplicationDelete_Should_Return_Readable_Error_When_Target_Resolution_Fails() {
		// Arrange
		UninstallAppCommand startupCommand = new(
			Substitute.For<IApplicationClient>(),
			new EnvironmentSettings(),
			Substitute.For<IDataProvider>(),
			new ApplicationManager(
				Substitute.For<IWorkingDirectoriesProvider>(),
				Substitute.For<IDataProvider>(),
				Substitute.For<ISettingsRepository>(),
				Substitute.For<IApplicationClientFactory>(),
				Substitute.For<IApplicationInstaller>()));
		ILogger logger = Substitute.For<ILogger>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver
			.When(resolver => resolver.Resolve<UninstallAppCommand>(Arg.Any<EnvironmentOptions>()))
			.Do(_ => throw new InvalidOperationException(
				"Either a configured environment name or an explicit URI is required for MCP command execution."));
		ApplicationDeleteTool tool = new(startupCommand, logger, commandResolver);

		// Act
		ApplicationDeleteResponse response = tool.DeleteApplication(new ApplicationDeleteArgs(
			EnvironmentName: null,
			AppName: "11111111-1111-1111-1111-111111111111",
			Uri: null,
			Login: null,
			Password: null));

		// Assert
		response.Success.Should().BeFalse(
			because: "application-delete should return a structured failure when no execution target can be resolved");
		response.Error.Should().Be("Either a configured environment name or an explicit URI is required for MCP command execution.",
			because: "the error payload should preserve the human-readable resolver diagnostics");
		response.Error.Should().NotContain("ErrorMessage",
			because: "the error payload should contain the log text instead of the log message type name");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks app-name as the only required MCP field for application-delete while keeping environment and credentials optional.")]
	public void ApplicationDeleteArgs_Should_Require_Only_AppName() {
		// Arrange
		Type argsType = typeof(ApplicationDeleteArgs);
		string[] requiredProperties = [
			nameof(ApplicationDeleteArgs.AppName)
		];
		string[] optionalProperties = [
			nameof(ApplicationDeleteArgs.EnvironmentName),
			nameof(ApplicationDeleteArgs.Uri),
			nameof(ApplicationDeleteArgs.Login),
			nameof(ApplicationDeleteArgs.Password)
		];

		// Act
		string[] propertiesWithoutRequired = requiredProperties
			.Where(propertyName => argsType.GetProperty(propertyName)!
				.GetCustomAttributes(typeof(RequiredAttribute), false)
				.Length == 0)
			.ToArray();
		string[] propertiesStillRequired = optionalProperties
			.Where(propertyName => argsType.GetProperty(propertyName)!
				.GetCustomAttributes(typeof(RequiredAttribute), false)
				.Length > 0)
			.ToArray();

		// Assert
		propertiesWithoutRequired.Should().BeEmpty(
			because: "application-delete should keep app-name required in the MCP contract");
		propertiesStillRequired.Should().BeEmpty(
			because: "environment-name and direct connection arguments should remain optional in the MCP contract");
	}

	[Test]
	[Category("Unit")]
	[Description("Prompt guidance for application MCP tools references the production tool names and the normalized kebab-case arguments.")]
	public void ApplicationPrompts_Should_Mention_Tool_Names_And_Kebab_Case_Arguments() {
		// Arrange

		// Act
		string listPrompt = ApplicationPrompt.ApplicationGetList(environmentName: "sandbox");
		string infoPrompt = ApplicationPrompt.ApplicationGetInfo(
			environmentName: "sandbox",
			id: "7f2d7f13-58d7-47d3-b410-03d432558db3",
			code: null);
		string createPrompt = ApplicationPrompt.ApplicationCreate(
			environmentName: "sandbox",
			name: "Codex App",
			code: "UsrCodexApp",
			templateCode: "AppFreedomUI",
			iconBackground: "#112233",
			description: null,
			iconId: "11111111-1111-1111-1111-111111111111",
			clientTypeId: "22222222-2222-2222-2222-222222222222",
			optionalTemplateDataJson: "{\"entitySchemaName\":\"UsrCodexEntity\"}");

		// Assert
		listPrompt.Should().Contain(ApplicationGetListTool.ApplicationGetListToolName,
			because: "the list prompt should reference the exact production tool name");
		listPrompt.Should().Contain(ToolContractGetTool.ToolName,
			because: "the list prompt should bootstrap the workflow from the authoritative MCP contract before the first application call");
		listPrompt.Should().Contain("`environment-name`",
			because: "the list prompt should keep the normalized environment argument visible");
		listPrompt.Should().Contain("do not wrap `environment-name` inside an `args` object",
			because: "the list prompt should explicitly reject the request shape that caused the analyzed session failure");
		listPrompt.Should().NotContain("`app-id`",
			because: "the list prompt should no longer advertise application filters");
		listPrompt.Should().NotContain("`app-code`",
			because: "the list prompt should no longer advertise application filters");
		listPrompt.Should().Contain("docs://mcp/guides/existing-app-maintenance",
			because: "the list prompt should point existing-app flows to the dedicated MCP maintenance guide");
		listPrompt.Should().Contain(ApplicationGetInfoTool.ApplicationGetInfoToolName,
			because: "the list prompt should direct callers to the canonical follow-up application inspection step");
		infoPrompt.Should().Contain(ApplicationGetInfoTool.ApplicationGetInfoToolName,
			because: "the info prompt should reference the exact production tool name");
		infoPrompt.Should().Contain(ToolContractGetTool.ToolName,
			because: "the info prompt should explain how callers bootstrap the workflow when inspection is their first application call");
		infoPrompt.Should().Contain("`environment-name`",
			because: "the info prompt should keep the normalized environment argument visible");
		infoPrompt.Should().Contain("exactly one identifier",
			because: "the info prompt should require the core-aligned identifier rule");
		infoPrompt.Should().Contain("`id`",
			because: "the info prompt should document the canonical installed-application id selector");
		infoPrompt.Should().Contain("`code`",
			because: "the info prompt should document the canonical installed-application code selector");
		infoPrompt.Should().Contain("docs://mcp/guides/existing-app-maintenance",
			because: "the info prompt should point callers to the dedicated MCP maintenance guide for discover and inspect flows");
		createPrompt.Should().Contain(ApplicationCreateTool.ApplicationCreateToolName,
			because: "the create prompt should reference the exact production tool name");
		createPrompt.Should().Contain(ToolContractGetTool.ToolName,
			because: "the create prompt should bootstrap app-modeling workflows from the authoritative MCP contract");
		createPrompt.Should().Contain("Provide `name`, `code`, `template-code`, and `icon-background`",
			because: "the create prompt should explain the new required input contract");
		createPrompt.Should().Contain("do not nest them under `args`",
			because: "the create prompt should explicitly reject the wrapper shape that caused the analyzed session failure");
		createPrompt.Should().Contain("`optional-template-data-json`",
			because: "the create prompt should mention the JSON string template-data field");
		createPrompt.Should().Contain("docs://mcp/guides/app-modeling",
			because: "the create prompt should point callers to the MCP-owned modeling guidance instead of relying only on consumer AGENTS instructions");
		createPrompt.Should().Contain("canonical main entity",
			because: "the create prompt should explain how callers should treat the template-created primary entity");
		createPrompt.Should().Contain("scalar app-shell tool",
			because: "the create prompt should state that application-create keeps app shell fields as plain strings");
		createPrompt.Should().Contain("Do not send `title-localizations`",
			because: "the create prompt should prevent callers from mixing application-create with entity-schema localization maps");
		createPrompt.Should().Contain("Known values include `AppFreedomUI`",
			because: "the create prompt should steer callers toward technical template names instead of display labels");
		createPrompt.Should().Contain("Pass `client-type-id` only when a non-default Creatio client type is required.",
			because: "the create prompt signature should stay in parity with the executable application-create contract");
		createPrompt.Should().Contain("follow-up entity-schema tools",
			because: "the create prompt should direct callers to schema tools when localized captions are needed");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured error envelope when template-code is missing from the minimal application-create shell.")]
	public void ApplicationCreate_Should_Return_Error_When_TemplateCode_Is_Missing() {
		// Arrange
		IApplicationCreateService applicationCreateService = Substitute.For<IApplicationCreateService>();
		ApplicationCreateTool tool = new(applicationCreateService);

		// Act
		ApplicationContextResponse result = tool.ApplicationCreate(new ApplicationCreateArgs(
			EnvironmentName: "sandbox",
			Name: "Codex App",
			Code: "UsrCodexApp",
			Description: null,
			TemplateCode: string.Empty,
			IconId: null,
			IconBackground: "#112233",
			ClientTypeId: null,
			OptionalTemplateDataJson: null));

		// Assert
		result.Success.Should().BeFalse(
			because: "missing required shell fields should fail before the backend create flow starts");
		result.Error.Should().Contain("template-code is required",
			because: "the validation message should identify the missing contract field");
		result.Error.Should().Contain("AppFreedomUI",
			because: "the validation message should point callers to a usable technical template example");
		applicationCreateService.DidNotReceiveWithAnyArgs().CreateApplication(default!, default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured error envelope when icon-background is missing from the minimal application-create shell.")]
	public void ApplicationCreate_Should_Return_Error_When_IconBackground_Is_Missing() {
		// Arrange
		IApplicationCreateService applicationCreateService = Substitute.For<IApplicationCreateService>();
		ApplicationCreateTool tool = new(applicationCreateService);

		// Act
		ApplicationContextResponse result = tool.ApplicationCreate(new ApplicationCreateArgs(
			EnvironmentName: "sandbox",
			Name: "Codex App",
			Code: "UsrCodexApp",
			Description: null,
			TemplateCode: "AppFreedomUI",
			IconId: null,
			IconBackground: string.Empty,
			ClientTypeId: null,
			OptionalTemplateDataJson: null));

		// Assert
		result.Success.Should().BeFalse(
			because: "missing required shell fields should fail before the backend create flow starts");
		result.Error.Should().Contain("icon-background is required",
			because: "the validation message should identify the missing contract field");
		result.Error.Should().Contain("#1F5F8B",
			because: "the validation message should point callers to a valid color example");
		applicationCreateService.DidNotReceiveWithAnyArgs().CreateApplication(default!, default!);
	}

}
