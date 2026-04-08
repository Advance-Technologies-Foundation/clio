using System;
using System.Linq;
using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public sealed class ApplicationInfoServiceTests {
	private ISettingsRepository _settingsRepository = null!;
	private IApplicationClientFactory _applicationClientFactory = null!;
	private IApplicationClient _applicationClient = null!;
	private ApplicationInfoService _sut = null!;
	private EnvironmentSettings _environment = null!;

	[SetUp]
	public void SetUp() {
		// Arrange
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		_applicationClient = Substitute.For<IApplicationClient>();
		_environment = new EnvironmentSettings {
			Uri = "https://example.invalid",
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = true
		};
		_settingsRepository.FindEnvironment("sandbox").Returns(_environment);
		_applicationClientFactory.CreateEnvironmentClient(_environment).Returns(_applicationClient);
		_sut = new ApplicationInfoService(_settingsRepository, _applicationClientFactory);
	}

	[Test]
	[Description("Loads application info by code, uses package and entity endpoints, removes inherited columns, and sorts the final payload deterministically.")]
	public void GetApplicationInfo_Should_Load_Aggregate_By_AppCode() {
		// Arrange
		ConfigureHappyPathResponses();

		// Act
		ApplicationInfoResult result = _sut.GetApplicationInfo("sandbox", null, "APP");

		// Assert
		result.PackageUId.Should().Be("pkg-uid",
			because: "the primary package identifier should come from the package service response");
		result.PackageName.Should().Be("PrimaryPkg",
			because: "the primary package name should come from the package service response");
		result.Entities.Select(entity => entity.Caption).Should().Equal(new[] { "Alpha caption", "Beta caption" },
			because: "application entities should be sorted by caption");
		result.Entities[0].Columns.Should().ContainSingle(
			because: "inherited runtime columns should be excluded from the final payload");
		result.Entities[0].Columns[0].DataValueType.Should().Be("Text",
			because: "runtime schema data-value types should be mapped to readable names");
		result.Entities[1].Columns[0].DefaultValueSource.Should().Be("Const",
			because: "default-value source metadata should be preserved for constant defaults");
		result.Entities[1].Columns[0].DefaultValue.Should().Be("Default text",
			because: "constant default values should be preserved in the final payload");
		result.Pages.Should().ContainSingle(
			because: "application info should now surface primary-package page metadata");
		result.Pages[0].SchemaName.Should().Be("UsrAlpha_FormPage",
			because: "page metadata should use schema-name semantics consistently");
	}

	[Test]
	[Description("Loads application info by id when the installed-application query is filtered by identifier.")]
	public void GetApplicationInfo_Should_Load_Aggregate_By_AppId() {
		// Arrange
		ConfigureHappyPathResponses();

		// Act
		ApplicationInfoResult result = _sut.GetApplicationInfo("sandbox", "app-uid", null);

		// Assert
		result.PackageName.Should().Be("PrimaryPkg",
			because: "id lookups should follow the same package resolution flow as code lookups");
		_applicationClient.Received().ExecutePostRequest(
			Arg.Is<string>(url => url.EndsWith("SelectQuery", StringComparison.Ordinal)),
			Arg.Is<string>(body => body.Contains("\"columnPath\":\"Id\"", StringComparison.Ordinal) &&
				body.Contains("\"value\":\"app-uid\"", StringComparison.Ordinal)));
		result.Pages.Should().ContainSingle(
			because: "id lookups should still include primary-package page metadata");
	}

	[Test]
	[Description("Falls back to design-time entity and column captions when the runtime schema omits localized captions.")]
	public void GetApplicationInfo_Should_Fall_Back_To_Design_Time_Captions_When_Runtime_Captions_Are_Empty() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("SelectQuery", StringComparison.Ordinal)),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysInstalledApp\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[{"Id":"app-uid","Code":"APP","Name":"App","Version":"1.0"}]}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("GetApplicationPackages", StringComparison.Ordinal)),
				Arg.Any<string>())
			.Returns("""{"success":true,"packages":[{"uId":"11111111-1111-1111-1111-111111111111","name":"PrimaryPkg","isApplicationPrimaryPackage":true}]}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("SelectQuery", StringComparison.Ordinal)),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationEntity\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[{"UId":"entity-a","Name":"UsrAlpha","Caption":"Base object"}]}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("RuntimeEntitySchemaRequest", StringComparison.Ordinal)),
				Arg.Is<string>(body => body.Contains("\"uId\":\"entity-a\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"schema":{"uId":"entity-a","name":"UsrAlpha","caption":{},"columns":{"Items":{"visible":{"name":"UsrEmail","caption":{},"dataValueType":45,"isInherited":false}}}}}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("GetSchemaDesignItem", StringComparison.Ordinal)),
				Arg.Is<string>(body => body.Contains("\"name\":\"UsrAlpha\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"schema":{"caption":[{"cultureName":"en-US","value":"Alpha from designer"}],"columns":[{"name":"UsrEmail","caption":[{"cultureName":"en-US","value":"Email from designer"}]}]}}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("SelectQuery", StringComparison.Ordinal)),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysSchema\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[]}""");

		// Act
		ApplicationInfoResult result = _sut.GetApplicationInfo("sandbox", null, "APP");

		// Assert
		result.Entities.Should().ContainSingle(
			because: "the fallback scenario still represents a single resolved application entity");
		result.Entities[0].Caption.Should().Be("Alpha from designer",
			because: "design-time entity captions should win when runtime localized captions are empty");
		result.Entities[0].Columns.Should().ContainSingle(
			because: "the runtime entity still exposes the non-inherited column");
		result.Entities[0].Columns[0].Caption.Should().Be("Email from designer",
			because: "design-time column captions should be used before falling back to the technical column name");
	}

	[Test]
	[Description("Prefers the design-time title for the canonical main entity when the runtime metadata falls back to the generic Base object caption.")]
	public void GetApplicationInfo_Should_Prefer_Design_Time_Caption_For_Canonical_Main_Entity_When_Runtime_Caption_Is_BaseObject() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("SelectQuery", StringComparison.Ordinal)),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysInstalledApp\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[{"Id":"app-uid","Code":"APP","Name":"App","Version":"1.0"}]}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("GetApplicationPackages", StringComparison.Ordinal)),
				Arg.Any<string>())
			.Returns("""{"success":true,"packages":[{"uId":"11111111-1111-1111-1111-111111111111","name":"UsrTodo","isApplicationPrimaryPackage":true}]}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("SelectQuery", StringComparison.Ordinal)),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationEntity\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[{"UId":"entity-a","Name":"UsrTodo","Caption":"Base object"}]}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("RuntimeEntitySchemaRequest", StringComparison.Ordinal)),
				Arg.Is<string>(body => body.Contains("\"uId\":\"entity-a\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"schema":{"uId":"entity-a","name":"UsrTodo","caption":{"en-US":"Base object"},"columns":{"Items":{}}}}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("GetSchemaDesignItem", StringComparison.Ordinal)),
				Arg.Is<string>(body => body.Contains("\"name\":\"UsrTodo\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"schema":{"caption":[{"cultureName":"en-US","value":"Todo"}],"columns":[]}}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("SelectQuery", StringComparison.Ordinal)),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysSchema\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[]}""");

		// Act
		ApplicationInfoResult result = _sut.GetApplicationInfo("sandbox", null, "APP");

		// Assert
		result.Entities.Should().ContainSingle(
			because: "the canonical-main-entity regression scenario still resolves a single application entity");
		result.Entities[0].Caption.Should().Be("Todo",
			because: "the canonical main entity should prefer the design-time title over the generic Base object runtime fallback");
	}

	[Test]
	[Description("Falls back to the installed application name for the canonical main entity when runtime metadata reports Base object and the design caption cannot be read.")]
	public void GetApplicationInfo_Should_Fall_Back_To_Application_Name_For_Canonical_Main_Entity_When_Design_Caption_Is_Unavailable() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("SelectQuery", StringComparison.Ordinal)),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysInstalledApp\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[{"Id":"app-uid","Code":"APP","Name":"Todo List","Version":"1.0"}]}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("GetApplicationPackages", StringComparison.Ordinal)),
				Arg.Any<string>())
			.Returns("""{"success":true,"packages":[{"uId":"11111111-1111-1111-1111-111111111111","name":"UsrTodo","isApplicationPrimaryPackage":true}]}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("SelectQuery", StringComparison.Ordinal)),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationEntity\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[{"UId":"entity-a","Name":"UsrTodo","Caption":"Base object"}]}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("RuntimeEntitySchemaRequest", StringComparison.Ordinal)),
				Arg.Is<string>(body => body.Contains("\"uId\":\"entity-a\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"schema":{"uId":"entity-a","name":"UsrTodo","caption":{"en-US":"Base object"},"columns":{"Items":{}}}}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("GetSchemaDesignItem", StringComparison.Ordinal)),
				Arg.Is<string>(body => body.Contains("\"name\":\"UsrTodo\"", StringComparison.Ordinal)))
			.Returns("""{"success":false,"schema":null}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("SelectQuery", StringComparison.Ordinal)),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysSchema\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[]}""");

		// Act
		ApplicationInfoResult result = _sut.GetApplicationInfo("sandbox", null, "APP");

		// Assert
		result.Entities.Should().ContainSingle(
			because: "the regression scenario still resolves the canonical main entity");
		result.Entities[0].Caption.Should().Be("Todo List",
			because: "the canonical main entity should fall back to the installed application display name instead of leaking the generic Base object caption");
	}

	[Test]
	[Description("Keeps the generic Base object caption for non-canonical entities so the canonical fallback does not bleed into unrelated entity reads.")]
	public void GetApplicationInfo_Should_Not_Use_Application_Name_Fallback_For_NonCanonical_Entities() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("SelectQuery", StringComparison.Ordinal)),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysInstalledApp\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[{"Id":"app-uid","Code":"APP","Name":"Todo List","Version":"1.0"}]}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("GetApplicationPackages", StringComparison.Ordinal)),
				Arg.Any<string>())
			.Returns("""{"success":true,"packages":[{"uId":"11111111-1111-1111-1111-111111111111","name":"UsrTodo","isApplicationPrimaryPackage":true}]}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("SelectQuery", StringComparison.Ordinal)),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationEntity\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[{"UId":"entity-b","Name":"UsrTodoDetail","Caption":"Base object"}]}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("RuntimeEntitySchemaRequest", StringComparison.Ordinal)),
				Arg.Is<string>(body => body.Contains("\"uId\":\"entity-b\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"schema":{"uId":"entity-b","name":"UsrTodoDetail","caption":{"en-US":"Base object"},"columns":{"Items":{}}}}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("GetSchemaDesignItem", StringComparison.Ordinal)),
				Arg.Is<string>(body => body.Contains("\"name\":\"UsrTodoDetail\"", StringComparison.Ordinal)))
			.Returns("""{"success":false,"schema":null}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("SelectQuery", StringComparison.Ordinal)),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysSchema\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[]}""");

		// Act
		ApplicationInfoResult result = _sut.GetApplicationInfo("sandbox", null, "APP");

		// Assert
		result.Entities.Should().ContainSingle(
			because: "the guard scenario only resolves the related non-canonical entity");
		result.Entities[0].Caption.Should().Be("Base object",
			because: "the application-name fallback must stay limited to the canonical main entity");
	}

	[Test]
	[Description("Returns a readable not-found failure when the installed-application lookup matches no rows.")]
	public void GetApplicationInfo_Should_Throw_When_Application_Is_Not_Found() {
		// Arrange
		_applicationClient.ExecutePostRequest(
			Arg.Is<string>(url => url.EndsWith("SelectQuery", StringComparison.Ordinal)),
			Arg.Any<string>())
			.Returns("""{"success":true,"rows":[]}""");

		// Act
		Action action = () => _sut.GetApplicationInfo("sandbox", null, "missing");

		// Assert
		InvalidOperationException exception = action.Should().Throw<InvalidOperationException>()
			.Which;
		exception.Message.Should().Match("*missing*",
			"callers should get a clear not-found error when the installed application does not exist");
		exception.Message.Should().Match("*not found*",
			"callers should get a clear not-found error when the installed application does not exist");
	}

	[Test]
	[Description("Fails when the application packages response does not contain a primary package.")]
	public void GetApplicationInfo_Should_Throw_When_Primary_Package_Is_Missing() {
		// Arrange
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("SelectQuery", StringComparison.Ordinal)),
				Arg.Any<string>())
			.Returns("""{"success":true,"rows":[{"Id":"app-uid","Code":"APP","Name":"App","Version":"1.0"}]}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("GetApplicationPackages", StringComparison.Ordinal)),
				Arg.Any<string>())
			.Returns("""{"success":true,"packages":[{"uId":"pkg-uid","name":"PrimaryPkg","isApplicationPrimaryPackage":false}]}""");

		// Act
		Action action = () => _sut.GetApplicationInfo("sandbox", null, "APP");

		// Assert
		action.Should().Throw<InvalidOperationException>()
			.WithMessage("*Primary package not found*",
				because: "the tool relies on the package service to identify the application primary package");
	}

	[Test]
	[Description("Rejects requests that omit both installed-application identifiers before any remote calls are made.")]
	public void GetApplicationInfo_Should_Throw_When_Identifiers_Are_Missing() {
		// Arrange

		// Act
		Action action = () => _sut.GetApplicationInfo("sandbox", null, null);

		// Assert
		ArgumentException exception = action.Should().Throw<ArgumentException>()
			.Which;
		exception.Message.Should().Match("*id*",
			"the service requires at least one installed-application identifier");
		exception.Message.Should().Match("*code*",
			"the service requires at least one installed-application identifier");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default!, default!);
	}

	[Test]
	[Description("Rejects unknown environments with the same readable diagnostics used by other environment-sensitive services.")]
	public void GetApplicationInfo_Should_Throw_When_Environment_Is_Not_Found() {
		// Arrange
		_settingsRepository.FindEnvironment("missing").Returns((EnvironmentSettings?)null);

		// Act
		Action action = () => _sut.GetApplicationInfo("missing", null, "APP");

		// Assert
		InvalidOperationException exception = action.Should().Throw<InvalidOperationException>()
			.Which;
		exception.Message.Should().Match("*missing*",
			"environment-sensitive reads should fail clearly when the environment key is unknown");
		exception.Message.Should().Match("*not found*",
			"environment-sensitive reads should fail clearly when the environment key is unknown");
	}

	private void ConfigureHappyPathResponses() {
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("SelectQuery", StringComparison.Ordinal)),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysInstalledApp\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[{"Id":"app-uid","Code":"APP","Name":"App","Version":"1.0"}]}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("GetApplicationPackages", StringComparison.Ordinal)),
				Arg.Any<string>())
			.Returns("""{"success":true,"packages":[{"uId":"secondary-pkg","name":"SecondaryPkg","isApplicationPrimaryPackage":false},{"uId":"pkg-uid","name":"PrimaryPkg","isApplicationPrimaryPackage":true}]}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("SelectQuery", StringComparison.Ordinal)),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"ApplicationEntity\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[{"UId":"entity-b","Name":"UsrBeta","Caption":"Beta caption"},{"UId":"entity-a","Name":"UsrAlpha","Caption":"Alpha caption"},{"UId":"entity-a","Name":"UsrAlpha","Caption":"Alpha caption"}]}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("RuntimeEntitySchemaRequest", StringComparison.Ordinal)),
				Arg.Is<string>(body => body.Contains("\"uId\":\"entity-a\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"schema":{"uId":"entity-a","name":"UsrAlpha","caption":{"en-US":"Alpha caption"},"columns":{"Items":{"visible":{"name":"Name","caption":{"en-US":"Name"},"dataValueType":1,"isInherited":false},"inherited":{"name":"CreatedOn","caption":{"en-US":"Created On"},"dataValueType":7,"isInherited":true}}}}}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("RuntimeEntitySchemaRequest", StringComparison.Ordinal)),
				Arg.Is<string>(body => body.Contains("\"uId\":\"entity-b\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"schema":{"uId":"entity-b","name":"UsrBeta","caption":{"en-US":"Beta caption"},"columns":{"Items":{"lookup":{"name":"Owner","caption":{"en-US":"Owner"},"dataValueType":10,"isInherited":false,"referenceSchemaName":"Contact","defValue":{"valueSourceType":1,"value":"Default text"}}}}}}""");
		_applicationClient.ExecutePostRequest(
				Arg.Is<string>(url => url.EndsWith("SelectQuery", StringComparison.Ordinal)),
				Arg.Is<string>(body => body.Contains("\"rootSchemaName\":\"SysSchema\"", StringComparison.Ordinal)))
			.Returns("""{"success":true,"rows":[{"Name":"UsrAlpha_FormPage","UId":"page-uid","PackageName":"PrimaryPkg","ParentSchemaName":"BasePage"}]}""");
	}
}
