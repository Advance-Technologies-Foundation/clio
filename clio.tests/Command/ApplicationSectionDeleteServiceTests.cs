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
public sealed class ApplicationSectionDeleteServiceTests {
	private ISettingsRepository _settingsRepository = null!;
	private IApplicationClientFactory _applicationClientFactory = null!;
	private IApplicationClient _applicationClient = null!;
	private IServiceUrlBuilder _serviceUrlBuilder = null!;
	private IApplicationInfoService _applicationInfoService = null!;
	private ILogger _logger = null!;
	private EnvironmentSettings _environmentSettings = null!;
	private ApplicationSectionDeleteService _sut = null!;

	private const string SectionId = "61f65fdb-3b63-4fcf-9110-9863457b3a0b";
	private const string SysModuleEntityId = "aaa10000-0000-0000-0000-000000000001";

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
		_sut = new ApplicationSectionDeleteService(
			_settingsRepository,
			_applicationClientFactory,
			_serviceUrlBuilder,
			_applicationInfoService,
			_logger);
	}

	[Test]
	[Description("Sends a DeleteQuery for the ApplicationSection schema so the app-section link and associated data bindings are removed.")]
	public void DeleteSection_Should_Delete_ApplicationSection_Record() {
		List<string> capturedBodies = [];
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(capturedBodies.Add))
			.Returns(callInfo => BuildMockResponse(callInfo.ArgAt<string>(1)));

		_sut.DeleteSection("sandbox", new ApplicationSectionDeleteRequest("UsrCustomerRequests", "Contact"));

		capturedBodies.Should().Contain(
			body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", System.StringComparison.Ordinal)
				&& body.Contains("DeleteQuery", System.StringComparison.Ordinal)
				&& body.Contains(SectionId, System.StringComparison.OrdinalIgnoreCase),
			because: "delete-app-section must send a DeleteQuery for ApplicationSection to remove the app-section link and clean up data bindings");
	}

	[Test]
	[Description("Sends a DeleteQuery for SysModule to fully remove the section definition.")]
	public void DeleteSection_Should_Delete_SysModule_Record() {
		List<string> capturedBodies = [];
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(capturedBodies.Add))
			.Returns(callInfo => BuildMockResponse(callInfo.ArgAt<string>(1)));

		_sut.DeleteSection("sandbox", new ApplicationSectionDeleteRequest("UsrCustomerRequests", "Contact"));

		capturedBodies.Should().Contain(
			body => body.Contains("\"rootSchemaName\":\"SysModule\"", System.StringComparison.Ordinal)
				&& body.Contains("DeleteQuery", System.StringComparison.Ordinal)
				&& body.Contains(SectionId, System.StringComparison.OrdinalIgnoreCase),
			because: "delete-app-section must delete the SysModule record to remove the section definition");
	}

	[Test]
	[Description("Deletes ApplicationSection before SysModule so FK constraints from ApplicationSection to SysModule are respected.")]
	public void DeleteSection_Should_Delete_ApplicationSection_Before_SysModule() {
		List<string> deleteOrder = [];
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(body => {
				if (body.Contains("\"rootSchemaName\":\"ApplicationSection\"", System.StringComparison.Ordinal)
					&& body.Contains("DeleteQuery", System.StringComparison.Ordinal)) {
					deleteOrder.Add("ApplicationSection");
				} else if (body.Contains("\"rootSchemaName\":\"SysModule\"", System.StringComparison.Ordinal)
					&& body.Contains("DeleteQuery", System.StringComparison.Ordinal)) {
					deleteOrder.Add("SysModule");
				}
			}))
			.Returns(callInfo => BuildMockResponse(callInfo.ArgAt<string>(1)));

		_sut.DeleteSection("sandbox", new ApplicationSectionDeleteRequest("UsrCustomerRequests", "Contact"));

		int appSectionIndex = deleteOrder.IndexOf("ApplicationSection");
		int sysModuleIndex = deleteOrder.IndexOf("SysModule");
		appSectionIndex.Should().BeGreaterThanOrEqualTo(0, because: "ApplicationSection delete must be issued");
		sysModuleIndex.Should().BeGreaterThanOrEqualTo(0, because: "SysModule delete must be issued");
		appSectionIndex.Should().BeLessThan(sysModuleIndex,
			because: "ApplicationSection must be deleted before SysModule to respect FK constraints");
	}

	[Test]
	[Description("Settings-based overload (ENG-93347 Story 8): rejects a null EnvironmentSettings with ArgumentNullException before any client factory or remote call is attempted.")]
	public void DeleteSection_ShouldThrowArgumentNullException_WhenEnvironmentSettingsAreNull() {
		// Arrange
		EnvironmentSettings environmentSettings = null!;
		ApplicationSectionDeleteRequest request = new("UsrCustomerRequests", "Contact");

		// Act
		Action action = () => _sut.DeleteSection(environmentSettings, request);

		// Assert
		action.Should().Throw<ArgumentNullException>(
			because: "the settings-based overload must fail fast on a null tenant before any factory invocation");
		_applicationClientFactory.DidNotReceiveWithAnyArgs().CreateEnvironmentClient(default!);
	}

	[Test]
	[Description("Settings-based overload (ENG-93347 Story 8, AC-03/AC-04): deletes the section against the supplied settings without ever consulting ISettingsRepository, and routes the nested FindApplicationId call through the settings-based overload, never the name-based one.")]
	public void DeleteSection_ShouldUseSettingsBasedNestedCall_WhenEnvironmentSettingsSupplied() {
		// Arrange
		List<string> capturedBodies = [];
		_applicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Do<string>(capturedBodies.Add))
			.Returns(callInfo => BuildMockResponse(callInfo.ArgAt<string>(1)));

		// Act
		ApplicationSectionDeleteResult result = _sut.DeleteSection(
			_environmentSettings,
			new ApplicationSectionDeleteRequest("UsrCustomerRequests", "Contact"));

		// Assert
		result.ApplicationId.Should().Be("app-id",
			because: "the settings-based overload must complete the delete end-to-end against the supplied settings");
		_settingsRepository.DidNotReceiveWithAnyArgs().FindEnvironment(default);
		_settingsRepository.DidNotReceiveWithAnyArgs().GetEnvironment(default(string));
		_applicationInfoService.Received(1).FindApplicationId(_environmentSettings, "UsrCustomerRequests");
		_applicationInfoService.DidNotReceiveWithAnyArgs().FindApplicationId(default(string)!, default!);
		capturedBodies.Should().Contain(
			body => body.Contains("\"rootSchemaName\":\"ApplicationSection\"", System.StringComparison.Ordinal)
				&& body.Contains("DeleteQuery", System.StringComparison.Ordinal),
			because: "the settings-based overload must still issue the ApplicationSection delete against the supplied tenant");
	}

	private static string BuildMockResponse(string requestBody) {
		if (requestBody == string.Empty) {
			return """{"items":[]}""";
		}
		bool isDeleteQuery = requestBody.Contains(
			"\"__type\":\"Terrasoft.Nui.ServiceModel.DataContract.DeleteQuery\"",
			System.StringComparison.Ordinal);
		if (!isDeleteQuery
			&& requestBody.Contains("\"rootSchemaName\":\"ApplicationSection\"", System.StringComparison.Ordinal)) {
			return BuildSectionSelectResponse();
		}
		return """{"success":true}""";
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
		    "ClientTypeId": null,
		    "CardSchemaUId": null,
		    "SysModuleEntityId": "{{SysModuleEntityId}}"
		  }]
		}
		""";
}
