using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
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

	[TearDown]
	public void TearDown() {
		Environment.SetEnvironmentVariable(
			ApplicationSectionCreateService.InsertTimeoutEnvironmentVariable, null);
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
				!body.Contains("\"EntitySchemaName\"", StringComparison.Ordinal)),
			Arg.Any<int>())
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
		StubExistingEntitySchema("UsrTaskStatus");
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
				body.Contains("\"EntitySchemaName\"", StringComparison.Ordinal) &&
				body.Contains("\"LogoId\"", StringComparison.Ordinal) &&
				body.Contains("\"IconBackground\"", StringComparison.Ordinal) &&
				!body.Contains("\"ClientTypeId\"", StringComparison.Ordinal)),
			Arg.Any<int>())
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
		StubExistingEntitySchema("Case");
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
				body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
				body.Contains("\"Code\"", StringComparison.Ordinal) &&
				!body.Contains("\"filters\"", StringComparison.Ordinal)),
			Arg.Any<int>())
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
				!body.Contains("\"filters\"", StringComparison.Ordinal)),
			Arg.Any<int>())
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

	[Test]
	[Description("Rejects a Cyrillic section caption when the effective culture is the Latin-script en-US profile, before any remote call.")]
	public void CreateSection_ShouldThrow_WhenCaptionScriptDoesNotMatchEnUsProfileCulture() {
		// Arrange
		// The default fixture resolver returns en-US; a Cyrillic caption would be stored under the
		// English profile and render foreign-language labels (the ENG-91044 regression).
		ApplicationSectionCreateRequest request = new(
			ApplicationCode: "UsrOrdersApp",
			Caption: "Заявки");

		// Act
		Action action = () => _sut.CreateSection("sandbox", request);

		// Assert
		action.Should().Throw<EntitySchemaDesignerException>(
				because: "a Cyrillic caption must not be stored under the Latin-script en-US profile culture")
			.Which.Message.Should().Contain("en-US",
				because: "the error must name the effective culture so the caller can fix the language");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
		_applicationInfoService.DidNotReceiveWithAnyArgs().GetApplicationInfo(default!, default, default);
	}

	[Test]
	[Description("A caption-culture override does not bypass the section guard: the caption is validated against the profile culture because the stored caption is localized under the profile, not the readback override.")]
	public void CreateSection_ShouldThrow_WhenCaptionCultureOverrideMasksNonProfileScript() {
		// Arrange
		// Profile is en-US (override = null resolves to the profile); the caller passes a uk-UA readback
		// override. Because the stored section caption is localized under the en-US profile, a Cyrillic
		// caption must still be rejected — the override must NOT smuggle it past the guard.
		ICaptionCultureResolver resolver = Substitute.For<ICaptionCultureResolver>();
		resolver.Resolve(Arg.Any<EnvironmentOptions>(), Arg.Any<string?>()).Returns("en-US");
		resolver.Resolve(Arg.Any<EnvironmentOptions>(), "uk-UA").Returns("uk-UA");
		ApplicationSectionCreateService sut = CreateSutWithResolver(resolver);
		ApplicationSectionCreateRequest request = new(
			ApplicationCode: "UsrOrdersApp",
			Caption: "Заявки",
			CaptionCulture: "uk-UA");

		// Act
		Action action = () => sut.CreateSection("sandbox", request);

		// Assert
		action.Should().Throw<EntitySchemaDesignerException>(
				because: "for sections the caption-culture override is readback-only; the caption is stored under the en-US profile, so a Cyrillic caption must still be rejected")
			.Which.Message.Should().Contain("en-US",
				because: "the guard must validate against the resolved profile culture, not the readback override");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
		_applicationInfoService.DidNotReceiveWithAnyArgs().GetApplicationInfo(default!, default, default);
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
			resolver,
			new SectionCreateSerializationGuard(_logger));
	}

	[Test]
	[Description("Propagates the Creatio server error message and appends an actionable hint when the section insert is rejected for a reused entity.")]
	public void CreateSection_Should_Throw_Actionable_Error_When_Insert_Fails_With_Server_Message() {
		// Arrange
		SetUpInsertFailureMocks("""{"success":false,"errorInfo":{"message":"Cannot insert duplicate key row"}}""");
		StubExistingEntitySchema("Contact");

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
		exception.Message.Should().NotContain("already bound to an existing section",
			because: "Creatio allows several sections per entity, so the failure must not assert the false entity-binding cause");
		exception.Message.Should().Contain("list-app-sections",
			because: "the message should point the user at the recovery command");
		exception.Message.Should().NotContain("did not detail",
			because: "this message is reached only for a DETAILED server rejection, so the tail must not claim the server gave no detail (F6)");
		exception.Message.Should().NotContain("without returning a detailed message",
			because: "the dead detail-less else branch was removed, so its contradictory text must not appear (F6)");
	}

	[Test]
	[Description("Classifies a detail-less reused-entity insert rejection as a retryable contention failure (ENG-93089) rather than a terminal server error, with serialize/retry guidance and section-created=null when the post-failure verification could not run.")]
	public void CreateSection_Should_Classify_DetailLess_Reused_Entity_Rejection_As_Contention() {
		// Arrange
		SetUpInsertFailureMocks("""{"success":false}""");
		StubExistingEntitySchema("Contact");

		// Act
		Action action = () => _sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(
				ApplicationCode: "UsrOrdersApp",
				Caption: "Contacts",
				EntitySchemaName: "Contact"));

		// Assert
		ApplicationSectionCreateException exception = action.Should().Throw<ApplicationSectionCreateException>(
			because: "a detail-less rejection is the contention signature and must surface as a classified failure").Which;
		exception.FailureClass.Should().Be(ApplicationSectionCreateFailureClass.Contention,
			because: "a detail-less 'InsertQuery failed' rejection is contention (retryable), not a terminal server error");
		exception.SectionCreated.Should().BeNull(
			because: "the verification readback was not configured here, so the section state is unknown, not proven absent");
		exception.RetryGuidance.Should().Contain("one at a time",
			because: "contention guidance must tell the agent to create sections sequentially, not in parallel");
		exception.RetryGuidance.Should().Contain("list-app-sections",
			because: "the guidance should point the agent at the verification command before a manual retry");
		exception.RetryGuidance.Should().Contain("server-side",
			because: "a detail-less rejection may be a server-side failure unrelated to concurrency, so the guidance must not claim contention as the only cause (ENG-93089 C4)");
		exception.RetryGuidance.Should().Contain("--code",
			because: "a detail-less rejection can also be a plain code collision, so the guidance must restore the actionable --code recovery hint (F5)");
		exception.RetryGuidance.Should().Contain("may already exist",
			because: "the guidance must tell the agent a section with the generated or explicit code may already exist (F5)");
		exception.Message.Should().NotContain("already bound to an existing section",
			because: "Creatio allows several sections per entity, so the message must not assert the false entity-binding cause");
	}

	[Test]
	[Description("Classifies a detail-less new-object insert rejection as a retryable contention failure (ENG-93089), naming the generated section code and not the (unset) entity binding.")]
	public void CreateSection_Should_Classify_DetailLess_New_Object_Rejection_As_Contention() {
		// Arrange
		SetUpInsertFailureMocks("""{"success":false}""");

		// Act
		Action action = () => _sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(
				ApplicationCode: "UsrOrdersApp",
				Caption: "Orders"));

		// Assert
		ApplicationSectionCreateException exception = action.Should().Throw<ApplicationSectionCreateException>(
			because: "a detail-less new-object rejection is the contention signature and must surface as a classified failure").Which;
		exception.FailureClass.Should().Be(ApplicationSectionCreateFailureClass.Contention,
			because: "a detail-less rejection is contention (retryable), not a terminal server error");
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
	[Description("Treats an empty server error message as detail-less and classifies it as a retryable contention failure (ENG-93089) rather than a terminal server error.")]
	public void CreateSection_Should_Classify_Empty_Error_Message_Rejection_As_Contention() {
		// Arrange
		SetUpInsertFailureMocks("""{"success":false,"errorInfo":{"message":""}}""");

		// Act
		Action action = () => _sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(
				ApplicationCode: "UsrOrdersApp",
				Caption: "Orders"));

		// Assert
		ApplicationSectionCreateException exception = action.Should().Throw<ApplicationSectionCreateException>(
			because: "an empty error message is detail-less and must be treated as contention").Which;
		exception.FailureClass.Should().Be(ApplicationSectionCreateFailureClass.Contention,
			because: "an empty errorInfo.message is the same detail-less rejection as a bare 'InsertQuery failed'");
		exception.RetryGuidance.Should().Contain("one at a time",
			because: "contention guidance must steer the agent to sequential section creation");
	}

	[Test]
	[Description("A detailed rejection (a real server message) stays a terminal server-error, not contention, so genuine failures are never masked by the retry path (ENG-93089 AC-02).")]
	public void CreateSection_Should_Keep_Detailed_Rejection_As_ServerError_Not_Contention() {
		// Arrange
		SetUpInsertFailureMocks("""{"success":false,"errorInfo":{"message":"Section with this code already exists"}}""");

		// Act
		Action action = () => _sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(ApplicationCode: "UsrOrdersApp", Caption: "Orders"));

		// Assert
		ApplicationSectionCreateException exception = action.Should().Throw<ApplicationSectionCreateException>(
			because: "a detailed rejection must still surface as a classified failure").Which;
		exception.FailureClass.Should().Be(ApplicationSectionCreateFailureClass.ServerError,
			because: "a rejection that carries a real server message is a genuine error, not detail-less contention");
		exception.SectionCreated.Should().BeFalse(
			because: "an explicit detailed rejection guarantees the section was not created");
		exception.Message.Should().Contain("Section with this code already exists",
			because: "the detailed server message must be propagated so the agent sees the real cause");
	}

	[Test]
	[Description("On a detail-less contention rejection whose section is nonetheless already committed, verification by the generated id short-circuits to the readback and issues NO second insert — no duplicate (ENG-93089 AC-03).")]
	public void CreateSection_Should_Not_Retry_And_Return_Readback_When_Contention_Section_Is_Visible() {
		// Arrange
		SetUpDetailLessInsertWithReadback(sectionVisibleOnVerify: true);

		// Act
		ApplicationSectionCreateResult result = _sut.CreateSection("sandbox", CreateReuseEntityRequest());

		// Assert
		result.Section.Code.Should().Be("UsrOrders",
			because: "a section that committed despite the aborted response must be returned via readback, not re-created");
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal)
				&& body.Contains("\"columnValues\"", StringComparison.Ordinal)
				&& !body.Contains("\"filters\"", StringComparison.Ordinal)),
			Arg.Any<int>());
	}

	[Test]
	[Description("On the contention-prone path (enableContentionRetry:true — the MCP/background path), a detail-less contention rejection with the section verified absent makes clio retry the insert exactly once and return the recovered section (ENG-93089 AC-04, F9).")]
	public void CreateSection_Should_Retry_Once_And_Recover_When_Contention_Section_Is_Absent() {
		// Arrange
		SetUpDetailLessThenSuccessfulInsertWithReadback();

		// Act — enableContentionRetry:true turns on the auto-retry (the MCP/background path); it is an
		// explicit flag now, no longer derived from the insert-timeout override.
		ApplicationSectionCreateResult result = _sut.CreateSection(
			"sandbox", CreateReuseEntityRequest(), insertTimeoutMsOverride: 600_000, enableContentionRetry: true);

		// Assert
		result.Section.Code.Should().Be("UsrOrders",
			because: "the retry after a verified-absent contention rejection should recover and return the created section");
		_applicationClient.Received(2).ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal)
				&& body.Contains("\"columnValues\"", StringComparison.Ordinal)
				&& !body.Contains("\"filters\"", StringComparison.Ordinal)),
			Arg.Any<int>());
	}

	[Test]
	[Description("When the single retry after a verified-absent contention rejection also aborts detail-lessly (and the re-verify still shows the section absent), clio stops (bounded to one retry — no unbounded loop) and throws the classified contention failure with section-created=false (ENG-93089 AC-06, F2/F9).")]
	public void CreateSection_Should_Stop_After_One_Retry_When_Contention_Persists() {
		// Arrange
		SetUpDetailLessInsertWithReadback(sectionVisibleOnVerify: false);

		// Act — enableContentionRetry:true turns on the auto-retry path that this test exercises.
		Action action = () => _sut.CreateSection(
			"sandbox", CreateReuseEntityRequest(), insertTimeoutMsOverride: 600_000, enableContentionRetry: true);

		// Assert
		ApplicationSectionCreateException exception = action.Should().Throw<ApplicationSectionCreateException>(
			because: "a persistent detail-less rejection must surface as a classified contention failure").Which;
		exception.FailureClass.Should().Be(ApplicationSectionCreateFailureClass.Contention,
			because: "a persistent detail-less rejection is still contention, not a terminal server error");
		exception.SectionCreated.Should().BeFalse(
			because: "the section was verified absent before AND after the retry, so it was never created");
		_applicationClient.Received(2).ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal)
				&& body.Contains("\"columnValues\"", StringComparison.Ordinal)
				&& !body.Contains("\"filters\"", StringComparison.Ordinal)),
			Arg.Any<int>());
	}

	[Test]
	[Description("On the bare synchronous CLI path (no insert-timeout override), a first-attempt detail-less rejection that verifies absent throws the classified contention failure immediately and issues NO second insert — one command per process never contends, so the destructive retry is skipped (F9).")]
	public void CreateSection_Should_Not_Retry_On_Cli_Path_When_Contention_Section_Is_Absent() {
		// Arrange
		SetUpDetailLessInsertWithReadback(sectionVisibleOnVerify: false);

		// Act — no override, so contentionRetryEnabled is false and the second insert must be skipped.
		Action action = () => _sut.CreateSection("sandbox", CreateReuseEntityRequest());

		// Assert
		ApplicationSectionCreateException exception = action.Should().Throw<ApplicationSectionCreateException>(
			because: "a verified-absent detail-less rejection on the CLI path must fail fast as a classified contention failure").Which;
		exception.FailureClass.Should().Be(ApplicationSectionCreateFailureClass.Contention,
			because: "a detail-less rejection is still contention even when the retry is skipped");
		exception.SectionCreated.Should().BeFalse(
			because: "the section was verified absent, so it was never created");
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal)
				&& body.Contains("\"columnValues\"", StringComparison.Ordinal)
				&& !body.Contains("\"filters\"", StringComparison.Ordinal)),
			Arg.Any<int>());
	}

	[Test]
	[Description("FIX B (ENG-93089 #3581998934): when the retry insert throws a TERMINAL detailed ServerError (a duplicate/constraint rejection) but the section is nonetheless visible by its generated id on re-verify — insert #1 committed yet answered detail-less — clio treats it as committed, swallows the ServerError, and returns the section via the outside readback instead of surfacing a terminal failure.")]
	public void CreateSection_Should_Recover_When_Retry_Terminal_ServerError_But_Section_Is_Visible() {
		// Arrange
		SetUpDetailLessThenServerErrorInsertWithVisibleSection();

		// Act — enableContentionRetry:true takes the retry leg; the retry's detailed ServerError is
		// re-verified by id before surfacing.
		ApplicationSectionCreateResult result = _sut.CreateSection(
			"sandbox", CreateReuseEntityRequest(), enableContentionRetry: true);

		// Assert
		result.Section.Code.Should().Be("UsrOrders",
			because: "the section committed by insert #1 must be returned via readback rather than surfaced as the retry's terminal ServerError");
		_applicationClient.Received(2).ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal)
				&& body.Contains("\"columnValues\"", StringComparison.Ordinal)
				&& !body.Contains("\"filters\"", StringComparison.Ordinal)),
			Arg.Any<int>());
	}

	[Test]
	[Description("FIX C (ENG-93089 #3581998739): the auto-retry is driven by the explicit enableContentionRetry flag, decoupled from the insert-timeout override — enableContentionRetry:true retries after a verified-absent detail-less rejection even with NO insert-timeout override supplied.")]
	public void CreateSection_Should_Retry_When_ContentionRetry_Enabled_Without_Timeout_Override() {
		// Arrange
		SetUpDetailLessThenSuccessfulInsertWithReadback();

		// Act — no insertTimeoutMsOverride; retry is enabled purely by the explicit flag.
		ApplicationSectionCreateResult result = _sut.CreateSection(
			"sandbox", CreateReuseEntityRequest(), enableContentionRetry: true);

		// Assert
		result.Section.Code.Should().Be("UsrOrders",
			because: "the explicit enableContentionRetry flag must enable the retry independently of the insert-timeout override");
		_applicationClient.Received(2).ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal)
				&& body.Contains("\"columnValues\"", StringComparison.Ordinal)
				&& !body.Contains("\"filters\"", StringComparison.Ordinal)),
			Arg.Any<int>());
	}

	[Test]
	[Description("FIX C (ENG-93089 #3581998739): supplying an insert-timeout override alone no longer enables the auto-retry — with enableContentionRetry left at its default false, a verified-absent detail-less rejection fails fast and issues NO second insert even when a generous insert-timeout override is set.")]
	public void CreateSection_Should_Not_Retry_When_Timeout_Override_Set_But_ContentionRetry_Disabled() {
		// Arrange
		SetUpDetailLessInsertWithReadback(sectionVisibleOnVerify: false);

		// Act — a generous override but the retry flag stays default false, so the retry must be skipped.
		Action action = () => _sut.CreateSection(
			"sandbox", CreateReuseEntityRequest(), insertTimeoutMsOverride: 600_000);

		// Assert
		ApplicationSectionCreateException exception = action.Should().Throw<ApplicationSectionCreateException>(
			because: "the insert-timeout override no longer implies contention retry, so a verified-absent rejection fails fast").Which;
		exception.FailureClass.Should().Be(ApplicationSectionCreateFailureClass.Contention,
			because: "a verified-absent detail-less rejection is still classified contention when the retry is disabled");
		_applicationClient.Received(1).ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal)
				&& body.Contains("\"columnValues\"", StringComparison.Ordinal)
				&& !body.Contains("\"filters\"", StringComparison.Ordinal)),
			Arg.Any<int>());
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

	[Test]
	[Description("Passes the default 90-second budget to the ApplicationSection insert when no override is configured.")]
	public void CreateSection_Should_Pass_Default_Insert_Timeout_When_EnvVar_Not_Set() {
		// Arrange
		Environment.SetEnvironmentVariable(
			ApplicationSectionCreateService.InsertTimeoutEnvironmentVariable, null);
		SetUpInsertTimeoutCaptureMocks();
		// Act
		Action act = () => _sut.CreateSection("sandbox", CreateReuseEntityRequest());
		// Assert
		act.Should().Throw<ApplicationSectionCreateException>(
			because: "the rejected insert should surface as a classified failure");
		_capturedInsertTimeout.Should().Be(90_000,
			because: "the insert budget should default to 90 seconds (below the MCP client request ceiling) when no env override is set");
	}

	[Test]
	[Description("Passes the env-var-overridden budget to the ApplicationSection insert when CLIO_CREATE_SECTION_TIMEOUT_SECONDS is set.")]
	public void CreateSection_Should_Pass_Overridden_Insert_Timeout_When_EnvVar_Set() {
		// Arrange
		Environment.SetEnvironmentVariable(
			ApplicationSectionCreateService.InsertTimeoutEnvironmentVariable, "42");
		SetUpInsertTimeoutCaptureMocks();
		// Act
		Action act = () => _sut.CreateSection("sandbox", CreateReuseEntityRequest());
		// Assert
		act.Should().Throw<ApplicationSectionCreateException>(
			because: "the rejected insert should surface as a classified failure");
		_capturedInsertTimeout.Should().Be(42_000,
			because: "the env-var override is expressed in whole seconds and converted to milliseconds");
	}

	[TestCase("abc")]
	[TestCase("0")]
	[TestCase("-5")]
	[Description("Falls back to the default budget when CLIO_CREATE_SECTION_TIMEOUT_SECONDS is non-numeric or non-positive.")]
	public void CreateSection_Should_Fall_Back_To_Default_Timeout_When_EnvVar_Invalid(string invalidValue) {
		// Arrange
		Environment.SetEnvironmentVariable(
			ApplicationSectionCreateService.InsertTimeoutEnvironmentVariable, invalidValue);
		SetUpInsertTimeoutCaptureMocks();
		// Act
		Action act = () => _sut.CreateSection("sandbox", CreateReuseEntityRequest());
		// Assert
		act.Should().Throw<ApplicationSectionCreateException>(
			because: "the rejected insert should surface as a classified failure");
		_capturedInsertTimeout.Should().Be(90_000,
			because: "invalid override values must not silently disable or corrupt the insert budget");
	}

	[Test]
	[Description("Passes the explicit 600 000 ms override (the MCP background path) to the 3-arg ApplicationSection insert so the section commits server-side after the response deadline returns early.")]
	public void CreateSection_Should_Pass_Explicit_Override_To_Three_Arg_Insert_When_Override_Provided() {
		// Arrange — no env var, so only the explicit override is in play.
		Environment.SetEnvironmentVariable(
			ApplicationSectionCreateService.InsertTimeoutEnvironmentVariable, null);
		SetUpInsertTimeoutCaptureMocks();
		// Act
		Action act = () => _sut.CreateSection("sandbox", CreateReuseEntityRequest(), insertTimeoutMsOverride: 600_000);
		// Assert
		act.Should().Throw<ApplicationSectionCreateException>(
			because: "the rejected insert should surface as a classified failure");
		_capturedInsertTimeout.Should().Be(600_000,
			because: "the explicit override is the linchpin that lets the background insert outlive the response deadline, so it must reach the 3-arg ExecutePostRequest verbatim");
	}

	[Test]
	[Description("Lets the explicit insert-timeout override win over the CLIO_CREATE_SECTION_TIMEOUT_SECONDS env var so the MCP background path is not capped by an operator's env value.")]
	public void CreateSection_Should_Prefer_Explicit_Override_Over_EnvVar_When_Both_Set() {
		// Arrange — env var set to a small value that the explicit override must beat.
		Environment.SetEnvironmentVariable(
			ApplicationSectionCreateService.InsertTimeoutEnvironmentVariable, "42");
		SetUpInsertTimeoutCaptureMocks();
		// Act
		Action act = () => _sut.CreateSection("sandbox", CreateReuseEntityRequest(), insertTimeoutMsOverride: 600_000);
		// Assert
		act.Should().Throw<ApplicationSectionCreateException>(
			because: "the rejected insert should surface as a classified failure");
		_capturedInsertTimeout.Should().Be(600_000,
			because: "an explicit override must take precedence over the env var, otherwise the background path could be silently capped");
	}

	[TestCase(0)]
	[TestCase(-1)]
	[Description("Ignores a non-positive insert-timeout override and falls through to the env-var/default budget, never passing a zero or negative timeout to the insert.")]
	public void CreateSection_Should_Ignore_NonPositive_Override_And_Fall_Through(int nonPositiveOverride) {
		// Arrange — no env var, so the fallthrough must land on the 90 s default.
		Environment.SetEnvironmentVariable(
			ApplicationSectionCreateService.InsertTimeoutEnvironmentVariable, null);
		SetUpInsertTimeoutCaptureMocks();
		// Act
		Action act = () => _sut.CreateSection("sandbox", CreateReuseEntityRequest(), insertTimeoutMsOverride: nonPositiveOverride);
		// Assert
		act.Should().Throw<ApplicationSectionCreateException>(
			because: "the rejected insert should surface as a classified failure");
		_capturedInsertTimeout.Should().Be(90_000,
			because: "a non-positive override (the 'is > 0' guard) must fall through to the default budget, not disable the insert timeout");
	}

	[Test]
	[Description("Bounds each success-path readback HTTP call with the explicit override (the MCP background path) so a readback Creatio accepts but never answers cannot hold a thread + connection after the response deadline returns early (ENG-91316).")]
	public void CreateSection_Should_Bound_Success_Readback_When_Readback_Override_Provided() {
		// Arrange
		SetUpSuccessfulCreateWithReadbackCapture();
		// Act
		_ = _sut.CreateSection("sandbox", CreateReuseEntityRequest(), readbackTimeoutMsOverride: 30_000);
		// Assert
		_capturedReadbackTimeout.Should().Be(30_000,
			because: "the background/MCP path must pass a finite per-request readback budget so a wedged readback cannot park a thread-pool worker for the life of the long-lived server process");
	}

	[Test]
	[Description("Leaves the success-path readback at Timeout.Infinite on the synchronous CLI path (no override), preserving the patient local-user behavior.")]
	public void CreateSection_Should_Use_Infinite_Success_Readback_When_No_Readback_Override() {
		// Arrange
		SetUpSuccessfulCreateWithReadbackCapture();
		// Act
		_ = _sut.CreateSection("sandbox", CreateReuseEntityRequest());
		// Assert
		_capturedReadbackTimeout.Should().Be(System.Threading.Timeout.Infinite,
			because: "the CLI path passes no readback override, so the readback must keep its patient Timeout.Infinite default");
	}

	[TestCase(0)]
	[TestCase(-1)]
	[Description("Ignores a non-positive readback override and keeps Timeout.Infinite, never passing a zero or negative timeout to the readback HTTP calls.")]
	public void CreateSection_Should_Ignore_NonPositive_Readback_Override_And_Keep_Infinite(int nonPositiveOverride) {
		// Arrange
		SetUpSuccessfulCreateWithReadbackCapture();
		// Act
		_ = _sut.CreateSection("sandbox", CreateReuseEntityRequest(), readbackTimeoutMsOverride: nonPositiveOverride);
		// Assert
		_capturedReadbackTimeout.Should().Be(System.Threading.Timeout.Infinite,
			because: "a non-positive readback override must fall through to the patient default, not disable or corrupt the readback timeout");
	}

	[Test]
	[Description("Classifies a connection-level WebException as a transport failure that never reached Creatio and is safe to retry.")]
	public void CreateSection_Should_Throw_Transport_Classified_Failure_When_Connect_Fails() {
		// Arrange
		SetUpInsertThrowingMocks(new WebException("Connection refused", WebExceptionStatus.ConnectFailure));
		// Act
		Action act = () => _sut.CreateSection("sandbox", CreateReuseEntityRequest());
		// Assert
		ApplicationSectionCreateException exception = act.Should().Throw<ApplicationSectionCreateException>(
				because: "a connect failure is a classified transport failure")
			.Which;
		exception.FailureClass.Should().Be(ApplicationSectionCreateFailureClass.Transport,
			because: "a connect failure means the request never reached the Creatio server");
		exception.SectionCreated.Should().BeFalse(
			because: "no side effect is possible when the request never reached the server");
		exception.RetryGuidance.Should().Contain("retrying is safe",
			because: "transport failures must tell the agent that a retry cannot double-create the section");
	}

	[Test]
	[Description("Classifies a network failure during the preparation reads (before the insert) as a side-effect-free failure that is safe to retry.")]
	public void CreateSection_Should_Throw_Classified_Preparation_Failure_When_AppInfo_Read_Fails() {
		// Arrange
		_applicationInfoService.GetApplicationInfo("sandbox", null, "UsrOrdersApp")
			.Returns(_ => throw new WebException("Connection refused", WebExceptionStatus.ConnectFailure));
		// Act
		Action act = () => _sut.CreateSection("sandbox", CreateReuseEntityRequest());
		// Assert
		ApplicationSectionCreateException exception = act.Should().Throw<ApplicationSectionCreateException>(
				because: "network failures during preparation reads should be classified too")
			.Which;
		exception.FailureClass.Should().Be(ApplicationSectionCreateFailureClass.Transport,
			because: "the connect failure happened before any request reached the DataService insert route");
		exception.SectionCreated.Should().BeFalse(
			because: "preparation reads run before the insert, so no side effect is possible");
		exception.RetryGuidance.Should().Contain("retrying is safe",
			because: "a failure before the insert is guaranteed side-effect-free");
		exception.Message.Should().Contain("before the section insert was attempted",
			because: "the message must make clear that the destructive step never ran");
	}

	[Test]
	[Description("Recovers and returns the normal success result when the insert response times out but the section created by this call is already visible on verification readback.")]
	public void CreateSection_Should_Return_Result_When_Insert_Times_Out_But_Section_Is_Visible() {
		// Arrange
		SetUpTimedOutInsertWithReadbackMocks();
		// Act
		ApplicationSectionCreateResult result = _sut.CreateSection("sandbox", CreateReuseEntityRequest());
		// Assert
		result.Section.Code.Should().Be("UsrOrders",
			because: "a timed-out insert whose section is already visible must be treated as a recovered success");
	}

	[Test]
	[Description("After a timed-out-but-visible insert, the read-only success readback now runs OUTSIDE the guard on the unified readback budget, so the MCP/background path (readback override) bounds the recovery readback too — a wedged readback cannot park a thread + connection after the response deadline returns early (F1, ENG-91316/ENG-91540).")]
	public void CreateSection_Should_Pass_Bounded_Readback_Timeout_When_Insert_Times_Out_But_Section_Is_Visible() {
		// Arrange
		SetUpTimedOutInsertWithReadbackMocks();
		// Act — the readback override is what bounds the recovery readback now that it runs outside the guard.
		_ = _sut.CreateSection("sandbox", CreateReuseEntityRequest(), readbackTimeoutMsOverride: 30_000);
		// Assert
		_capturedReadbackTimeout.Should().Be(30_000,
			because: "the recovery readback must run under the finite readback override so the full response stays below the MCP client request ceiling after the insert timed out");
	}

	[Test]
	[Description("Does not treat a pre-existing section bound to the same entity as proof of success when the insert times out: verification matches strictly by the generated section id.")]
	public void CreateSection_Should_Not_Recover_When_Readback_Returns_Unrelated_Section_For_Same_Entity() {
		// Arrange
		SetUpInsertThrowingMocks(new WebException("The operation has timed out.", WebExceptionStatus.Timeout));
		SetUpSectionReadbackMock(
			"""{"success":true,"rows":[{"Id":"00000000-aaaa-bbbb-cccc-000000000001","ApplicationId":"app-id","Caption":"Pre-existing","Code":"UsrPreExisting","Description":null,"EntitySchemaName":"UsrOrders","PackageId":"pkg-uid","SectionSchemaUId":"section-schema-uid","LogoId":"icon-id","IconBackground":null,"ClientTypeId":null}]}""");
		// Act
		Action act = () => _sut.CreateSection("sandbox", CreateReuseEntityRequest());
		// Assert
		ApplicationSectionCreateException exception = act.Should().Throw<ApplicationSectionCreateException>(
				because: "a pre-existing section bound to the same entity must not be mistaken for the one this call attempted to create")
			.Which;
		exception.FailureClass.Should().Be(ApplicationSectionCreateFailureClass.CreatioTimeout,
			because: "the insert outcome is still unknown — only an Id match proves this call created the section");
		exception.SectionCreated.Should().BeFalse(
			because: "the generated section id was not found, so this call's section is not visible");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("UpdateQuery", StringComparison.Ordinal)),
			Arg.Any<int>());
	}

	[Test]
	[Description("Classifies a timed-out insert whose verification readback finds no section as creatio-timeout with section-created=false.")]
	public void CreateSection_Should_Throw_CreatioTimeout_Failure_When_Insert_Times_Out_And_Section_Is_Absent() {
		// Arrange
		SetUpInsertThrowingMocks(new WebException("The operation has timed out.", WebExceptionStatus.Timeout));
		SetUpSectionReadbackMock("""{"success":true,"rows":[]}""");
		// Act
		Action act = () => _sut.CreateSection("sandbox", CreateReuseEntityRequest());
		// Assert
		ApplicationSectionCreateException exception = act.Should().Throw<ApplicationSectionCreateException>(
				because: "a timed-out insert without a visible section is a classified creatio-timeout failure")
			.Which;
		exception.FailureClass.Should().Be(ApplicationSectionCreateFailureClass.CreatioTimeout,
			because: "the request was sent but Creatio produced no response within the budget");
		exception.SectionCreated.Should().BeFalse(
			because: "the post-timeout verification readback did not find the section");
		exception.RetryGuidance.Should().Contain("list-app-sections",
			because: "the agent must verify the section state before deciding to retry");
	}

	[Test]
	[Description("Reports section-created=unknown when the insert times out and the verification readback itself fails.")]
	public void CreateSection_Should_Report_Unknown_Section_State_When_Verification_Readback_Fails() {
		// Arrange
		SetUpInsertThrowingMocks(new WebException("The operation has timed out.", WebExceptionStatus.Timeout));
		SetUpSectionReadbackMock("""{"success":false,"errorInfo":{"message":"Verification failed"}}""");
		// Act
		Action act = () => _sut.CreateSection("sandbox", CreateReuseEntityRequest());
		// Assert
		ApplicationSectionCreateException exception = act.Should().Throw<ApplicationSectionCreateException>(
				because: "a timed-out insert is a classified creatio-timeout failure even when verification fails")
			.Which;
		exception.FailureClass.Should().Be(ApplicationSectionCreateFailureClass.CreatioTimeout,
			because: "the verification outcome refines but does not change the timeout classification");
		exception.SectionCreated.Should().BeNull(
			because: "a failed verification readback leaves the section state unknown");
		exception.RetryGuidance.Should().Contain("Do not retry blindly",
			because: "an unknown section state makes a blind retry the most dangerous option");
	}

	private static IEnumerable<TestCaseData> InsertFailureClassificationCases() {
		yield return new TestCaseData(
				new TaskCanceledException("A task was canceled."),
				ApplicationSectionCreateFailureClass.CreatioTimeout)
			.SetName("CreateSection_Should_Classify_TaskCanceledException_As_CreatioTimeout");
		yield return new TestCaseData(
				new OperationCanceledException("The operation was canceled."),
				ApplicationSectionCreateFailureClass.CreatioTimeout)
			.SetName("CreateSection_Should_Classify_OperationCanceledException_As_CreatioTimeout");
		yield return new TestCaseData(
				new TimeoutException("The request timed out."),
				ApplicationSectionCreateFailureClass.CreatioTimeout)
			.SetName("CreateSection_Should_Classify_TimeoutException_As_CreatioTimeout");
		yield return new TestCaseData(
				new HttpRequestException("The connection could not be established."),
				ApplicationSectionCreateFailureClass.Transport)
			.SetName("CreateSection_Should_Classify_Bare_HttpRequestException_As_Transport");
		yield return new TestCaseData(
				new HttpRequestException("Internal server error.", null, HttpStatusCode.InternalServerError),
				ApplicationSectionCreateFailureClass.ServerError)
			.SetName("CreateSection_Should_Classify_HttpRequestException_500_As_ServerError");
		yield return new TestCaseData(
				new HttpRequestException("Service unavailable.", null, HttpStatusCode.ServiceUnavailable),
				ApplicationSectionCreateFailureClass.CreatioTimeout)
			.SetName("CreateSection_Should_Classify_Transient_HttpRequestException_503_As_CreatioTimeout");
		yield return new TestCaseData(
				new InvalidOperationException(
					"Request execution failed.",
					new WebException("The operation has timed out.", WebExceptionStatus.Timeout)),
				ApplicationSectionCreateFailureClass.CreatioTimeout)
			.SetName("CreateSection_Should_Classify_Wrapped_WebException_Timeout_Via_Chain_Walk");
		yield return new TestCaseData(
				new AggregateException(
					new HttpRequestException(
						"Send failed.",
						new SocketException((int)SocketError.ConnectionRefused))),
				ApplicationSectionCreateFailureClass.Transport)
			.SetName("CreateSection_Should_Classify_AggregateException_With_Nested_SocketException_As_Transport");
	}

	[TestCaseSource(nameof(InsertFailureClassificationCases))]
	[Description("Classifies the HttpClient-era and nested network failure shapes (.NET-Core transport) into the documented failure classes via the exception-chain walk.")]
	public void CreateSection_Should_Classify_Insert_Failure_Shapes(
		Exception insertException,
		ApplicationSectionCreateFailureClass expectedClass) {
		// Arrange
		SetUpInsertThrowingMocks(insertException);
		SetUpSectionReadbackMock("""{"success":true,"rows":[]}""");
		// Act
		Action act = () => _sut.CreateSection("sandbox", CreateReuseEntityRequest());
		// Assert
		act.Should().Throw<ApplicationSectionCreateException>(
				because: "every network-shaped insert failure must surface as a classified failure")
			.Which.FailureClass.Should().Be(expectedClass,
				because: "the agent-facing retry decision depends on the exact failure class");
	}

	[Test]
	[Description("Clamps an env override whose millisecond equivalent exceeds int.MaxValue instead of overflowing.")]
	public void CreateSection_Should_Clamp_Insert_Timeout_When_EnvVar_Exceeds_Int_Range() {
		// Arrange
		Environment.SetEnvironmentVariable(
			ApplicationSectionCreateService.InsertTimeoutEnvironmentVariable, "3000000");
		SetUpInsertTimeoutCaptureMocks();
		// Act
		Action act = () => _sut.CreateSection("sandbox", CreateReuseEntityRequest());
		// Assert
		act.Should().Throw<ApplicationSectionCreateException>(
			because: "the rejected insert should surface as a classified failure");
		_capturedInsertTimeout.Should().Be(int.MaxValue,
			because: "3,000,000 seconds in milliseconds exceeds int.MaxValue and must clamp, not overflow");
	}

	[Test]
	[Description("Classifies a JSON-null insert response as a server-error failure with an unknown section state instead of escaping unclassified with a dangling spinner.")]
	public void CreateSection_Should_Classify_Empty_Insert_Response_As_ServerError() {
		// Arrange
		SetUpInsertFailureMocks("null");
		// Act
		Action act = () => _sut.CreateSection("sandbox", CreateNewEntityRequest());
		// Assert
		ApplicationSectionCreateException exception = act.Should().Throw<ApplicationSectionCreateException>(
				because: "an empty insert response is a classified server-error failure")
			.Which;
		exception.FailureClass.Should().Be(ApplicationSectionCreateFailureClass.ServerError,
			because: "the server replied, but not with the insert acknowledgement contract");
		exception.SectionCreated.Should().BeNull(
			because: "an empty response leaves the actual insert outcome unknown");
		exception.Message.Should().Contain("empty",
			because: "the message must explain that the server returned an empty payload");
	}

	[Test]
	[Description("Classifies an HTTP protocol error from the insert as a server-error failure.")]
	public void CreateSection_Should_Throw_ServerError_Classified_Failure_When_Protocol_Error_Occurs() {
		// Arrange
		SetUpInsertThrowingMocks(new WebException("The remote server returned an error: (500) Internal Server Error.",
			WebExceptionStatus.ProtocolError));
		// Act
		Action act = () => _sut.CreateSection("sandbox", CreateReuseEntityRequest());
		// Assert
		ApplicationSectionCreateException exception = act.Should().Throw<ApplicationSectionCreateException>(
				because: "an HTTP error response is a classified server-error failure")
			.Which;
		exception.FailureClass.Should().Be(ApplicationSectionCreateFailureClass.ServerError,
			because: "Creatio replied with an error, so retrying the same arguments will likely fail again");
		exception.SectionCreated.Should().BeFalse(
			because: "an HTTP-level rejection happens before the insert is processed");
	}

	[Test]
	[Description("Classifies a non-JSON (HTML) insert response as a server-error failure with an unknown section state.")]
	public void CreateSection_Should_Throw_ServerError_Classified_Failure_When_Response_Is_Html() {
		// Arrange
		SetUpInsertFailureMocks("<html><body>Server Error</body></html>");
		// Act
		Action act = () => _sut.CreateSection("sandbox", CreateNewEntityRequest());
		// Assert
		ApplicationSectionCreateException exception = act.Should().Throw<ApplicationSectionCreateException>(
				because: "an HTML body instead of the insert acknowledgement is a classified server-error failure")
			.Which;
		exception.FailureClass.Should().Be(ApplicationSectionCreateFailureClass.ServerError,
			because: "a non-JSON response means the server is misconfigured or in a broken state");
		exception.SectionCreated.Should().BeNull(
			because: "an unexpected response body leaves the actual insert outcome unknown");
		exception.Message.Should().Contain("non-JSON",
			because: "the message must explain that the server returned an unexpected payload");
	}

	[Test]
	[Description("Classifies a rejected insert (success=false) as a server-error failure while preserving the actionable message.")]
	public void CreateSection_Should_Classify_Rejected_Insert_As_ServerError() {
		// Arrange
		SetUpInsertFailureMocks("""{"success":false,"errorInfo":{"message":"Cannot insert duplicate key row"}}""");
		// Act
		Action act = () => _sut.CreateSection("sandbox", CreateNewEntityRequest());
		// Assert
		ApplicationSectionCreateException exception = act.Should().Throw<ApplicationSectionCreateException>(
				because: "a rejected insert is a classified server-error failure")
			.Which;
		exception.FailureClass.Should().Be(ApplicationSectionCreateFailureClass.ServerError,
			because: "Creatio explicitly rejected the insert");
		exception.SectionCreated.Should().BeFalse(
			because: "an explicit rejection guarantees the section was not created");
		exception.RetryGuidance.Should().Contain("list-app-sections",
			because: "the agent should inspect existing sections instead of blind-retrying a rejected insert");
	}

	[Test]
	[Description("Maps the Contention failure class to the kebab-case 'contention' wire value while the other classes keep their existing wire values (ENG-93089 AC-07).")]
	[TestCase(ApplicationSectionCreateFailureClass.Contention, "contention")]
	[TestCase(ApplicationSectionCreateFailureClass.Transport, "transport")]
	[TestCase(ApplicationSectionCreateFailureClass.CreatioTimeout, "creatio-timeout")]
	[TestCase(ApplicationSectionCreateFailureClass.ServerError, "server-error")]
	public void ToWireValue_ShouldReturnKebabCaseWireValue_ForEveryFailureClass(
		ApplicationSectionCreateFailureClass failureClass,
		string expectedWireValue) {
		// Act
		string wireValue = failureClass.ToWireValue();

		// Assert
		wireValue.Should().Be(expectedWireValue,
			because: "each failure class must surface a stable kebab-case error-class on the MCP envelope");
	}

	private int? _capturedInsertTimeout;

	private static ApplicationSectionCreateRequest CreateReuseEntityRequest() =>
		new(
			"UsrOrdersApp",
			"Orders",
			"Order workspace",
			"UsrOrders",
			WithMobilePages: false);

	private static ApplicationSectionCreateRequest CreateNewEntityRequest() =>
		new(
			"UsrOrdersApp",
			"Orders",
			"Order workspace",
			EntitySchemaName: null,
			WithMobilePages: false);

	private static ApplicationInfoResult CreateBeforeInfo() =>
		new(
			"pkg-uid",
			"UsrOrdersApp",
			[],
			[],
			"app-id",
			"Orders App",
			"UsrOrdersApp",
			"8.3.0");

	private void SetUpCommonReadMocks() {
		_applicationInfoService.GetApplicationInfo("sandbox", null, "UsrOrdersApp")
			.Returns(CreateBeforeInfo());
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysAppIcons\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[{"Id":"11111111-1111-1111-1111-111111111111"}]}""");
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysSchema\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[]}""");
		StubExistingEntitySchema("UsrOrders");
	}

	private void SetUpInsertTimeoutCaptureMocks() {
		SetUpCommonReadMocks();
		_capturedInsertTimeout = null;
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
					body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
					!body.Contains("\"filters\"", StringComparison.Ordinal)),
				Arg.Any<int>())
			.Returns(callInfo => {
				_capturedInsertTimeout = callInfo.ArgAt<int>(2);
				return """{"success":false,"errorInfo":{"message":"Rejected"}}""";
			});
	}

	private string? _capturedInsertBody;

	private int? _capturedReadbackTimeout;

	private void SetUpInsertThrowingMocks(Exception insertException) {
		SetUpCommonReadMocks();
		_capturedInsertBody = null;
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
					body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
					!body.Contains("\"filters\"", StringComparison.Ordinal)),
				Arg.Any<int>())
			.Returns(callInfo => {
				_capturedInsertBody = callInfo.ArgAt<string>(1);
				throw insertException;
			});
	}

	private string ExtractGeneratedSectionId() {
		_capturedInsertBody.Should().NotBeNull(
			because: "the insert stub must have captured the insert body before the readback is built");
		using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(_capturedInsertBody!);
		return document.RootElement
			.GetProperty("columnValues")
			.GetProperty("items")
			.GetProperty("Id")
			.GetProperty("parameter")
			.GetProperty("value")
			.GetString()!;
	}

	private void SetUpSectionReadbackMock(string responseJson) {
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
					body.Contains("\"SectionSchemaUId\"", StringComparison.Ordinal)),
				Arg.Any<int>())
			.Returns(responseJson);
	}

	private void SetUpTimedOutInsertWithReadbackMocks() {
		SetUpInsertThrowingMocks(new WebException("The operation has timed out.", WebExceptionStatus.Timeout));
		// The readback row carries the section id generated inside the service for this call,
		// captured from the insert body, so the Id-based verification can recognize it.
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
					body.Contains("\"SectionSchemaUId\"", StringComparison.Ordinal)),
				Arg.Any<int>())
			.Returns(callInfo => {
				_capturedReadbackTimeout = callInfo.ArgAt<int>(2);
				return $$"""{"success":true,"rows":[{"Id":"{{ExtractGeneratedSectionId()}}","ApplicationId":"app-id","Caption":"Orders","Code":"UsrOrders","Description":"Order workspace","EntitySchemaName":"UsrOrders","PackageId":"pkg-uid","SectionSchemaUId":"section-schema-uid","LogoId":"icon-id","IconBackground":null,"ClientTypeId":null}]}""";
			});
		// LoadCreatedSection re-reads the app info and persists the icon background after recovery;
		// the recovery readback runs bounded, so the update stub must accept the explicit timeout.
		_applicationInfoService.GetApplicationInfo("sandbox", null, "UsrOrdersApp")
			.Returns(CreateBeforeInfo(), CreateBeforeInfo());
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
					body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
					body.Contains("\"IconBackground\"", StringComparison.Ordinal) &&
					body.Contains("\"filters\"", StringComparison.Ordinal)),
				Arg.Any<int>())
			.Returns("""{"success":true}""");
	}

	private void SetUpSuccessfulCreateWithReadbackCapture() {
		_capturedReadbackTimeout = null;
		ApplicationEntityInfoResult entity = new("entity-uid", "UsrOrders", "Orders", []);
		ApplicationInfoResult beforeInfo = new(
			"pkg-uid", "UsrOrdersApp", [entity], [], "app-id", "Orders App", "UsrOrdersApp", "8.3.0");
		ApplicationInfoResult afterInfo = new(
			"pkg-uid", "UsrOrdersApp", [entity],
			[new PageListItem {
				SchemaName = "UsrOrders_FormPage", UId = "page-new", PackageName = "UsrOrdersApp", ParentSchemaName = "BasePageV2"
			}],
			"app-id", "Orders App", "UsrOrdersApp", "8.3.0");
		_applicationInfoService.GetApplicationInfo("sandbox", null, "UsrOrdersApp")
			.Returns(beforeInfo, afterInfo);
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysAppIcons\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[{"Id":"11111111-1111-1111-1111-111111111111"}]}""");
		StubExistingEntitySchema("UsrOrders");
		// Insert succeeds (reuse-entity payload carries EntitySchemaName, no filters).
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
				body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
				body.Contains("\"EntitySchemaName\"", StringComparison.Ordinal) &&
				!body.Contains("\"filters\"", StringComparison.Ordinal)),
			Arg.Any<int>())
			.Returns("""{"success":true}""");
		// Success-path readback (SectionSchemaUId select) — capture the timeout that reaches it.
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
				body.Contains("\"SectionSchemaUId\"", StringComparison.Ordinal)),
			Arg.Any<int>())
			.Returns(callInfo => {
				_capturedReadbackTimeout = callInfo.ArgAt<int>(2);
				return """{"success":true,"rows":[{"Id":"section-id","ApplicationId":"app-id","Caption":"Orders","Code":"UsrOrders","Description":"Order workspace","EntitySchemaName":"UsrOrders","PackageId":"pkg-uid","SectionSchemaUId":"section-schema-uid","LogoId":"icon-id","IconBackground":null,"ClientTypeId":null}]}""";
			});
		// Icon-background UpdateQuery runs on the same readback budget, so it must accept any timeout.
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
				body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
				body.Contains("\"IconBackground\"", StringComparison.Ordinal) &&
				body.Contains("\"filters\"", StringComparison.Ordinal)),
			Arg.Any<int>())
			.Returns("""{"success":true}""");
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
				!body.Contains("\"filters\"", StringComparison.Ordinal)),
			Arg.Any<int>())
			.Returns(insertResponseJson);
	}

	// Insert always returns the detail-less contention rejection; the section readback either shows the
	// section (verified visible → recovered via readback, no retry) or is empty (verified absent → one retry,
	// which also stays detail-less → classified contention). Used by the ENG-93089 AC-03/AC-06 tests.
	private void SetUpDetailLessInsertWithReadback(bool sectionVisibleOnVerify) {
		SetUpCommonReadMocks();
		_capturedInsertBody = null;
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
					body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
					!body.Contains("\"filters\"", StringComparison.Ordinal)),
				Arg.Any<int>())
			.Returns(callInfo => {
				_capturedInsertBody = callInfo.ArgAt<string>(1);
				return """{"success":false}""";
			});
		_applicationInfoService.GetApplicationInfo("sandbox", null, "UsrOrdersApp")
			.Returns(CreateBeforeInfo(), CreateBeforeInfo());
		if (sectionVisibleOnVerify) {
			_applicationClient.ExecutePostRequest(
					Arg.Any<string>(),
					Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
						body.Contains("\"SectionSchemaUId\"", StringComparison.Ordinal)),
					Arg.Any<int>())
				.Returns(_ => $$"""{"success":true,"rows":[{"Id":"{{ExtractGeneratedSectionId()}}","ApplicationId":"app-id","Caption":"Orders","Code":"UsrOrders","Description":"Order workspace","EntitySchemaName":"UsrOrders","PackageId":"pkg-uid","SectionSchemaUId":"section-schema-uid","LogoId":"icon-id","IconBackground":null,"ClientTypeId":null}]}""");
			_applicationClient.ExecutePostRequest(
					Arg.Any<string>(),
					Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
						body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
						body.Contains("\"IconBackground\"", StringComparison.Ordinal) &&
						body.Contains("\"filters\"", StringComparison.Ordinal)),
					Arg.Any<int>())
				.Returns("""{"success":true}""");
		} else {
			_applicationClient.ExecutePostRequest(
					Arg.Any<string>(),
					Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
						body.Contains("\"SectionSchemaUId\"", StringComparison.Ordinal)),
					Arg.Any<int>())
				.Returns("""{"success":true,"rows":[]}""");
		}
	}

	// First insert returns the detail-less contention rejection; the bounded settle+poll verify (F4, up to
	// 3 attempts) finds the section absent throughout the pre-retry window, so clio retries once, the second
	// insert succeeds, and the outside readback then returns the created section.
	// Used by the ENG-93089 AC-04 recovery test.
	private void SetUpDetailLessThenSuccessfulInsertWithReadback() {
		SetUpCommonReadMocks();
		_capturedInsertBody = null;
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
					body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
					!body.Contains("\"filters\"", StringComparison.Ordinal)),
				Arg.Any<int>())
			.Returns(
				callInfo => {
					_capturedInsertBody = callInfo.ArgAt<string>(1);
					return """{"success":false}""";
				},
				callInfo => {
					_capturedInsertBody = callInfo.ArgAt<string>(1);
					return """{"success":true}""";
				});
		_applicationInfoService.GetApplicationInfo("sandbox", null, "UsrOrdersApp")
			.Returns(CreateBeforeInfo(), CreateBeforeInfo());
		// The settle+poll verify makes up to ContentionVerifyAttempts (3) calls before the retry; all must
		// report the section absent. Only the readback that runs AFTER the successful retry (the 4th call
		// onwards, which NSubstitute repeats) returns the created section by its generated id.
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
					body.Contains("\"SectionSchemaUId\"", StringComparison.Ordinal)),
				Arg.Any<int>())
			.Returns(
				_ => """{"success":true,"rows":[]}""",
				_ => """{"success":true,"rows":[]}""",
				_ => """{"success":true,"rows":[]}""",
				_ => $$"""{"success":true,"rows":[{"Id":"{{ExtractGeneratedSectionId()}}","ApplicationId":"app-id","Caption":"Orders","Code":"UsrOrders","Description":"Order workspace","EntitySchemaName":"UsrOrders","PackageId":"pkg-uid","SectionSchemaUId":"section-schema-uid","LogoId":"icon-id","IconBackground":null,"ClientTypeId":null}]}""");
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
					body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
					body.Contains("\"IconBackground\"", StringComparison.Ordinal) &&
					body.Contains("\"filters\"", StringComparison.Ordinal)),
				Arg.Any<int>())
			.Returns("""{"success":true}""");
	}

	// First insert returns the detail-less contention rejection; the bounded settle+poll verify (3 attempts)
	// finds the section absent, so clio retries once — but the RETRY insert returns a DETAILED server
	// rejection (a duplicate/constraint error) because insert #1 actually committed and answered detail-less.
	// The section becomes visible on the FIX B re-verify (4th readback onward), so the terminal ServerError is
	// swallowed and the outside readback returns the created section. Used by the ENG-93089 FIX B test.
	private void SetUpDetailLessThenServerErrorInsertWithVisibleSection() {
		SetUpCommonReadMocks();
		_capturedInsertBody = null;
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
					body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
					!body.Contains("\"filters\"", StringComparison.Ordinal)),
				Arg.Any<int>())
			.Returns(
				callInfo => {
					_capturedInsertBody = callInfo.ArgAt<string>(1);
					return """{"success":false}""";
				},
				callInfo => {
					_capturedInsertBody = callInfo.ArgAt<string>(1);
					return """{"success":false,"errorInfo":{"message":"Section with this code already exists"}}""";
				});
		_applicationInfoService.GetApplicationInfo("sandbox", null, "UsrOrdersApp")
			.Returns(CreateBeforeInfo(), CreateBeforeInfo());
		// The pre-retry settle+poll verify makes up to 3 calls (all absent); the FIX B re-verify after the
		// retry's terminal ServerError (the 4th call onward, which NSubstitute repeats) finds the section by
		// its generated id, and the outside readback then returns it.
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
					body.Contains("\"SectionSchemaUId\"", StringComparison.Ordinal)),
				Arg.Any<int>())
			.Returns(
				_ => """{"success":true,"rows":[]}""",
				_ => """{"success":true,"rows":[]}""",
				_ => """{"success":true,"rows":[]}""",
				_ => $$"""{"success":true,"rows":[{"Id":"{{ExtractGeneratedSectionId()}}","ApplicationId":"app-id","Caption":"Orders","Code":"UsrOrders","Description":"Order workspace","EntitySchemaName":"UsrOrders","PackageId":"pkg-uid","SectionSchemaUId":"section-schema-uid","LogoId":"icon-id","IconBackground":null,"ClientTypeId":null}]}""");
		_applicationClient.ExecutePostRequest(
				Arg.Any<string>(),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
					body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
					body.Contains("\"IconBackground\"", StringComparison.Ordinal) &&
					body.Contains("\"filters\"", StringComparison.Ordinal)),
				Arg.Any<int>())
			.Returns("""{"success":true}""");
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
				!body.Contains("\"filters\"", StringComparison.Ordinal)),
			Arg.Any<int>())
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

	[Test]
	[Description("Fails fast with an actionable error pointing at --code when the caption has no Latin characters and no explicit code is supplied, instead of sending an invalid non-ASCII section code that Creatio silently rejects.")]
	public void CreateSection_Should_Throw_Actionable_Error_When_Caption_Has_No_Latin_Characters_And_No_Code() {
		// Arrange
		// A uk-UA profile makes the Cyrillic caption valid, so the code-generation guidance (not the
		// caption-script guard) is what fails when no explicit Latin code can be derived.
		ICaptionCultureResolver ukResolver = Substitute.For<ICaptionCultureResolver>();
		ukResolver.Resolve(Arg.Any<EnvironmentOptions>(), Arg.Any<string?>()).Returns("uk-UA");
		ApplicationSectionCreateService sut = CreateSutWithResolver(ukResolver);
		ApplicationInfoResult beforeInfo = new(
			"pkg-uid", "UsrOrdersApp", [], [], "app-id", "Orders App", "UsrOrdersApp", "8.3.0");
		_applicationInfoService.GetApplicationInfo("sandbox", null, "UsrOrdersApp")
			.Returns(beforeInfo);

		// Act
		Action action = () => sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(
				ApplicationCode: "UsrOrdersApp",
				Caption: "Контакти"));

		// Assert
		ArgumentException exception = action.Should().Throw<ArgumentException>(
			because: "a non-Latin caption cannot produce a valid section code and must fail with guidance instead of an opaque server rejection").Which;
		exception.Message.Should().Contain("--code",
			because: "the message must tell the caller to provide an explicit code");
		exception.Message.Should().Contain("Контакти",
			because: "the message should name the caption that could not be converted to a code");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
	}

	[Test]
	[Description("Uses the explicit code (with the environment prefix ensured) for the section code when the caption is non-Latin, so the section is created with a valid Latin code while the caption stays localized.")]
	public void CreateSection_Should_Use_Explicit_Code_When_Caption_Is_Non_Latin() {
		// Arrange
		// A uk-UA profile makes the Cyrillic caption valid; this test covers the explicit-code path,
		// not the caption-script guard.
		ICaptionCultureResolver ukResolver = Substitute.For<ICaptionCultureResolver>();
		ukResolver.Resolve(Arg.Any<EnvironmentOptions>(), Arg.Any<string?>()).Returns("uk-UA");
		ApplicationSectionCreateService sut = CreateSutWithResolver(ukResolver);
		SetUpPrefixTestMocks("UsrContacts");

		// Act
		ApplicationSectionCreateResult result = sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(
				ApplicationCode: "UsrOrdersApp",
				Caption: "Контакти",
				Code: "Contacts"));

		// Assert
		result.Section.Code.Should().Be("UsrContacts",
			because: "the explicit code must be prefixed with the environment schema-name prefix and used verbatim for the section code");
	}

	[Test]
	[Description("Rejects an explicit code that is not a valid Latin identifier before any remote call, so an invalid override fails clearly instead of being silently rejected by the server.")]
	public void CreateSection_Should_Reject_Invalid_Explicit_Code() {
		// Arrange
		ApplicationInfoResult beforeInfo = new(
			"pkg-uid", "UsrOrdersApp", [], [], "app-id", "Orders App", "UsrOrdersApp", "8.3.0");
		_applicationInfoService.GetApplicationInfo("sandbox", null, "UsrOrdersApp")
			.Returns(beforeInfo);

		// Act
		Action action = () => _sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(
				ApplicationCode: "UsrOrdersApp",
				Caption: "Contacts",
				Code: "Контакти"));

		// Assert
		ArgumentException exception = action.Should().Throw<ArgumentException>(
			because: "an explicit code that is not a Latin identifier must be rejected with a clear validation error").Which;
		exception.Message.Should().Contain("invalid",
			because: "the message must state that the supplied code is invalid");
		exception.Message.Should().Contain("Latin",
			because: "the message must explain that section codes must be Latin identifiers");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
	}

	[Test]
	[Description("Throws a clear, non-silent error before any insert when --entity-schema-name targets an object that does not exist, instead of letting the section insert fail opaquely.")]
	public void CreateSection_Should_Throw_When_Existing_Entity_Does_Not_Exist() {
		// Arrange
		ApplicationInfoResult beforeInfo = new(
			"pkg-uid", "UsrOrdersApp", [], [], "app-id", "Orders App", "UsrOrdersApp", "8.3.0");
		_applicationInfoService.GetApplicationInfo("sandbox", null, "UsrOrdersApp")
			.Returns(beforeInfo);
		StubRandomIcon();
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysSchema\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[]}""");

		// Act
		Action action = () => _sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(
				ApplicationCode: "UsrOrdersApp",
				Caption: "Ghosts",
				EntitySchemaName: "UsrDoesNotExist"));

		// Assert
		InvalidOperationException exception = action.Should().Throw<InvalidOperationException>(
			because: "targeting a non-existent existing object must fail with a clear error instead of an opaque insert rejection").Which;
		exception.Message.Should().Contain("does not exist",
			because: "the message must state that the requested object was not found");
		exception.Message.Should().Contain("UsrDoesNotExist",
			because: "the message must name the missing object so the caller can correct the request");
		_applicationClient.DidNotReceive().ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
				body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
				!body.Contains("\"filters\"", StringComparison.Ordinal)));
	}

	[Test]
	[Description("Does not double-prefix an explicit code that already starts with the environment prefix in canonical casing.")]
	public void CreateSection_Should_Not_Double_Prefix_When_Code_Already_Starts_With_Prefix() {
		// Arrange
		SetUpPrefixTestMocks("UsrContacts");

		// Act
		ApplicationSectionCreateResult result = _sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(
				ApplicationCode: "UsrOrdersApp",
				Caption: "Contacts",
				Code: "UsrContacts"));

		// Assert
		result.Section.Code.Should().Be("UsrContacts",
			because: "a code that already begins with the canonical prefix must not get the prefix prepended again");
	}

	[Test]
	[Description("Re-canonicalizes prefix casing when the explicit code starts with the prefix in a different case, so --code usrContacts with prefix Usr yields UsrContacts.")]
	public void CreateSection_Should_Canonicalize_Prefix_Casing_When_Code_Has_Lowercase_Prefix() {
		// Arrange
		SetUpPrefixTestMocks("UsrContacts");

		// Act
		ApplicationSectionCreateResult result = _sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(
				ApplicationCode: "UsrOrdersApp",
				Caption: "Contacts",
				Code: "usrContacts"));

		// Assert
		result.Section.Code.Should().Be("UsrContacts",
			because: "when the code starts with the prefix in the wrong case, the canonical casing from SchemaNamePrefix must be applied");
	}

	[Test]
	[Description("Salvages the ASCII digit fragment from a mixed caption (e.g. 'Контакти 2024') to produce a valid code, inserting an underscore after the prefix so the identifier does not start with a digit.")]
	public void CreateSection_Should_Salvage_ASCII_Digit_Fragment_From_Mixed_Caption() {
		// Arrange
		// A uk-UA profile makes the Cyrillic part of the caption valid; this test covers the
		// digit-salvage code-generation path, not the caption-script guard.
		ICaptionCultureResolver ukResolver = Substitute.For<ICaptionCultureResolver>();
		ukResolver.Resolve(Arg.Any<EnvironmentOptions>(), Arg.Any<string?>()).Returns("uk-UA");
		ApplicationSectionCreateService sut = CreateSutWithResolver(ukResolver);
		SetUpPrefixTestMocks("Usr_2024");

		// Act
		ApplicationSectionCreateResult result = sut.CreateSection(
			"sandbox",
			new ApplicationSectionCreateRequest(
				ApplicationCode: "UsrOrdersApp",
				Caption: "Контакти 2024"));

		// Assert
		result.Section.Code.Should().Be("Usr_2024",
			because: "the non-Latin word is dropped and the digit fragment '2024' is salvaged; an underscore is inserted after the prefix because the generated code would otherwise start with a digit");
	}

	[Test]
	[Description("Proceeds with the section insert when the entity existence probe throws unexpectedly, so a permissions or transport failure on the probe does not block creation.")]
	public void CreateSection_Should_Proceed_With_Insert_When_EntityExistenceProbe_Throws() {
		// Arrange
		ApplicationEntityInfoResult existingEntity = new("entity-uid", "UsrTaskStatus", "Task statuses", []);
		ApplicationInfoResult beforeInfo = new(
			"pkg-uid", "UsrOrdersApp", [existingEntity], [], "app-id", "Orders App", "UsrOrdersApp", "8.3.0");
		ApplicationInfoResult afterInfo = new(
			"pkg-uid", "UsrOrdersApp", [existingEntity], [], "app-id", "Orders App", "UsrOrdersApp", "8.3.0");
		_applicationInfoService.GetApplicationInfo("sandbox", null, "UsrOrdersApp").Returns(beforeInfo, afterInfo);
		StubRandomIcon();
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysSchema\"", StringComparison.Ordinal)
				&& body.Contains("\"value\":\"UsrTaskStatus\"", StringComparison.Ordinal)))
			.Returns(_ => throw new InvalidOperationException("Simulated transport failure"));
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
				body.Contains("\"columnValues\"", StringComparison.Ordinal) &&
				!body.Contains("\"filters\"", StringComparison.Ordinal)),
			Arg.Any<int>())
			.Returns("""{"success":true}""");
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", StringComparison.Ordinal) &&
				body.Contains("\"SectionSchemaUId\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[{"Id":"section-id","ApplicationId":"app-id","Caption":"Task statuses","Code":"UsrTaskStatus","Description":null,"EntitySchemaName":"UsrTaskStatus","PackageId":"pkg-uid","SectionSchemaUId":"section-schema-uid","LogoId":"icon-id","IconBackground":null,"ClientTypeId":null}]}""");
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
				EntitySchemaName: "UsrTaskStatus"));

		// Assert
		result.Section.Code.Should().Be("UsrTaskStatus",
			because: "a transport failure in the existence probe must not block creation; the probe is best-effort and the insert must proceed");
	}

	private void StubRandomIcon() {
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysAppIcons\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[{"Id":"11111111-1111-1111-1111-111111111111"}]}""");
	}

	private void StubExistingEntitySchema(string entityName) {
		_applicationClient.ExecutePostRequest(
			Arg.Any<string>(),
			Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysSchema\"", StringComparison.Ordinal)
				&& body.Contains($"\"value\":\"{entityName}\"", StringComparison.Ordinal)))
			.Returns($$"""{"success":true,"rows":[{"Name":"{{entityName}}"}]}""");
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
