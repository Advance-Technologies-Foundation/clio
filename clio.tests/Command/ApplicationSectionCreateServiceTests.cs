using System;
using System.Collections.Generic;
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
public sealed class ApplicationSectionCreateServiceTests {
	private ISettingsRepository _settingsRepository = null!;
	private IApplicationClientFactory _applicationClientFactory = null!;
	private IApplicationClient _applicationClient = null!;
	private IServiceUrlBuilder _serviceUrlBuilder = null!;
	private IApplicationInfoService _applicationInfoService = null!;
	private ISysSettingsManager _sysSettingsManager = null!;
	private EnvironmentSettings _environmentSettings = null!;
	private ApplicationSectionCreateService _sut = null!;
	private ILogger _logger = null!;

	[SetUp]
	public void SetUp() {
		// Arrange
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_applicationInfoService = Substitute.For<IApplicationInfoService>();
		_logger = new NullLogger();
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
		_sysSettingsManager = Substitute.For<ISysSettingsManager>();
		_sysSettingsManager.GetSysSettingValueByCode("SchemaNamePrefix").Returns("Usr");
		ICaptionCultureResolver captionCultureResolver =
			Substitute.For<ICaptionCultureResolver>();
		captionCultureResolver.Resolve(Arg.Any<EnvironmentOptions>(), Arg.Any<string>())
			.Returns("en-US");
		_sut = CreateSutWithResolver(captionCultureResolver);
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
				body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
				body.Contains("\"IconBackground\"", StringComparison.Ordinal) &&
				body.Contains("\"filters\"", StringComparison.Ordinal)))
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
				body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
				body.Contains("\"IconBackground\"", StringComparison.Ordinal) &&
				body.Contains("\"filters\"", StringComparison.Ordinal)))
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
	[Description("When EntitySchemaName is provided, Creatio stores Code = EntitySchemaName on the section " +
		"instead of the caption-derived code sent in the INSERT. The readback must match by EntitySchemaName " +
		"so that the polling loop finds the section and returns success.")]
	public void CreateSection_WithPlatformEntity_Should_Match_Section_By_EntitySchemaName_As_Code() {
		// Arrange
		ApplicationEntityInfoResult entity = new("entity-uid", "Case", "Cases", []);
		ApplicationInfoResult appInfo = new(
			"pkg-uid", "UsrTaskApp", [entity], [], "app-id", "Task App", "UsrTaskApp", "8.3.0");
		_applicationInfoService.GetApplicationInfo("sandbox", null, "UsrTaskApp")
			.Returns(appInfo);
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysAppIcons\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[{"Id":"11111111-1111-1111-1111-111111111111"}]}""");
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
				body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
				body.Contains("\"Code\"", StringComparison.Ordinal) &&
				!body.Contains("\"filters\"", StringComparison.Ordinal)))
			.Returns("""{"success":true}""");
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
				body.Contains("\"SectionSchemaUId\"", StringComparison.Ordinal) &&
				body.Contains("\"filters\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[{"Id":"section-id","ApplicationId":"app-id","Caption":"My Case Section","Code":"Case","Description":null,"EntitySchemaName":"Case","PackageId":"pkg-uid","SectionSchemaUId":"section-schema-uid","LogoId":"icon-id","IconBackground":null,"ClientTypeId":null}]}""");
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
				body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
				body.Contains("\"IconBackground\"", StringComparison.Ordinal) &&
				body.Contains("\"filters\"", StringComparison.Ordinal)))
			.Returns("""{"success":true}""");

		// Act
		ApplicationSectionCreateResult result = _sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(
				ApplicationCode: "UsrTaskApp",
				Caption: "My Case Section",
				EntitySchemaName: "Case"));

		// Assert
		result.Section.Code.Should().Be("Case",
			because: "Creatio stores Code = EntitySchemaName for platform entity sections; " +
				"the readback must match this server-assigned code, not the caption-derived code sent in the INSERT");
		result.Section.EntitySchemaName.Should().Be("Case",
			because: "the entity schema name from the request should be reflected in the section result");
	}

	[Test]
	[Description("Section code uses the default Usr prefix when SchemaNamePrefix returns Usr.")]
	public void CreateSection_Should_Generate_Code_With_Usr_Prefix() {
		// Arrange
		_sysSettingsManager.GetSysSettingValueByCode("SchemaNamePrefix").Returns("Usr");
		SetUpPrefixTestMocks("UsrTestSection");

		// Act
		ApplicationSectionCreateResult result = _sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(ApplicationCode: "UsrOrdersApp", Caption: "TestSection"));

		// Assert
		result.Section.Code.Should().Be("UsrTestSection",
			because: "SchemaNamePrefix=Usr should produce the Usr-prefixed section code");
		result.Entity!.Name.Should().Be("UsrTestSection",
			because: "the entity schema name should match the prefix-derived section code");
	}

	[Test]
	[Description("Section code uses a custom prefix when SchemaNamePrefix returns a non-default value.")]
	public void CreateSection_Should_Generate_Code_With_Custom_Prefix() {
		// Arrange
		_sysSettingsManager.GetSysSettingValueByCode("SchemaNamePrefix").Returns("Tst");
		SetUpPrefixTestMocks("TstTestSection");

		// Act
		ApplicationSectionCreateResult result = _sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(ApplicationCode: "UsrOrdersApp", Caption: "TestSection"));

		// Assert
		result.Section.Code.Should().Be("TstTestSection",
			because: "SchemaNamePrefix=Tst should produce the Tst-prefixed section code instead of the hardcoded Usr");
		result.Entity!.Name.Should().Be("TstTestSection",
			because: "the entity schema name should match the prefix-derived section code");
	}

	[Test]
	[Description("Section code has no prefix when SchemaNamePrefix is empty.")]
	public void CreateSection_Should_Generate_Code_With_No_Prefix_When_Prefix_Is_Empty() {
		// Arrange
		_sysSettingsManager.GetSysSettingValueByCode("SchemaNamePrefix").Returns(string.Empty);
		SetUpPrefixTestMocks("TestSection");

		// Act
		ApplicationSectionCreateResult result = _sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(ApplicationCode: "UsrOrdersApp", Caption: "TestSection"));

		// Assert
		result.Section.Code.Should().Be("TestSection",
			because: "empty SchemaNamePrefix should produce an unprefixed section code");
		result.Entity!.Name.Should().Be("TestSection",
			because: "the entity schema name should match the unprefixed section code");
	}

	[Test]
	[Description("Section code inserts an underscore separator after the prefix when the caption starts with a digit, using the actual prefix length instead of the legacy hardcoded value.")]
	public void CreateSection_Should_Insert_Underscore_After_Prefix_When_Caption_Starts_With_Digit() {
		// Arrange
		_sysSettingsManager.GetSysSettingValueByCode("SchemaNamePrefix").Returns("MyPrefix");
		SetUpPrefixTestMocks("MyPrefix_2024Orders");

		// Act
		ApplicationSectionCreateResult result = _sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(ApplicationCode: "UsrOrdersApp", Caption: "2024 Orders"));

		// Assert
		result.Section.Code.Should().Be("MyPrefix_2024Orders",
			because: "the underscore separator must be inserted at the prefix boundary (length 8) so the identifier does not start with a digit");
		result.Entity!.Name.Should().Be("MyPrefix_2024Orders",
			because: "the entity schema name should match the prefix-derived section code");
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

	[Test]
	[Description("Rejects icon-background values that are not Freedom UI palette colors.")]
	public void CreateSection_Should_Reject_Non_Palette_Icon_Background() {
		ApplicationSectionCreateRequest request = new(
			"UsrOrdersApp",
			"Orders",
			IconBackground: "#FF00FF");

		Action action = () => _sut.CreateSection("sandbox", request);

		action.Should().Throw<ArgumentException>()
			.WithMessage("*Freedom UI palette*",
				because: "non-palette hex colors render as a white tile in the app manager UI");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
	}

	[Test]
	[Description("Rejects icon-background values that are not #RRGGBB hex strings.")]
	public void CreateSection_Should_Reject_Invalid_Format_Icon_Background() {
		ApplicationSectionCreateRequest request = new(
			"UsrOrdersApp",
			"Orders",
			IconBackground: "red");

		Action action = () => _sut.CreateSection("sandbox", request);

		action.Should().Throw<ArgumentException>()
			.WithMessage("*#RRGGBB format*",
				because: "non-hex strings are never valid palette values");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
	}

	[Test]
	[Description("When the culture resolver returns a non-en-US culture, ResolveLocalizedCaption returns the effective-culture value from the localized readback map.")]
	public void CreateSection_ShouldSurfaceEffectiveCultureCaption_WhenResolverReturnsNonEnUSCulture() {
		// Arrange
		ICaptionCultureResolver ukResolver = Substitute.For<ICaptionCultureResolver>();
		ukResolver.Resolve(Arg.Any<EnvironmentOptions>(), Arg.Any<string?>()).Returns("uk-UA");
		ApplicationSectionCreateService sut = CreateSutWithResolver(ukResolver);

		ApplicationEntityInfoResult entity = new("entity-uid", "UsrOrders", "Orders", []);
		ApplicationInfoResult beforeInfo = new(
			"pkg-uid", "UsrOrdersApp", [], [], "app-id", "Orders App", "UsrOrdersApp", "8.3.0");
		ApplicationInfoResult afterInfo = new(
			"pkg-uid", "UsrOrdersApp", [entity],
			[new PageListItem { SchemaName = "UsrOrders_FormPage", UId = "page-new", PackageName = "UsrOrdersApp", ParentSchemaName = "BasePageV2" }],
			"app-id", "Orders App", "UsrOrdersApp", "8.3.0");
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
				body.Contains("\"Code\"", StringComparison.Ordinal) &&
				!body.Contains("\"filters\"", StringComparison.Ordinal)))
			.Returns("""{"success":true}""");
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
				body.Contains("\"SectionSchemaUId\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[{"Id":"section-id","ApplicationId":"app-id","Caption":"{\"en-US\":\"Orders\",\"uk-UA\":\"Замовлення\"}","Code":"UsrOrders","Description":null,"EntitySchemaName":"UsrOrders","PackageId":"pkg-uid","SectionSchemaUId":"section-schema-uid","LogoId":"icon-id","IconBackground":null,"ClientTypeId":null}]}""");
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
				body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
				body.Contains("\"IconBackground\"", StringComparison.Ordinal) &&
				body.Contains("\"filters\"", StringComparison.Ordinal)))
			.Returns("""{"success":true}""");

		// Act
		ApplicationSectionCreateResult result = sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(ApplicationCode: "UsrOrdersApp", Caption: "Orders"));

		// Assert
		result.Section.Caption.Should().Be("Замовлення",
			because: "when the resolver returns uk-UA and the readback map has a uk-UA entry, " +
				"ResolveLocalizedCaption must return the effective-culture value, not the en-US fallback");
	}

	private ApplicationSectionCreateService CreateSutWithResolver(ICaptionCultureResolver resolver) {
		IServiceUrlBuilderFactory serviceUrlBuilderFactory = Substitute.For<IServiceUrlBuilderFactory>();
		serviceUrlBuilderFactory.Create(Arg.Any<EnvironmentSettings>()).Returns(_serviceUrlBuilder);
		return new ApplicationSectionCreateService(
			_settingsRepository,
			_applicationClientFactory,
			_serviceUrlBuilder,
			serviceUrlBuilderFactory,
			_applicationInfoService,
			_ => _sysSettingsManager,
			_logger,
			resolver);
	}

	[Test]
	[Description("Propagates the Creatio server error message and appends an actionable hint when the section insert is rejected for a reused entity.")]
	public void CreateSection_Should_Throw_Actionable_Error_When_Insert_Fails_With_Server_Message() {
		// Arrange
		SetUpInsertFailureMocks("""{"success":false,"errorInfo":{"message":"Cannot insert duplicate key row"}}""");

		// Act
		Action action = () => _sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(
				ApplicationCode: "UsrOrdersApp",
				Caption: "Contacts",
				EntitySchemaName: "Contact"));

		// Assert
		InvalidOperationException exception = action.Should().Throw<InvalidOperationException>(
			because: "a rejected section insert must surface as a readable failure").Which;
		exception.Message.Should().Contain("Cannot insert duplicate key row",
			because: "the underlying Creatio server error must be propagated instead of swallowed");
		exception.Message.Should().Contain("Contact",
			because: "the failure should name the entity the section was being bound to");
		exception.Message.Should().Contain("already bound to an existing section",
			because: "the message should explain the most common cause for a reused-entity insert failure");
		exception.Message.Should().Contain("list-app-sections",
			because: "the message should point the user at the recovery command");
	}

	[Test]
	[Description("Replaces the opaque 'InsertQuery failed.' fallback with an actionable message when the server rejects a reused-entity insert without a message.")]
	public void CreateSection_Should_Throw_Actionable_Error_When_Insert_Fails_Without_Server_Message_For_Reused_Entity() {
		// Arrange
		SetUpInsertFailureMocks("""{"success":false}""");

		// Act
		Action action = () => _sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(
				ApplicationCode: "UsrOrdersApp",
				Caption: "Contacts",
				EntitySchemaName: "Contact"));

		// Assert
		InvalidOperationException exception = action.Should().Throw<InvalidOperationException>(
			because: "a rejected section insert must surface as a readable failure").Which;
		exception.Message.Should().NotBe("InsertQuery failed.",
			because: "the opaque legacy fallback must be replaced with a diagnostic message");
		exception.Message.Should().Contain("Failed to create section",
			because: "the message should clearly state the operation that failed");
		exception.Message.Should().Contain("Contact",
			because: "the message should name the reused entity even when the server returns no detail");
		exception.Message.Should().Contain("already bound to an existing section",
			because: "the message should explain the common cause when no server message is available");
	}

	[Test]
	[Description("Attributes a new-object insert rejection to a duplicate section code without referencing entity binding when no entity-schema-name was supplied.")]
	public void CreateSection_Should_Throw_Actionable_Error_When_Insert_Fails_For_New_Object_Without_Server_Message() {
		// Arrange
		SetUpInsertFailureMocks("""{"success":false}""");

		// Act
		Action action = () => _sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(
				ApplicationCode: "UsrOrdersApp",
				Caption: "Orders"));

		// Assert
		InvalidOperationException exception = action.Should().Throw<InvalidOperationException>(
			because: "a rejected section insert must surface as a readable failure").Which;
		exception.Message.Should().Contain("a section with code",
			because: "the new-object path should attribute the failure to a duplicate section code");
		exception.Message.Should().Contain("UsrOrders",
			because: "the message should include the generated section code derived from the caption");
		exception.Message.Should().NotContain("bound to entity",
			because: "no entity-schema-name was provided, so the message must not reference entity binding");
	}

	[Test]
	[Description("Propagates the server error message when a new-object (no entity-schema-name) insert is rejected, without referencing entity binding in the diagnostic hint.")]
	public void CreateSection_Should_Propagate_Server_Message_When_Insert_Fails_For_New_Object() {
		// Arrange
		SetUpInsertFailureMocks("""{"success":false,"errorInfo":{"message":"Duplicate section code violation"}}""");

		// Act
		Action action = () => _sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(
				ApplicationCode: "UsrOrdersApp",
				Caption: "Orders"));

		// Assert
		InvalidOperationException exception = action.Should().Throw<InvalidOperationException>(
			because: "a rejected new-object insert must surface the server message").Which;
		exception.Message.Should().Contain("Duplicate section code violation",
			because: "the server error text must be propagated for new-object failures as well");
		exception.Message.Should().NotContain("bound to entity",
			because: "no entity-schema-name was provided, so entity-binding language must not appear");
	}

	[Test]
	[Description("Appends exactly one period after the server message when the message lacks terminal punctuation.")]
	public void CreateSection_Should_Append_Period_When_Server_Message_Has_No_Terminal_Punctuation() {
		// Arrange
		SetUpInsertFailureMocks("""{"success":false,"errorInfo":{"message":"Access denied"}}""");

		// Act
		Action action = () => _sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(
				ApplicationCode: "UsrOrdersApp",
				Caption: "Orders"));

		// Assert
		InvalidOperationException exception = action.Should().Throw<InvalidOperationException>(
			because: "a rejected insert must surface the server message").Which;
		exception.Message.Should().Contain("Access denied.",
			because: "a single period should be appended when the server message lacks terminal punctuation");
		exception.Message.Should().NotContain("Access denied..",
			because: "exactly one period should be appended, not two");
	}

	[Test]
	[Description("Does not append a period when the server message already ends with a period.")]
	public void CreateSection_Should_Not_Double_Period_When_Server_Message_Ends_With_Period() {
		// Arrange
		SetUpInsertFailureMocks("""{"success":false,"errorInfo":{"message":"Constraint violation."}}""");

		// Act
		Action action = () => _sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(
				ApplicationCode: "UsrOrdersApp",
				Caption: "Orders"));

		// Assert
		InvalidOperationException exception = action.Should().Throw<InvalidOperationException>(
			because: "a rejected insert must surface the server message").Which;
		exception.Message.Should().NotContain("Constraint violation..",
			because: "a period must not be appended when the server message already ends with one");
	}

	[Test]
	[Description("Uses the fallback message and omits 'Server error:' when the server returns an empty error message.")]
	public void CreateSection_Should_Use_Fallback_When_Server_Returns_Empty_Error_Message() {
		// Arrange
		SetUpInsertFailureMocks("""{"success":false,"errorInfo":{"message":""}}""");

		// Act
		Action action = () => _sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(
				ApplicationCode: "UsrOrdersApp",
				Caption: "Orders"));

		// Assert
		InvalidOperationException exception = action.Should().Throw<InvalidOperationException>(
			because: "a rejected insert with an empty error message should use the fallback text").Which;
		exception.Message.Should().Contain("The server rejected the section insert",
			because: "the fallback message should appear when errorInfo.message is empty");
		exception.Message.Should().NotContain("Server error:",
			because: "the 'Server error:' prefix must only appear when there is a non-empty server message");
	}

	[Test]
	[Description("Does not append a period after a server message that ends with '!' or '?'.")]
	[TestCase("Access denied!")]
	[TestCase("Are you authorized?")]
	public void CreateSection_Should_Not_Append_Period_After_Terminal_Punctuation(string serverMessage) {
		// Arrange
		SetUpInsertFailureMocks($$$"""{"success":false,"errorInfo":{"message":"{{{serverMessage}}}"}}""");

		// Act
		Action action = () => _sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(
				ApplicationCode: "UsrOrdersApp",
				Caption: "Orders"));

		// Assert
		InvalidOperationException exception = action.Should().Throw<InvalidOperationException>(
			because: "a rejected insert must surface the server message without modification").Which;
		exception.Message.Should().Contain(serverMessage,
			because: "the server message should appear verbatim in the exception");
		exception.Message.Should().NotContain(serverMessage + ".",
			because: "a period must not be appended after '!' or '?'");
	}

	private void SetUpInsertFailureMocks(string insertResponseJson) {
		ApplicationInfoResult beforeInfo = new(
			"pkg-uid", "UsrOrdersApp", [], [], "app-id", "Orders App", "UsrOrdersApp", "8.3.0");
		_applicationInfoService.GetApplicationInfo("sandbox", null, "UsrOrdersApp")
			.Returns(beforeInfo);
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
				body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
				!body.Contains("\"filters\"", StringComparison.Ordinal)))
			.Returns(insertResponseJson);
	}

	private void SetUpPrefixTestMocks(string expectedCode) {
		ApplicationEntityInfoResult entity = new("entity-uid", expectedCode, "TestSection", []);
		ApplicationInfoResult beforeInfo = new(
			"pkg-uid", "UsrOrdersApp", [], [], "app-id", "Orders App", "UsrOrdersApp", "8.3.0");
		ApplicationInfoResult afterInfo = new(
			"pkg-uid", "UsrOrdersApp", [entity],
			[new PageListItem { SchemaName = $"{expectedCode}_FormPage", UId = "page-new", PackageName = "UsrOrdersApp", ParentSchemaName = "BasePageV2" }],
			"app-id", "Orders App", "UsrOrdersApp", "8.3.0");
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
				body.Contains("\"Code\"", StringComparison.Ordinal) &&
				!body.Contains("\"filters\"", StringComparison.Ordinal)))
			.Returns("""{"success":true}""");
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
				body.Contains("\"SectionSchemaUId\"", StringComparison.Ordinal)))
			.Returns($$$"""{"success":true,"rows":[{"Id":"section-id","ApplicationId":"app-id","Caption":"TestSection","Code":"{{{expectedCode}}}","Description":"","EntitySchemaName":"{{{expectedCode}}}","PackageId":"pkg-uid","SectionSchemaUId":"section-schema-uid","LogoId":"icon-id","IconBackground":null,"ClientTypeId":"195785B4-F55A-4E72-ACE3-6480B54C8FA5"}]}""");
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
				body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
				body.Contains("\"IconBackground\"", StringComparison.Ordinal) &&
				body.Contains("\"filters\"", StringComparison.Ordinal)))
			.Returns("""{"success":true}""");
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
