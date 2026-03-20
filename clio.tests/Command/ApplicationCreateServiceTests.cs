using System;
using System.Text.RegularExpressions;
using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public sealed class ApplicationCreateServiceTests {
	private ISettingsRepository _settingsRepository = null!;
	private IApplicationClientFactory _applicationClientFactory = null!;
	private IApplicationClient _applicationClient = null!;
	private IServiceUrlBuilder _serviceUrlBuilder = null!;
	private IApplicationInfoService _applicationInfoService = null!;
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
			IconBackground: "#FFFFFF",
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
		_sut = new ApplicationCreateService(
			_settingsRepository,
			_applicationClientFactory,
			_serviceUrlBuilder,
			_applicationInfoService);
	}

	[Test]
	[Description("Creates an application through CreateApp and returns the structured application-info payload when the endpoint returns a valid application id.")]
	public void CreateApplication_Should_Return_ApplicationInfo_On_Success() {
		// Arrange
		ApplicationInfoResult expectedResult = new("pkg-uid", "PrimaryPkg", []);
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
			IconBackground = "#FFFFFF"
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
			IconBackground = "#FFFFFF"
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
			IconBackground = "#123456"
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
	[Description("Generates an icon background color when the caller omits it.")]
	public void CreateApplication_Should_Generate_Icon_Background_When_Omitted() {
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
			Arg.Is<string>(body => Regex.IsMatch(body, "\"iconBackground\":\"#[0-9A-F]{6}\"")));
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
			Arg.Is<string>(body => body.Contains("\"iconBackground\":\"#FFFFFF\"", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Resolves a random icon from SysAppIcons when the caller omits icon-id.")]
	public void CreateApplication_Should_Resolve_Random_Icon_When_IconId_Is_Omitted() {
		// Arrange
		ApplicationCreateRequest request = _fullRequest with {
			IconId = null,
			IconBackground = "#FFFFFF"
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
			IconBackground = "#FFFFFF"
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
			IconBackground = "#FFFFFF"
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
		_applicationInfoService.DidNotReceiveWithAnyArgs().GetApplicationInfo(default!, default, default);
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
		ApplicationInfoResult expectedResult = new("pkg-uid", "PrimaryPkg", []);
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
	[Description("Polls application-get-info by application code when the CreateApp request times out and returns the first successful structured result.")]
	public void CreateApplication_Should_Poll_ApplicationInfo_When_CreateApp_Times_Out() {
		// Arrange
		ApplicationInfoResult expectedResult = new("pkg-uid", "PrimaryPkg", []);
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
			because: "timeout recovery should return the created application once application-get-info can resolve it");
		_applicationInfoService.Received(3).GetApplicationInfo("sandbox", null, "UsrCodexApp");
	}

	[Test]
	[Description("Fails cleanly when CreateApp times out and the created application never becomes visible through application-get-info polling.")]
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
