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
	[Description("Loads application info by app-code, uses package and entity endpoints, removes inherited columns, and sorts the final payload deterministically.")]
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
	}

	[Test]
	[Description("Loads application info by app-id when the installed-application query is filtered by identifier.")]
	public void GetApplicationInfo_Should_Load_Aggregate_By_AppId() {
		// Arrange
		ConfigureHappyPathResponses();

		// Act
		ApplicationInfoResult result = _sut.GetApplicationInfo("sandbox", "app-uid", null);

		// Assert
		result.PackageName.Should().Be("PrimaryPkg",
			because: "app-id lookups should follow the same package resolution flow as app-code lookups");
		_applicationClient.Received().ExecutePostRequest(
			Arg.Is<string>(url => url.EndsWith("SelectQuery", StringComparison.Ordinal)),
			Arg.Is<string>(body => body.Contains("\"columnPath\":\"Id\"", StringComparison.Ordinal) &&
				body.Contains("\"value\":\"app-uid\"", StringComparison.Ordinal)));
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
		exception.Message.Should().Match("*app-id*",
			"the service requires at least one installed-application identifier");
		exception.Message.Should().Match("*app-code*",
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
	}
}
