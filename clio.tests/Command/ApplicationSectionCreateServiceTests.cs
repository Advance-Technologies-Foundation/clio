using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ApplicationSectionCreateServiceTests {
	private ISettingsRepository _settingsRepository = null!;
	private IApplicationClientFactory _applicationClientFactory = null!;
	private IApplicationClient _applicationClient = null!;
	private IServiceUrlBuilder _serviceUrlBuilder = null!;
	private IApplicationInfoService _applicationInfoService = null!;
	private EnvironmentSettings _environmentSettings = null!;
	private ApplicationSectionCreateService _sut = null!;

	[SetUp]
	public void SetUp() {
		// Arrange
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_applicationInfoService = Substitute.For<IApplicationInfoService>();
		_environmentSettings = new EnvironmentSettings {
			Uri = "https://example.invalid",
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = true
		};
		_settingsRepository.FindEnvironment("sandbox").Returns(_environmentSettings);
		_applicationClientFactory.CreateEnvironmentClient(_environmentSettings).Returns(_applicationClient);
		_serviceUrlBuilder.Build(Arg.Any<string>(), Arg.Any<EnvironmentSettings>())
			.Returns(callInfo => $"https://example.invalid/{callInfo.ArgAt<string>(0)}");
		IServiceUrlBuilderFactory serviceUrlBuilderFactory = Substitute.For<IServiceUrlBuilderFactory>();
		serviceUrlBuilderFactory.Create(Arg.Any<EnvironmentSettings>()).Returns(_serviceUrlBuilder);
		_sut = new ApplicationSectionCreateService(
			_settingsRepository,
			_applicationClientFactory,
			_serviceUrlBuilder,
			serviceUrlBuilderFactory,
			_applicationInfoService);
	}

	[Test]
	[Description("Creates a new-object section with web-only pages and returns structured readback data including the created pages diff.")]
	public void CreateSection_Should_Create_New_Object_Section_With_Web_Pages_Only() {
		// Arrange
		string? insertBody = null;
		ApplicationInfoResult beforeInfo = new(
			"pkg-uid",
			"UsrOrdersApp",
			[],
			[
				new PageListItem {
					SchemaName = "UsrOrdersApp_Main",
					UId = "page-old",
					PackageName = "UsrOrdersApp",
					ParentSchemaName = "BasePage"
				}
			],
			"app-id",
			"Orders App",
			"UsrOrdersApp",
			"8.3.0");
		ApplicationEntityInfoResult entity = new(
			"entity-uid",
			"UsrOrders",
			"Orders",
			[]);
		ApplicationInfoResult afterInfo = new(
			"pkg-uid",
			"UsrOrdersApp",
			[entity],
			[
				new PageListItem {
					SchemaName = "UsrOrdersApp_Main",
					UId = "page-old",
					PackageName = "UsrOrdersApp",
					ParentSchemaName = "BasePage"
				},
				new PageListItem {
					SchemaName = "UsrOrders_FormPage",
					UId = "page-new",
					PackageName = "UsrOrdersApp",
					ParentSchemaName = "BasePageV2"
				}
			],
			"app-id",
			"Orders App",
			"UsrOrdersApp",
			"8.3.0");
		_applicationInfoService.GetApplicationInfo("sandbox", null, "UsrOrdersApp")
			.Returns(beforeInfo, afterInfo);
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysAppIcons\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[{"Id":"11111111-1111-1111-1111-111111111111"}]}""");
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysSchema\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[]}""");
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
				body.Contains("\"ClientTypeId\"", StringComparison.Ordinal) &&
				body.Contains("\"LogoId\"", StringComparison.Ordinal) &&
				body.Contains("\"IconBackground\"", StringComparison.Ordinal) &&
				!body.Contains("\"EntitySchemaName\"", StringComparison.Ordinal)))
			.Returns(callInfo => {
				insertBody = callInfo.ArgAt<string>(1);
				return """{"success":true}""";
			});
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
				body.Contains("\"SectionSchemaUId\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[{"Id":"section-id","ApplicationId":"app-id","Caption":"{\"en-US\":\"Orders\"}","Code":"UsrOrders","Description":"Order workspace","EntitySchemaName":"UsrOrders","PackageId":"pkg-uid","SectionSchemaUId":"section-schema-uid","LogoId":"icon-id","IconBackground":null,"ClientTypeId":"195785B4-F55A-4E72-ACE3-6480B54C8FA5"}]}""");
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
				body.Contains("\"IconBackground\"", StringComparison.Ordinal) &&
				!body.Contains("\"LogoId\"", StringComparison.Ordinal) &&
				!body.Contains("\"operationType\"", StringComparison.Ordinal)))
			.Returns("""{"success":true}""");

		// Act
		ApplicationSectionCreateResult result = _sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(
				ApplicationCode: "UsrOrdersApp",
				Caption: "Orders",
				Description: "Order workspace",
				EntitySchemaName: null,
				WithMobilePages: false));

		// Assert
		result.PackageName.Should().Be("UsrOrdersApp",
			because: "the create result should preserve the target application's primary package");
		result.Section.Code.Should().Be("UsrOrders",
			because: "the section code should be generated from the caption");
		result.Section.EntitySchemaName.Should().Be("UsrOrders",
			because: "the section readback should expose the created entity schema name");
		result.Section.ClientTypeId.Should().Be("195785B4-F55A-4E72-ACE3-6480B54C8FA5",
			because: "web-only section creation should set the explicit web client type");
		result.Entity.Should().Be(entity,
			because: "the create result should include the created entity metadata from the refreshed app context");
		result.Pages.Should().ContainSingle(
			because: "the create result should return only the pages introduced by the section flow");
		result.Pages[0].SchemaName.Should().Be("UsrOrders_FormPage",
			because: "the page diff should surface the created section pages");
		insertBody.Should().NotBeNullOrWhiteSpace(
			because: "the section create flow should send an insert payload for the new section");
		insertBody.Should().Contain("\"value\":\"Orders\"",
			because: "the section caption should be inserted as plain text so the UI header is rendered correctly");
		insertBody.Should().NotContain("{\\u0022en-US\\u0022:\\u0022Orders\\u0022",
			because: "the section caption must not be serialized as a JSON string literal");
		result.Section.IconBackground.Should().MatchRegex("^#[0-9A-Fa-f]{6}$",
			because: "the create flow should set a valid hex color on the section via explicit UpdateQuery");
	}

	[Test]
	[Description("Creates an existing-entity section with mobile pages and omits the web-only client type selector from the insert payload.")]
	public void CreateSection_Should_Create_Existing_Entity_Section_With_Mobile_Pages() {
		// Arrange
		ApplicationEntityInfoResult existingEntity = new(
			"entity-uid",
			"UsrTaskStatus",
			"Task statuses",
			[]);
		ApplicationInfoResult beforeInfo = new(
			"pkg-uid",
			"UsrOrdersApp",
			[existingEntity],
			[],
			"app-id",
			"Orders App",
			"UsrOrdersApp",
			"8.3.0");
		ApplicationInfoResult afterInfo = new(
			"pkg-uid",
			"UsrOrdersApp",
			[existingEntity],
			[
				new PageListItem {
					SchemaName = "UsrTaskStatus_FormPage",
					UId = "page-mobile",
					PackageName = "UsrOrdersApp",
					ParentSchemaName = "BasePageV2"
				}
			],
			"app-id",
			"Orders App",
			"UsrOrdersApp",
			"8.3.0");
		_applicationInfoService.GetApplicationInfo("sandbox", null, "UsrOrdersApp")
			.Returns(beforeInfo, afterInfo);
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysAppIcons\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[{"Id":"11111111-1111-1111-1111-111111111111"}]}""");
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
				body.Contains("\"EntitySchemaName\"", StringComparison.Ordinal) &&
				body.Contains("\"LogoId\"", StringComparison.Ordinal) &&
				body.Contains("\"IconBackground\"", StringComparison.Ordinal) &&
				!body.Contains("\"ClientTypeId\"", StringComparison.Ordinal)))
			.Returns("""{"success":true}""");
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
				body.Contains("\"SectionSchemaUId\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[{"Id":"section-id","ApplicationId":"app-id","Caption":"{\"en-US\":\"Task statuses\"}","Code":"UsrTaskStatuses","Description":null,"EntitySchemaName":"UsrTaskStatus","PackageId":"pkg-uid","SectionSchemaUId":"section-schema-uid","LogoId":"icon-id","IconBackground":null,"ClientTypeId":null}]}""");
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
				body.Contains("\"IconBackground\"", StringComparison.Ordinal) &&
				!body.Contains("\"LogoId\"", StringComparison.Ordinal) &&
				!body.Contains("\"operationType\"", StringComparison.Ordinal)))
			.Returns("""{"success":true}""");

		// Act
		ApplicationSectionCreateResult result = _sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(
				ApplicationCode: "UsrOrdersApp",
				Caption: "Task statuses",
				Description: null,
				EntitySchemaName: "UsrTaskStatus",
				WithMobilePages: true));

		// Assert
		result.Section.EntitySchemaName.Should().Be("UsrTaskStatus",
			because: "existing-entity section creation should preserve the provided entity schema name");
		result.Section.ClientTypeId.Should().BeNull(
			because: "mobile section creation should omit the explicit web-only client type selector");
		result.Entity.Should().Be(existingEntity,
			because: "existing-entity section creation should resolve the targeted entity from refreshed app metadata");
		result.Pages.Should().ContainSingle(
			because: "the refreshed readback should still report pages created by the section flow");
		result.Section.IconBackground.Should().MatchRegex("^#[0-9A-Fa-f]{6}$",
			because: "the create flow should set a valid hex color on the section via explicit UpdateQuery");
	}

	[Test]
	[Description("Rejects requests that omit application-code before any remote calls are made.")]
	public void CreateSection_Should_Reject_Missing_ApplicationCode() {
		// Arrange
		ApplicationSectionCreateRequest request = new(
			string.Empty,
			"Orders");

		// Act
		Action action = () => _sut.CreateSection("sandbox", request);

		// Assert
		action.Should().Throw<ArgumentException>()
			.WithMessage("*application-code is required*",
				because: "the section-create contract now requires the installed application code");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
	}
}

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class CreateAppSectionOptionsTests {

	[Test]
	[Description("Returns true when WithMobilePagesValue is 'true' (case-insensitive)")]
	[TestCase("true")]
	[TestCase("True")]
	[TestCase("TRUE")]
	public void WithMobilePages_WhenValueIsTrue_ReturnsTrue(string value) {
		// Arrange
		CreateAppSectionOptions options = new() { WithMobilePagesValue = value };

		// Act
		bool result = options.WithMobilePages;

		// Assert
		result.Should().BeTrue(because: $"'{value}' is a valid truthy value for --with-mobile-pages");
	}

	[Test]
	[Description("Returns false when WithMobilePagesValue is 'false' (case-insensitive)")]
	[TestCase("false")]
	[TestCase("False")]
	[TestCase("FALSE")]
	public void WithMobilePages_WhenValueIsFalse_ReturnsFalse(string value) {
		// Arrange
		CreateAppSectionOptions options = new() { WithMobilePagesValue = value };

		// Act
		bool result = options.WithMobilePages;

		// Assert
		result.Should().BeFalse(because: $"'{value}' is a valid falsy value for --with-mobile-pages");
	}

	[Test]
	[Description("Returns false for invalid values — validation must be done via ValidateMobilePagesOption, not the getter")]
	[TestCase("0")]
	[TestCase("1")]
	[TestCase("no")]
	[TestCase("yes")]
	[TestCase("enabled")]
	[TestCase("xyz")]
	public void WithMobilePages_WhenValueIsInvalid_ReturnsFalse(string value) {
		// Arrange
		CreateAppSectionOptions options = new() { WithMobilePagesValue = value };

		// Act
		bool result = options.WithMobilePages;

		// Assert
		result.Should().BeFalse(because: $"'{value}' is not 'true', so the getter returns false; validation is separate");
	}

	[Test]
	[Description("Returns true when WithMobilePagesValue is null (default behavior)")]
	public void WithMobilePages_WhenValueIsNull_ReturnsTrue() {
		// Arrange
		CreateAppSectionOptions options = new() { WithMobilePagesValue = null };

		// Act
		bool result = options.WithMobilePages;

		// Assert
		result.Should().BeTrue(because: "null means the option was not provided, defaulting to true");
	}

	[Test]
	[Description("ValidateMobilePagesOption does not throw for null (default value)")]
	public void ValidateMobilePagesOption_WhenValueIsNull_DoesNotThrow() {
		// Arrange / Act
		Action action = () => CreateAppSectionOptions.ValidateMobilePagesOption(null);

		// Assert
		action.Should().NotThrow(because: "null means the CLI option was omitted and should use the default");
	}

	[Test]
	[Description("ValidateMobilePagesOption does not throw for 'true' or 'false' (case-insensitive)")]
	[TestCase("true")]
	[TestCase("True")]
	[TestCase("TRUE")]
	[TestCase("false")]
	[TestCase("False")]
	[TestCase("FALSE")]
	public void ValidateMobilePagesOption_WhenValueIsValid_DoesNotThrow(string value) {
		// Arrange / Act
		Action action = () => CreateAppSectionOptions.ValidateMobilePagesOption(value);

		// Assert
		action.Should().NotThrow(because: $"'{value}' is a valid boolean string for --with-mobile-pages");
	}

	[Test]
	[Description("ValidateMobilePagesOption throws ArgumentException for invalid values like '0', 'no', or typos")]
	[TestCase("0")]
	[TestCase("1")]
	[TestCase("no")]
	[TestCase("yes")]
	[TestCase("enabled")]
	[TestCase("xyz")]
	public void ValidateMobilePagesOption_WhenValueIsInvalid_ThrowsArgumentException(string value) {
		// Arrange / Act
		Action action = () => CreateAppSectionOptions.ValidateMobilePagesOption(value);

		// Assert
		action.Should().Throw<ArgumentException>()
			.WithMessage($"*{value}*",
				because: $"'{value}' is not a valid boolean value and must be rejected with a clear error message");
	}
}
