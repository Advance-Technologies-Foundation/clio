using System;
using System.Linq;
using System.Text.RegularExpressions;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ApplicationCreateServiceTests {
	private ISettingsRepository _settingsRepository = null!;
	private IApplicationClientFactory _applicationClientFactory = null!;
	private IApplicationClient _applicationClient = null!;
	private IServiceUrlBuilder _serviceUrlBuilder = null!;
	private IApplicationInfoService _applicationInfoService = null!;
	private ISysSettingsManager _sysSettingsManager = null!;
	private ICaptionCultureResolver _captionCultureResolver = null!;
	private ILogger _logger = null!;
	private ApplicationCreateService _sut = null!;
	private EnvironmentSettings _environment = null!;
	private ApplicationCreateRequest _fullRequest = null!;

	[SetUp]
	public void SetUp() {
		// Arrange
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_applicationInfoService = Substitute.For<IApplicationInfoService>();
		_sysSettingsManager = Substitute.For<ISysSettingsManager>();
		_sysSettingsManager.GetSysSettingValueByCode("SchemaNamePrefix").Returns("Usr");
		_logger = new NullLogger();
		_environment = new EnvironmentSettings {
			Uri = "https://example.invalid",
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = true
		};
		_fullRequest = new ApplicationCreateRequest(
			Name: "Codex App",
			Code: "UsrCodexApp",
			Description: "Created by tests",
			TemplateCode: "AppFreedomUI",
			IconId: "11111111-1111-1111-1111-111111111111",
			IconBackground: "#0058EF",
			ClientTypeId: "22222222-2222-2222-2222-222222222222",
			OptionalTemplateData: new ApplicationOptionalTemplateData(
				EntitySchemaName: "UsrCodexEntity",
				UseExistingEntitySchema: true,
				UseAiContentGeneration: false,
				AppSectionDescription: "Section description"));
		_settingsRepository.FindEnvironment("sandbox").Returns(_environment);
		_applicationClientFactory.CreateEnvironmentClient(_environment).Returns(_applicationClient);
		_serviceUrlBuilder.Build(Arg.Any<string>(), Arg.Any<EnvironmentSettings>())
			.Returns(callInfo => $"https://example.invalid/{callInfo.ArgAt<string>(0)}");
		_captionCultureResolver = Substitute.For<ICaptionCultureResolver>();
		_captionCultureResolver.Resolve(Arg.Any<EnvironmentOptions>(), Arg.Any<string?>()).Returns("en-US");
		_captionCultureResolver.Resolve(Arg.Any<EnvironmentSettings>(), Arg.Any<string?>()).Returns("en-US");
		_sut = new ApplicationCreateService(
			_settingsRepository,
			_applicationClientFactory,
			_serviceUrlBuilder,
			_applicationInfoService,
			_ => _sysSettingsManager,
			_logger,
			_captionCultureResolver);
	}

	[Test]
	[Description("Rejects a Cyrillic application name when the profile culture is the Latin-script en-US, before the CreateApp call.")]
	public void CreateApplication_Should_Throw_WhenNameScriptDoesNotMatchProfileCulture() {
		// Arrange — the fixture resolver returns en-US for the profile; the application name is
		// localized server-side under the profile, so Cyrillic text would render foreign-language labels.
		ApplicationCreateRequest request = new(
			Name: "Замовлення",
			Code: "UsrCyrillicApp",
			Description: null,
			TemplateCode: "AppFreedomUI");

		// Act
		Action action = () => _sut.CreateApplication("sandbox", request);

		// Assert
		action.Should().Throw<EntitySchemaDesignerException>(
				because: "a Cyrillic application name must not be stored under the Latin-script en-US profile (ENG-91044)")
			.Which.Message.Should().Contain("en-US",
				because: "the error must name the profile culture so the caller can fix the language");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
	}

	[Test]
	[Description("Creates an application through CreateApp and returns the structured application-info payload when the endpoint returns a valid application id.")]
	public void CreateApplication_Should_Return_ApplicationInfo_On_Success() {
		// Arrange
		ApplicationInfoResult expectedResult = new("pkg-uid", "PrimaryPkg", [], SchemaNamePrefix: "Usr");
		ConfigureCreateSuccessForCode("UsrCodexApp");
		_applicationInfoService.GetApplicationInfo("sandbox", "33333333-3333-3333-3333-333333333333", "UsrCodexApp")
			.Returns(expectedResult);

		// Act
		ApplicationInfoResult result = _sut.CreateApplication("sandbox", _fullRequest);

		// Assert
		result.Should().Be(expectedResult,
			because: "successful CreateApp calls should reuse the structured application-info result shape");
		_applicationInfoService.Received(1)
			.GetApplicationInfo("sandbox", "33333333-3333-3333-3333-333333333333", "UsrCodexApp");
	}

	[Test]
	[Description("Generates the application code from the provided name when code is omitted.")]
	public void CreateApplication_Should_Generate_Code_From_Name() {
		// Arrange
		ApplicationCreateRequest request = _fullRequest with {
			Code = null,
			IconId = "11111111-1111-1111-1111-111111111111",
			IconBackground = "#0058EF"
		};
		ConfigureCreateSuccessForCode("UsrCodexApp");
		_applicationInfoService.GetApplicationInfo("sandbox", "33333333-3333-3333-3333-333333333333", "UsrCodexApp")
			.Returns(new ApplicationInfoResult("pkg-uid", "PrimaryPkg", []));

		// Act
		_sut.CreateApplication("sandbox", request);

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
			Arg.Is<string>(body => body.Contains("\"code\":\"UsrCodexApp\"", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Derives a readable application name from the provided code when name is omitted.")]
	public void CreateApplication_Should_Derive_Name_From_Code() {
		// Arrange
		ApplicationCreateRequest request = _fullRequest with {
			Name = null,
			Code = "UsrCodexApplication",
			IconId = "11111111-1111-1111-1111-111111111111",
			IconBackground = "#0058EF"
		};
		ConfigureCreateSuccessForCode("UsrCodexApplication");
		_applicationInfoService.GetApplicationInfo("sandbox", "33333333-3333-3333-3333-333333333333", "UsrCodexApplication")
			.Returns(new ApplicationInfoResult("pkg-uid", "PrimaryPkg", []));

		// Act
		_sut.CreateApplication("sandbox", request);

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
			Arg.Is<string>(body => body.Contains("\"name\":\"Codex Application\"", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Rejects create requests that omit both name and code before any remote calls are made.")]
	public void CreateApplication_Should_Reject_Missing_Name_And_Code() {
		// Arrange
		ApplicationCreateRequest request = _fullRequest with { Name = null, Code = null };

		// Act
		Action action = () => _sut.CreateApplication("sandbox", request);

		// Assert
		action.Should().Throw<ArgumentException>()
			.WithMessage("*Either name or code is required*",
				because: "the create contract requires at least one identifier to resolve the application name/code pair");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
	}

	[Test]
	[Description("Rejects name-based code generation when the supplied name contains no valid code characters.")]
	public void CreateApplication_Should_Reject_Unusable_Generated_Code() {
		// Arrange
		ApplicationCreateRequest request = _fullRequest with {
			Name = "!!!@@@",
			Code = null
		};

		// Act
		Action action = () => _sut.CreateApplication("sandbox", request);

		// Assert
		action.Should().Throw<ArgumentException>()
			.WithMessage("*contains no valid characters*",
				because: "auto-generated codes must fail clearly when the supplied name cannot produce a valid code");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
	}

	[Test]
	[Description("Preserves explicit caller-provided name and code values instead of overriding them with generated defaults.")]
	public void CreateApplication_Should_Preserve_Explicit_Name_And_Code() {
		// Arrange
		ConfigureCreateSuccessForCode("UsrExplicitApp");
		_applicationInfoService.GetApplicationInfo("sandbox", "33333333-3333-3333-3333-333333333333", "UsrExplicitApp")
			.Returns(new ApplicationInfoResult("pkg-uid", "PrimaryPkg", []));
		ApplicationCreateRequest request = _fullRequest with {
			Name = "Explicit App",
			Code = "UsrExplicitApp",
			IconId = "11111111-1111-1111-1111-111111111111",
			IconBackground = "#0058EF"
		};

		// Act
		_sut.CreateApplication("sandbox", request);

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
			Arg.Is<string>(body =>
				body.Contains("\"name\":\"Explicit App\"", StringComparison.Ordinal) &&
				body.Contains("\"code\":\"UsrExplicitApp\"", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Assigns a random Freedom UI palette color when the caller omits icon-background.")]
	public void CreateApplication_Should_Assign_Palette_Color_When_IconBackground_Omitted() {
		// Arrange
		ApplicationCreateRequest request = _fullRequest with {
			IconBackground = null,
			IconId = "11111111-1111-1111-1111-111111111111"
		};
		ConfigureCreateSuccessForCode("UsrCodexApp");
		_applicationInfoService.GetApplicationInfo("sandbox", "33333333-3333-3333-3333-333333333333", "UsrCodexApp")
			.Returns(new ApplicationInfoResult("pkg-uid", "PrimaryPkg", []));

		// Act
		_sut.CreateApplication("sandbox", request);

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
			Arg.Is<string>(body => ApplicationSectionColorPalette.Colors.Any(c =>
				body.Contains($"\"iconBackground\":\"{c}\"", StringComparison.Ordinal))));
	}

	[Test]
	[Description("Rejects icon-background values that are not in the Freedom UI palette.")]
	public void CreateApplication_Should_Reject_IconBackground_Outside_Palette() {
		// Arrange
		ApplicationCreateRequest request = _fullRequest with { IconBackground = "#1F5F8B" };

		// Act
		Action action = () => _sut.CreateApplication("sandbox", request);

		// Assert
		action.Should().Throw<ArgumentException>()
			.WithMessage("*not a Freedom UI palette color*",
				because: "only the 16 Freedom UI palette swatches are accepted as icon background colors");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
	}

	[Test]
	[Description("Preserves an explicit icon background color instead of replacing it with a generated one.")]
	public void CreateApplication_Should_Preserve_Explicit_Icon_Background() {
		// Arrange
		ConfigureCreateSuccessForCode("UsrCodexApp");
		_applicationInfoService.GetApplicationInfo("sandbox", "33333333-3333-3333-3333-333333333333", "UsrCodexApp")
			.Returns(new ApplicationInfoResult("pkg-uid", "PrimaryPkg", []));

		// Act
		_sut.CreateApplication("sandbox", _fullRequest);

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
			Arg.Is<string>(body => body.Contains("\"iconBackground\":\"#0058EF\"", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Resolves a random icon from SysAppIcons when the caller omits icon-id.")]
	public void CreateApplication_Should_Resolve_Random_Icon_When_IconId_Is_Omitted() {
		// Arrange
		ApplicationCreateRequest request = _fullRequest with {
			IconId = null,
			IconBackground = "#0058EF"
		};
		ConfigureIconQuery("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
		ConfigureCreateSuccessForCode("UsrCodexApp");
		_applicationInfoService.GetApplicationInfo("sandbox", "33333333-3333-3333-3333-333333333333", "UsrCodexApp")
			.Returns(new ApplicationInfoResult("pkg-uid", "PrimaryPkg", []));

		// Act
		_sut.CreateApplication("sandbox", request);

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(url => url.EndsWith("SelectQuery", StringComparison.Ordinal)),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysAppIcons\"", StringComparison.Ordinal)));
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
			Arg.Is<string>(body => body.Contains("\"iconId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Treats icon-id='auto' the same as an omitted icon-id and resolves a random SysAppIcons identifier.")]
	public void CreateApplication_Should_Resolve_Random_Icon_When_IconId_Is_Auto() {
		// Arrange
		ApplicationCreateRequest request = _fullRequest with {
			IconId = "auto",
			IconBackground = "#0058EF"
		};
		ConfigureIconQuery("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
		ConfigureCreateSuccessForCode("UsrCodexApp");
		_applicationInfoService.GetApplicationInfo("sandbox", "33333333-3333-3333-3333-333333333333", "UsrCodexApp")
			.Returns(new ApplicationInfoResult("pkg-uid", "PrimaryPkg", []));

		// Act
		_sut.CreateApplication("sandbox", request);

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
			Arg.Is<string>(body => body.Contains("\"iconId\":\"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\"", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Preserves an explicit icon identifier instead of querying SysAppIcons.")]
	public void CreateApplication_Should_Preserve_Explicit_IconId() {
		// Arrange
		ConfigureCreateSuccessForCode("UsrCodexApp");
		_applicationInfoService.GetApplicationInfo("sandbox", "33333333-3333-3333-3333-333333333333", "UsrCodexApp")
			.Returns(new ApplicationInfoResult("pkg-uid", "PrimaryPkg", []));

		// Act
		_sut.CreateApplication("sandbox", _fullRequest);

		// Assert
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Is<string>(url => url.EndsWith("SelectQuery", StringComparison.Ordinal)),
			Arg.Any<string>());
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
			Arg.Is<string>(body => body.Contains("\"iconId\":\"11111111-1111-1111-1111-111111111111\"", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Fails cleanly when SysAppIcons returns no rows for auto-generated icon resolution.")]
	public void CreateApplication_Should_Throw_When_SysAppIcons_Is_Empty() {
		// Arrange
		ApplicationCreateRequest request = _fullRequest with {
			IconId = null,
			IconBackground = "#0058EF"
		};
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("SelectQuery", StringComparison.Ordinal)),
				Arg.Any<string>())
			.Returns("""{"success":true,"rows":[]}""");

		// Act
		Action action = () => _sut.CreateApplication("sandbox", request);

		// Assert
		action.Should().Throw<InvalidOperationException>()
			.WithMessage("*No icons found in SysAppIcons*",
				because: "missing icons should fail clearly instead of sending an invalid CreateApp request");
	}

	[Test]
	[Description("Includes appSectionDescription in the CreateApp optionalTemplateData payload when provided.")]
	public void CreateApplication_Should_Include_AppSectionDescription_When_Provided() {
		// Arrange
		ConfigureCreateSuccessForCode("UsrCodexApp");
		_applicationInfoService.GetApplicationInfo("sandbox", "33333333-3333-3333-3333-333333333333", "UsrCodexApp")
			.Returns(new ApplicationInfoResult("pkg-uid", "PrimaryPkg", []));

		// Act
		_sut.CreateApplication("sandbox", _fullRequest);

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
			Arg.Is<string>(body => body.Contains("\"appSectionDescription\":\"Section description\"", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Rejects invalid icon identifiers before any CreateApp request is sent.")]
	public void CreateApplication_Should_Reject_Invalid_IconId() {
		// Arrange
		ApplicationCreateRequest request = _fullRequest with { IconId = "not-a-guid" };

		// Act
		Action action = () => _sut.CreateApplication("sandbox", request);

		// Assert
		action.Should().Throw<ArgumentException>()
			.WithMessage("*Icon id must be a valid GUID or 'auto'*",
				because: "explicit invalid icon ids should be rejected before any remote calls happen");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
	}

	[Test]
	[Description("Rejects invalid client type identifiers before any CreateApp request is sent.")]
	public void CreateApplication_Should_Reject_Invalid_ClientTypeId() {
		// Arrange
		ApplicationCreateRequest request = _fullRequest with { ClientTypeId = "not-a-guid" };

		// Act
		Action action = () => _sut.CreateApplication("sandbox", request);

		// Assert
		action.Should().Throw<ArgumentException>()
			.WithMessage("*Client type id must be a valid GUID*",
				because: "explicit invalid client type ids should be rejected before any remote calls happen");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
	}

	[Test]
	[Description("Rejects CreateApp success responses that do not contain a valid application identifier before application info is loaded.")]
	public void CreateApplication_Should_Throw_When_CreateApp_Returns_Invalid_AppId() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
				Arg.Any<string>())
			.Returns("""{"success":true,"value":"not-a-guid"}""");

		// Act
		Action action = () => _sut.CreateApplication("sandbox", _fullRequest);

		// Assert
		action.Should().Throw<InvalidOperationException>()
			.WithMessage("*invalid application identifier*",
				because: "the create flow must validate the returned app id before reading application info");
		_applicationInfoService.DidNotReceiveWithAnyArgs().GetApplicationInfo(default(string)!, default, default);
	}

	[Test]
	[Description("Wraps application-info reload failures with a clear loading error when CreateApp succeeds but the created application's metadata cannot be read.")]
	public void CreateApplication_Should_Throw_Clear_Error_When_ApplicationInfo_Load_Fails() {
		// Arrange
		ConfigureCreateSuccessForCode("UsrCodexApp");
		_applicationInfoService.GetApplicationInfo("sandbox", "33333333-3333-3333-3333-333333333333", "UsrCodexApp")
			.Returns(_ => throw new InvalidOperationException("Primary package not found."));

		// Act
		Action action = () => _sut.CreateApplication("sandbox", _fullRequest);

		// Assert
		action.Should().Throw<InvalidOperationException>()
			.WithMessage("*UsrCodexApp*")
			.WithMessage("*metadata could not be loaded after 15 attempts*")
			.WithMessage("*Primary package not found*",
				because: "successful CreateApp responses should retry eventual-consistency misses and still explain the last metadata load failure");
		_applicationInfoService.Received(15)
			.GetApplicationInfo("sandbox", "33333333-3333-3333-3333-333333333333", "UsrCodexApp");
	}

	[Test]
	[Description("Retries the metadata load after a successful CreateApp response and returns the first successful application-info result.")]
	public void CreateApplication_Should_Retry_Metadata_Load_After_Successful_Create() {
		// Arrange
		ApplicationInfoResult expectedResult = new("pkg-uid", "PrimaryPkg", [], SchemaNamePrefix: "Usr");
		ConfigureCreateSuccessForCode("UsrCodexApp");
		_applicationInfoService.GetApplicationInfo("sandbox", "33333333-3333-3333-3333-333333333333", "UsrCodexApp")
			.Returns(
				_ => throw new InvalidOperationException("Application 'UsrCodexApp' not found."),
				_ => throw new InvalidOperationException("Application 'UsrCodexApp' not found."),
				_ => expectedResult);

		// Act
		ApplicationInfoResult result = _sut.CreateApplication("sandbox", _fullRequest);

		// Assert
		result.Should().Be(expectedResult,
			because: "CreateApp can complete before the follow-up application-info query becomes consistent in the target environment");
		_applicationInfoService.Received(3)
			.GetApplicationInfo("sandbox", "33333333-3333-3333-3333-333333333333", "UsrCodexApp");
	}

	[Test]
	[Description("Maps CreateApp failures to a readable error message when the endpoint returns errorInfo.")]
	public void CreateApplication_Should_Throw_Readable_Error_When_CreateApp_Fails() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
				Arg.Any<string>())
			.Returns("""{"success":false,"errorInfo":{"message":"Template validation failed."}}""");

		// Act
		Action action = () => _sut.CreateApplication("sandbox", _fullRequest);

		// Assert
		action.Should().Throw<InvalidOperationException>()
			.WithMessage("*Template validation failed*",
				because: "backend create failures should surface the server-provided message to MCP callers");
	}

	[Test]
	[Description("Handles CreateApp failures where the backend explicitly returns dependenciesErrors as null without throwing a null-reference exception.")]
	public void CreateApplication_Should_Throw_Readable_Error_When_CreateApp_Failure_Has_Null_DependenciesErrors() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
				Arg.Any<string>())
			.Returns("""{"success":false,"errorInfo":{"message":"Environment with key 'missing-env' not found."},"dependenciesErrors":null}""");

		// Act
		Action action = () => _sut.CreateApplication("sandbox", _fullRequest);

		// Assert
		action.Should().Throw<InvalidOperationException>()
			.WithMessage("*missing-env*")
			.WithMessage("*not found*",
				because: "backend failure payloads can omit dependency details and should still surface the readable server error");
	}

	[Test]
	[Description("Includes dependency diagnostics in the mapped failure message when CreateApp reports dependency errors.")]
	public void CreateApplication_Should_Include_Dependency_Errors_In_Failure_Message() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
				Arg.Any<string>())
			.Returns("""{"success":false,"errorInfo":{"message":"Dependency check failed."},"dependenciesErrors":[{"source":"Template","reference":"UsrEntity","package":"PkgA"}]}""");

		// Act
		Action action = () => _sut.CreateApplication("sandbox", _fullRequest);

		// Assert
		action.Should().Throw<InvalidOperationException>()
			.WithMessage("*Dependency check failed*")
			.WithMessage("*source=Template*")
			.WithMessage("*reference=UsrEntity*")
			.WithMessage("*package=PkgA*",
				because: "dependency diagnostics should remain readable when CreateApp validation fails");
	}

	[Test]
	[Description("Polls get-app-info by application code when the CreateApp request times out and returns the first successful structured result.")]
	public void CreateApplication_Should_Poll_ApplicationInfo_When_CreateApp_Times_Out() {
		// Arrange
		ApplicationInfoResult expectedResult = new("pkg-uid", "PrimaryPkg", [], SchemaNamePrefix: "Usr");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
				Arg.Any<string>())
			.Returns(_ => throw new InvalidOperationException(
				"App Installer CreateApp request failed for https://example.invalid with timeout of 30000ms exceeded."));
		_applicationInfoService.GetApplicationInfo("sandbox", null, "UsrCodexApp")
			.Returns(
				_ => throw new InvalidOperationException("Application 'UsrCodexApp' not found."),
				_ => throw new InvalidOperationException("Application 'UsrCodexApp' not found."),
				_ => expectedResult);

		// Act
		ApplicationInfoResult result = _sut.CreateApplication("sandbox", _fullRequest);

		// Assert
		result.Should().Be(expectedResult,
			because: "timeout recovery should return the created application once get-app-info can resolve it");
		_applicationInfoService.Received(3).GetApplicationInfo("sandbox", null, "UsrCodexApp");
	}

	[Test]
	[Description("Fails cleanly when CreateApp times out and the created application never becomes visible through get-app-info polling.")]
	public void CreateApplication_Should_Throw_When_Timeout_Recovery_Does_Not_Find_Application() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
				Arg.Any<string>())
			.Returns(_ => throw new InvalidOperationException(
				"App Installer CreateApp request failed for https://example.invalid with timeout of 30000ms exceeded."));
		_applicationInfoService.GetApplicationInfo("sandbox", null, "UsrCodexApp")
			.Returns(_ => throw new InvalidOperationException("Application 'UsrCodexApp' not found."));

		// Act
		Action action = () => _sut.CreateApplication("sandbox", _fullRequest);

		// Assert
		action.Should().Throw<InvalidOperationException>()
			.WithMessage("*UsrCodexApp*")
			.WithMessage("*not found*",
				because: "timeout recovery failures should surface the final polling failure when the application never becomes observable");
		_applicationInfoService.Received(15).GetApplicationInfo("sandbox", null, "UsrCodexApp");
	}

	[Test]
	[Description("Rejects unknown environments with the same readable diagnostics used by other environment-sensitive services.")]
	public void CreateApplication_Should_Throw_When_Environment_Is_Not_Found() {
		// Arrange
		_settingsRepository.FindEnvironment("missing").Returns((EnvironmentSettings?)null);

		// Act
		Action action = () => _sut.CreateApplication("missing", _fullRequest);

		// Assert
		action.Should().Throw<InvalidOperationException>()
			.WithMessage("*missing*")
			.WithMessage("*not found*",
				because: "environment-sensitive create flows should fail clearly when the target environment is unknown");
	}

	[Test]
	[Description("Rejects placeholder environment settings returned by the repository for unknown keys before any remote calls are attempted.")]
	public void CreateApplication_Should_Throw_When_Environment_Settings_Are_Empty() {
		// Arrange
		_settingsRepository.FindEnvironment("missing").Returns(new EnvironmentSettings());

		// Act
		Action action = () => _sut.CreateApplication("missing", _fullRequest);

		// Assert
		action.Should().Throw<InvalidOperationException>()
			.WithMessage("*missing*")
			.WithMessage("*not found*",
				because: "settings repositories can materialize empty environment placeholders for unknown keys and the create flow should reject them explicitly");
		_applicationClientFactory.DidNotReceiveWithAnyArgs().CreateEnvironmentClient(default!);
	}

	[Test]
	[Description("Prepends a non-default prefix when the environment SchemaNamePrefix differs from Usr.")]
	public void CreateApplication_Should_Prepend_NonDefault_Prefix_To_Generated_Code() {
		// Arrange
		_sysSettingsManager.GetSysSettingValueByCode("SchemaNamePrefix").Returns("Abc");
		ApplicationCreateRequest request = _fullRequest with {
			Name = "Todo List",
			Code = null,
			IconId = "11111111-1111-1111-1111-111111111111",
			IconBackground = "#0058EF"
		};
		ConfigureCreateSuccessForCode("AbcTodoList");
		_applicationInfoService.GetApplicationInfo("sandbox", "33333333-3333-3333-3333-333333333333", "AbcTodoList")
			.Returns(new ApplicationInfoResult("pkg-uid", "PrimaryPkg", [], SchemaNamePrefix: "Abc"));

		// Act
		_sut.CreateApplication("sandbox", request);

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
			Arg.Is<string>(body => body.Contains("\"code\":\"AbcTodoList\"", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Preserves a code that already carries the non-default prefix without double-prefixing.")]
	public void CreateApplication_Should_Not_Double_Prefix_When_Code_Already_Has_NonDefault_Prefix() {
		// Arrange
		_sysSettingsManager.GetSysSettingValueByCode("SchemaNamePrefix").Returns("Abc");
		ApplicationCreateRequest request = _fullRequest with {
			Code = "AbcTodoList",
			Name = null,
			IconId = "11111111-1111-1111-1111-111111111111",
			IconBackground = "#0058EF"
		};
		ConfigureCreateSuccessForCode("AbcTodoList");
		_applicationInfoService.GetApplicationInfo("sandbox", "33333333-3333-3333-3333-333333333333", "AbcTodoList")
			.Returns(new ApplicationInfoResult("pkg-uid", "PrimaryPkg", [], SchemaNamePrefix: "Abc"));

		// Act
		_sut.CreateApplication("sandbox", request);

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
			Arg.Is<string>(body => body.Contains("\"code\":\"AbcTodoList\"", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Generates an unprefixed code when SchemaNamePrefix is empty.")]
	public void CreateApplication_Should_Generate_Unprefixed_Code_When_Prefix_Is_Empty() {
		// Arrange
		_sysSettingsManager.GetSysSettingValueByCode("SchemaNamePrefix").Returns(string.Empty);
		ApplicationCreateRequest request = _fullRequest with {
			Name = "Todo List",
			Code = null,
			IconId = "11111111-1111-1111-1111-111111111111",
			IconBackground = "#0058EF"
		};
		ConfigureCreateSuccessForCode("TodoList");
		_applicationInfoService.GetApplicationInfo("sandbox", "33333333-3333-3333-3333-333333333333", "TodoList")
			.Returns(new ApplicationInfoResult("pkg-uid", "PrimaryPkg", [], SchemaNamePrefix: string.Empty));

		// Act
		_sut.CreateApplication("sandbox", request);

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
			Arg.Is<string>(body => body.Contains("\"code\":\"TodoList\"", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Sanitizes an unprefixed code correctly when SchemaNamePrefix is empty.")]
	public void CreateApplication_Should_Sanitize_Code_Without_Prefix_When_Prefix_Is_Empty() {
		// Arrange
		_sysSettingsManager.GetSysSettingValueByCode("SchemaNamePrefix").Returns(string.Empty);
		ApplicationCreateRequest request = _fullRequest with {
			Code = "TodoList",
			Name = null,
			IconId = "11111111-1111-1111-1111-111111111111",
			IconBackground = "#0058EF"
		};
		ConfigureCreateSuccessForCode("TodoList");
		_applicationInfoService.GetApplicationInfo("sandbox", "33333333-3333-3333-3333-333333333333", "TodoList")
			.Returns(new ApplicationInfoResult("pkg-uid", "PrimaryPkg", [], SchemaNamePrefix: string.Empty));

		// Act
		_sut.CreateApplication("sandbox", request);

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
			Arg.Is<string>(body => body.Contains("\"code\":\"TodoList\"", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Rejects a digit-starting name when SchemaNamePrefix is empty because no prefix can absorb the digit.")]
	public void CreateApplication_Should_Reject_Digit_Starting_Name_When_Prefix_Is_Empty() {
		// Arrange
		_sysSettingsManager.GetSysSettingValueByCode("SchemaNamePrefix").Returns(string.Empty);
		ApplicationCreateRequest request = _fullRequest with {
			Name = "1st Task",
			Code = null,
			IconId = "11111111-1111-1111-1111-111111111111",
			IconBackground = "#0058EF"
		};

		// Act
		Action action = () => _sut.CreateApplication("sandbox", request);

		// Assert
		action.Should().Throw<ArgumentException>()
			.WithMessage("*starts with a digit*",
				because: "a digit-starting name cannot produce a valid schema code when no prefix is configured");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
	}

	[Test]
	[Description("Rejects a digit-starting code when SchemaNamePrefix is empty.")]
	public void CreateApplication_Should_Reject_Digit_Starting_Code_When_Prefix_Is_Empty() {
		// Arrange
		_sysSettingsManager.GetSysSettingValueByCode("SchemaNamePrefix").Returns(string.Empty);
		ApplicationCreateRequest request = _fullRequest with {
			Code = "1TodoList",
			Name = null,
			IconId = "11111111-1111-1111-1111-111111111111",
			IconBackground = "#0058EF"
		};

		// Act
		Action action = () => _sut.CreateApplication("sandbox", request);

		// Assert
		action.Should().Throw<ArgumentException>()
			.WithMessage("*starts with a digit*",
				because: "a digit-starting code cannot be used as a valid schema name when no prefix is configured");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
	}

	[Test]
	[Description("Propagates the exception when the SchemaNamePrefix setting cannot be read, so the caller receives an explicit failure instead of silently creating schemas with the wrong prefix.")]
	public void CreateApplication_Should_Throw_When_SysSettings_Throws() {
		// Arrange
		_sysSettingsManager.GetSysSettingValueByCode("SchemaNamePrefix")
			.Returns(_ => throw new InvalidOperationException("SysSettings unavailable."));
		ApplicationCreateRequest request = _fullRequest with {
			Name = "Todo List",
			Code = null,
			IconId = "11111111-1111-1111-1111-111111111111",
			IconBackground = "#0058EF"
		};

		// Act
		Action action = () => _sut.CreateApplication("sandbox", request);

		// Assert
		action.Should().Throw<InvalidOperationException>()
			.WithMessage("*SysSettings unavailable*",
				because: "a read failure must propagate so the caller knows the prefix could not be determined rather than receiving silently wrong schema names");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
	}

	[Test]
	[Description("Pins the web client type so Creatio skips the main entity mobile pages when with-mobile-pages is false and no explicit client type is supplied.")]
	public void CreateApplication_Should_Send_WebClientTypeId_When_WithMobilePages_Is_False() {
		// Arrange
		ApplicationCreateRequest request = _fullRequest with {
			ClientTypeId = null,
			WithMobilePages = false,
			IconId = "11111111-1111-1111-1111-111111111111",
			IconBackground = "#0058EF"
		};
		ConfigureCreateSuccessForCode("UsrCodexApp");
		_applicationInfoService.GetApplicationInfo("sandbox", "33333333-3333-3333-3333-333333333333", "UsrCodexApp")
			.Returns(new ApplicationInfoResult("pkg-uid", "PrimaryPkg", []));

		// Act
		_sut.CreateApplication("sandbox", request);

		// Assert
		// with-mobile-pages=false must send the web client type so the backend creates web pages only
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
			Arg.Is<string>(body => body.Contains("\"clientTypeId\":\"195785B4-F55A-4E72-ACE3-6480B54C8FA5\"", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Omits the client type entirely when with-mobile-pages is true and no explicit client type is supplied, preserving the default full page set.")]
	public void CreateApplication_Should_Not_Send_ClientTypeId_When_WithMobilePages_Is_True_And_None_Provided() {
		// Arrange
		ApplicationCreateRequest request = _fullRequest with {
			ClientTypeId = null,
			WithMobilePages = true,
			IconId = "11111111-1111-1111-1111-111111111111",
			IconBackground = "#0058EF"
		};
		ConfigureCreateSuccessForCode("UsrCodexApp");
		_applicationInfoService.GetApplicationInfo("sandbox", "33333333-3333-3333-3333-333333333333", "UsrCodexApp")
			.Returns(new ApplicationInfoResult("pkg-uid", "PrimaryPkg", []));

		// Act
		_sut.CreateApplication("sandbox", request);

		// Assert
		// the default with-mobile-pages=true must leave clientTypeId unset so Creatio generates the full five-page set as before
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
			Arg.Is<string>(body => !body.Contains("clientTypeId", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Prefers an explicit client type over the with-mobile-pages=false mapping so callers can target a specific Creatio client type.")]
	public void CreateApplication_Should_Prefer_Explicit_ClientTypeId_Over_WithMobilePages_False() {
		// Arrange
		ApplicationCreateRequest request = _fullRequest with {
			ClientTypeId = "22222222-2222-2222-2222-222222222222",
			WithMobilePages = false,
			IconId = "11111111-1111-1111-1111-111111111111",
			IconBackground = "#0058EF"
		};
		ConfigureCreateSuccessForCode("UsrCodexApp");
		_applicationInfoService.GetApplicationInfo("sandbox", "33333333-3333-3333-3333-333333333333", "UsrCodexApp")
			.Returns(new ApplicationInfoResult("pkg-uid", "PrimaryPkg", []));

		// Act
		_sut.CreateApplication("sandbox", request);

		// Assert
		// an explicit client-type-id must take precedence over the with-mobile-pages web-client mapping
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
			Arg.Is<string>(body =>
				body.Contains("\"clientTypeId\":\"22222222-2222-2222-2222-222222222222\"", StringComparison.Ordinal) &&
				!body.Contains("195785B4-F55A-4E72-ACE3-6480B54C8FA5", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Settings-based overload (ENG-93347 Story 5): rejects a null EnvironmentSettings with ArgumentNullException before any client factory or remote call is attempted.")]
	public void CreateApplication_ShouldThrowArgumentNullException_WhenEnvironmentSettingsAreNull() {
		// Arrange
		EnvironmentSettings environmentSettings = null!;

		// Act
		Action action = () => _sut.CreateApplication(environmentSettings, _fullRequest);

		// Assert
		action.Should().Throw<ArgumentNullException>(
			because: "the settings-based overload must fail fast on a null tenant before any factory invocation");
		_applicationClientFactory.DidNotReceiveWithAnyArgs().CreateEnvironmentClient(default!);
	}

	[Test]
	[Description("Settings-based overload (ENG-93347 Story 5, AC-03/AC-04): creates the application against the supplied settings without ever consulting ISettingsRepository, and routes BOTH nested calls — the caption-culture resolution and the application-info readback — through the settings-based overloads, never the name-based ones.")]
	public void CreateApplication_ShouldUseSettingsBasedNestedCalls_WhenEnvironmentSettingsSupplied() {
		// Arrange
		ApplicationInfoResult expectedResult = new("pkg-uid", "PrimaryPkg", [], SchemaNamePrefix: "Usr");
		ConfigureCreateSuccessForCode("UsrCodexApp");
		_applicationInfoService.GetApplicationInfo(_environment, "33333333-3333-3333-3333-333333333333", "UsrCodexApp")
			.Returns(expectedResult);

		// Act
		ApplicationInfoResult result = _sut.CreateApplication(_environment, _fullRequest);

		// Assert
		result.Should().Be(expectedResult,
			because: "the settings-based overload must return the same structured application-info result as the name-based path");
		_settingsRepository.DidNotReceiveWithAnyArgs().FindEnvironment(default);
		_settingsRepository.DidNotReceiveWithAnyArgs().GetEnvironment(default(EnvironmentOptions)!);
		_captionCultureResolver.Received(1).Resolve(_environment, null);
		_captionCultureResolver.DidNotReceiveWithAnyArgs().Resolve(default(EnvironmentOptions)!, default);
		_applicationInfoService.Received(1)
			.GetApplicationInfo(_environment, "33333333-3333-3333-3333-333333333333", "UsrCodexApp");
		_applicationInfoService.DidNotReceiveWithAnyArgs().GetApplicationInfo(default(string)!, default, default);
	}

	[Test]
	[Description("Settings-based overload (ENG-93347 Story 5, AC-04): when CreateApp times out, the timeout-recovery polling loop reads the application back through the settings-based GetApplicationInfo overload — never through the name-based one.")]
	public void CreateApplication_ShouldPollThroughSettingsOverload_WhenCreateAppTimesOutWithSettingsSupplied() {
		// Arrange
		ApplicationInfoResult expectedResult = new("pkg-uid", "PrimaryPkg", [], SchemaNamePrefix: "Usr");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
				Arg.Any<string>())
			.Returns(_ => throw new InvalidOperationException(
				"App Installer CreateApp request failed for https://example.invalid with timeout of 30000ms exceeded."));
		_applicationInfoService.GetApplicationInfo(_environment, null, "UsrCodexApp")
			.Returns(
				_ => throw new InvalidOperationException("Application 'UsrCodexApp' not found."),
				_ => expectedResult);

		// Act
		ApplicationInfoResult result = _sut.CreateApplication(_environment, _fullRequest);

		// Assert
		result.Should().Be(expectedResult,
			because: "timeout recovery on the settings-based path should return the created application once the settings-based readback resolves it");
		_applicationInfoService.Received(2).GetApplicationInfo(_environment, null, "UsrCodexApp");
		_applicationInfoService.DidNotReceiveWithAnyArgs().GetApplicationInfo(default(string)!, default, default);
		_settingsRepository.DidNotReceiveWithAnyArgs().FindEnvironment(default);
	}

	private void ConfigureCreateSuccessForCode(string appCode = "UsrCodexApp")
    {
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("CreateApp", StringComparison.Ordinal)),
				Arg.Any<string>())
			.Returns("""{"success":true,"value":"33333333-3333-3333-3333-333333333333"}""");
	}

	private void ConfigureIconQuery(string iconId) {
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("SelectQuery", StringComparison.Ordinal)),
				Arg.Any<string>())
			.Returns($$"""{"success":true,"rows":[{"Id":"{{iconId}}"}]}""");
	}
}
