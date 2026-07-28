using System;
using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Direct service-level coverage for <see cref="ApplicationSectionGetListService"/>'s settings-based
/// overload (ENG-93347 Story 9), mirroring <see cref="ApplicationSectionDeleteServiceTests"/> from
/// Story 8: the settings-based overload must never consult <see cref="ISettingsRepository"/> and must
/// route the nested <c>FindApplicationId</c> lookup through the settings-based
/// <see cref="IApplicationInfoService"/> overload, never the name-based one.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ApplicationSectionGetListServiceTests {
	private ISettingsRepository _settingsRepository = null!;
	private IApplicationClientFactory _applicationClientFactory = null!;
	private IApplicationClient _applicationClient = null!;
	private IServiceUrlBuilder _serviceUrlBuilder = null!;
	private IApplicationInfoService _applicationInfoService = null!;
	private ILogger _logger = null!;
	private EnvironmentSettings _environmentSettings = null!;
	private ApplicationSectionGetListService _sut = null!;

	private const string SectionId = "61f65fdb-3b63-4fcf-9110-9863457b3a0b";

	[SetUp]
	public void SetUp() {
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_applicationInfoService = Substitute.For<IApplicationInfoService>();
		_logger = Substitute.For<ILogger>();
		_environmentSettings = new EnvironmentSettings {
			Uri = "https://example.invalid",
			Login = "Supervisor",
			Password = "Supervisor",
			IsNetCore = true
		};
		_settingsRepository.FindEnvironment("sandbox").Returns(_environmentSettings);
		_applicationClientFactory.CreateEnvironmentClient(_environmentSettings).Returns(_applicationClient);
		_serviceUrlBuilder
			.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>(), Arg.Any<EnvironmentSettings>())
			.Returns(callInfo => $"https://example.invalid/{callInfo.ArgAt<ServiceUrlBuilder.KnownRoute>(0)}");
		_applicationInfoService
			.FindApplicationId("sandbox", "UsrCustomerRequests")
			.Returns(new InstalledAppSummary("app-id", "UsrCustomerRequests", "Customer Requests", "1.0.0"));
		_applicationInfoService
			.FindApplicationId(_environmentSettings, "UsrCustomerRequests")
			.Returns(new InstalledAppSummary("app-id", "UsrCustomerRequests", "Customer Requests", "1.0.0"));
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>())
			.Returns(BuildSectionSelectResponse());
		_sut = new ApplicationSectionGetListService(
			_settingsRepository,
			_applicationClientFactory,
			_serviceUrlBuilder,
			_applicationInfoService,
			_logger);
	}

	[Test]
	[Description("Settings-based overload (ENG-93347 Story 9): rejects a null EnvironmentSettings with ArgumentNullException before any client factory or remote call is attempted.")]
	public void GetSections_ShouldThrowArgumentNullException_WhenEnvironmentSettingsAreNull() {
		// Arrange
		EnvironmentSettings environmentSettings = null!;
		ApplicationSectionGetListRequest request = new("UsrCustomerRequests");

		// Act
		Action action = () => _sut.GetSections(environmentSettings, request);

		// Assert
		action.Should().Throw<ArgumentNullException>(
			because: "the settings-based overload must fail fast on a null tenant before any factory invocation");
		_applicationClientFactory.DidNotReceiveWithAnyArgs().CreateEnvironmentClient(default!);
	}

	[Test]
	[Description("Settings-based overload (ENG-93347 Story 9, AC-03/AC-04): lists sections against the supplied settings without ever consulting ISettingsRepository, and routes the nested FindApplicationId call through the settings-based overload, never the name-based one.")]
	public void GetSections_ShouldUseSettingsBasedNestedCall_WhenEnvironmentSettingsSupplied() {
		// Act
		ApplicationSectionGetListResult result = _sut.GetSections(
			_environmentSettings,
			new ApplicationSectionGetListRequest("UsrCustomerRequests"));

		// Assert
		result.ApplicationId.Should().Be("app-id",
			because: "the settings-based overload must complete the section list end-to-end against the supplied settings");
		result.Sections.Should().ContainSingle(
			because: "the settings-based overload must still surface the section collection from the supplied tenant");
		_settingsRepository.DidNotReceiveWithAnyArgs().FindEnvironment(default);
		_settingsRepository.DidNotReceiveWithAnyArgs().GetEnvironment(default(string));
		_applicationInfoService.Received(1).FindApplicationId(_environmentSettings, "UsrCustomerRequests");
		_applicationInfoService.DidNotReceiveWithAnyArgs().FindApplicationId(default(string)!, default!);
	}

	private static string BuildSectionSelectResponse() =>
		$$"""
		{
		  "success": true,
		  "rows": [{
		    "Id": "{{SectionId}}",
		    "ApplicationId": "app-id",
		    "Caption": "Contacts",
		    "Code": "Contact",
		    "Description": "Contacts section",
		    "EntitySchemaName": "Contact",
		    "PackageId": "00000000-0000-0000-0000-000000000000",
		    "SectionSchemaUId": "731ef26f-5a01-4e9d-8586-2e83b5ae6998",
		    "LogoId": "icon-id",
		    "IconBackground": "#A49839",
		    "ClientTypeId": null
		  }]
		}
		""";
}
