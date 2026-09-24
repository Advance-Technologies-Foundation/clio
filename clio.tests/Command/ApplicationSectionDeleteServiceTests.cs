using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
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
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Structure", "NUnit1032:An IDisposable field/property should be Disposed in a TearDown method",
		Justification = "The system under test owns and disposes the factory-returned substitute.")]
	private IOwnedApplicationClient _applicationClient = null!;
	private IServiceUrlBuilder _serviceUrlBuilder = null!;
	private IApplicationInfoService _applicationInfoService = null!;
	private ILogger _logger = null!;
	private EnvironmentSettings _environmentSettings = null!;
	private ApplicationSectionDeleteService _sut = null!;

	private ServiceProvider _provider = null!;

	private const string SectionId = "61f65fdb-3b63-4fcf-9110-9863457b3a0b";
	private const string SysModuleEntityId = "aaa10000-0000-0000-0000-000000000001";

	[SetUp]
	public void SetUp() {
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_applicationClientFactory = Substitute.For<IApplicationClientFactory>();
		_applicationClient = Substitute.For<IOwnedApplicationClient>();
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
		ServiceCollection services = new();
		services.AddSingleton(_settingsRepository);
		services.AddSingleton(_applicationClientFactory);
		services.AddSingleton(_serviceUrlBuilder);
		services.AddSingleton(_applicationInfoService);
		services.AddSingleton(_logger);
		services.AddTransient<IApplicationSectionDeleteService, ApplicationSectionDeleteService>();
		_provider = services.BuildServiceProvider();
		_sut = (ApplicationSectionDeleteService)_provider.GetRequiredService<IApplicationSectionDeleteService>();
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

	[TearDown]
	public void TearDown() {
		_provider.Dispose();
		_applicationClient.ClearReceivedCalls();
	}

	[TestCase(false)]
	[TestCase(true)]
	[Description("Deletes only declared page identities and the exact opted-in entity, preserving all prefix neighbors and unowned auxiliary schemas.")]
	public void DeleteSection_ShouldPreserveNeighbors_WhenNamesSharePrefix(bool deleteEntity) {
		// Arrange
		List<string> deletedNames = [];
		string items = JsonSerializer.Serialize(new { items = new[] {
			new { uId = "731ef26f-5a01-4e9d-8586-2e83b5ae6998", name = "RenamedList", type = 4 },
			new { uId = "731ef26f-5a01-4e9d-8586-2e83b5ae6999", name = "Contact", type = 3 },
			new { uId = "731ef26f-5a01-4e9d-8586-2e83b5ae6000", name = "ContactAllowedParent", type = 3 },
			new { uId = "731ef26f-5a01-4e9d-8586-2e83b5ae6001", name = "ContactAllowedParentListener", type = 5 },
			new { uId = "731ef26f-5a01-4e9d-8586-2e83b5ae6002", name = "ContactAllowedParentForm", type = 4 },
			new { uId = "731ef26f-5a01-4e9d-8586-2e83b5ae6003", name = "ContactRelatedPage", type = 11 },
			new { uId = "731ef26f-5a01-4e9d-8586-2e83b5ae6004", name = "Contact_MobileListPage", type = 4 }
		}});
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(call => {
			string body = call.ArgAt<string>(1);
			if (body.Length == 0) return items;
			if (body.StartsWith("[", StringComparison.Ordinal)) {
				using JsonDocument doc = JsonDocument.Parse(body);
				deletedNames.Add(doc.RootElement[0].GetProperty("name").GetString()!);
			}
			return BuildMockResponse(body).Replace("\"Code\": \"Contact\"", "\"Code\": \"DifferentSectionCode\"");
		});

		// Act
		_sut.DeleteSection("sandbox", new ApplicationSectionDeleteRequest("UsrCustomerRequests", "DifferentSectionCode", deleteEntity));

		// Assert
		deletedNames.Should().Equal(deleteEntity ? new[] { "RenamedList", "Contact" } : new[] { "RenamedList" },
			because: "neither a shared prefix nor the section code proves artifact ownership or identifies its entity");
	}

	[TestCase("{\"success\":false,\"items\":[]}")]
	[TestCase("{}")]
	[TestCase("{\"items\":[{\"uId\":\"731ef26f-5a01-4e9d-8586-2e83b5ae6998\",\"name\":\"UnexpectedEntity\",\"type\":3}]}")]
	[TestCase("{\"items\":[{\"uId\":\"731ef26f-5a01-4e9d-8586-2e83b5ae6998\",\"type\":4},{\"uId\":\"731ef26f-5a01-4e9d-8586-2e83b5ae6998\",\"type\":4}]}")]
	[Description("Fails before any writes when workspace discovery fails or a declared page identity is ambiguous or has the wrong type.")]
	public void DeleteSection_ShouldRefuseBeforeWrites_WhenDiscoveryIsUnsafe(string items) {
		// Arrange
		List<string> urls = [];
		_applicationClient.ExecutePostRequest(Arg.Do<string>(urls.Add), Arg.Any<string>())
			.Returns(call => call.ArgAt<string>(1).Length == 0 ? items : BuildMockResponse(call.ArgAt<string>(1)));

		// Act
		Action act = () => _sut.DeleteSection("sandbox", new("UsrCustomerRequests", "Contact"));

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "uncertain discovery must fail closed");
		urls.Should().NotContain(url => url.EndsWith("/Delete", StringComparison.Ordinal) || url.EndsWith("/DeleteWorkspaceItem", StringComparison.Ordinal),
			because: "the complete deletion set must be resolved before any mutation");
	}

	[TestCase(false)]
	[TestCase(true)]
	[Description("Refuses missing or ambiguous entity names before any delete when entity removal is explicitly requested.")]
	public void DeleteSection_ShouldRefuseBeforeWrites_WhenEntityCannotBeResolvedUniquely(bool duplicate) {
		// Arrange
		string entity = """{"uId":"731ef26f-5a01-4e9d-8586-2e83b5ae6999","name":"Contact","type":3}""";
		string items = duplicate ? "{\"items\":[" + entity + "," + entity + "]}" : "{\"items\":[]}";
		List<string> urls = [];
		_applicationClient.ExecutePostRequest(Arg.Do<string>(urls.Add), Arg.Any<string>())
			.Returns(call => call.ArgAt<string>(1).Length == 0 ? items : BuildMockResponse(call.ArgAt<string>(1)));

		// Act
		Action act = () => _sut.DeleteSection("sandbox", new("UsrCustomerRequests", "Contact", true));

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "an opt-in does not authorize guessing the entity identity");
		urls.Should().NotContain(url => url.EndsWith("/Delete", StringComparison.Ordinal), because: "ambiguous entity discovery must leave bindings intact");
	}

	[Test]
	[Description("Returns an error rather than success when the platform refuses a schema deletion.")]
	public void DeleteSection_ShouldSurfaceFailure_WhenSchemaDeleteIsRejected() {
		// Arrange
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(call => {
			string body = call.ArgAt<string>(1);
			if (body.Length == 0) return """{"items":[{"uId":"731ef26f-5a01-4e9d-8586-2e83b5ae6998","name":"ContactPage","type":4}]}""";
			return body.StartsWith("[", StringComparison.Ordinal) ? """{"success":false,"errorInfo":{"message":"dependent objects"}}""" : BuildMockResponse(body);
		});

		// Act
		Action act = () => _sut.DeleteSection("sandbox", new("UsrCustomerRequests", "Contact"));

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("*dependent objects*", because: "failed destructive operations cannot be reported as successful");
	}
	[TestCase("section")]
	[TestCase("card")]
	[TestCase("edit")]
	[TestCase("mini")]
	[TestCase("search")]
	[Description("Retains a declared list page when another section or independent edit-page registration uses that schema.")]
	public void DeleteSection_ShouldPreserveSharedPage_WhenAnotherSurfaceReferencesIt(string reference) {
		// Arrange
		const string pageUId = "731ef26f-5a01-4e9d-8586-2e83b5ae6998";
		List<string> schemaDeletes = [];
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(call => {
			string body = call.ArgAt<string>(1);
			if (body.Length == 0) return """{"items":[{"uId":"731ef26f-5a01-4e9d-8586-2e83b5ae6998","name":"SharedPage","type":4}]}""";
			if (body.StartsWith("[", StringComparison.Ordinal)) schemaDeletes.Add(body);
			if (body.Contains("\"rootSchemaName\":\"SysModuleEdit\"", StringComparison.Ordinal)) {
				string field = reference == "edit" ? "CardSchemaUId" : reference == "mini" ? "SectionSchemaUId" : "SearchRowSchemaUId";
				return reference is "edit" or "mini" or "search" ? "{\"success\":true,\"rows\":[{\"" + field + "\":\"" + pageUId + "\"}]}" : """{"success":true,"rows":[]}""";
			}
			string result = BuildMockResponse(body);
			if (call.ArgAt<string>(0).EndsWith("/Select", StringComparison.Ordinal) && body.Contains("\"rootSchemaName\":\"SysModule\"", StringComparison.Ordinal)) {
				using JsonDocument doc = JsonDocument.Parse(result);
				string field = reference == "section" ? "SectionSchemaUId" : "CardSchemaUId";
				if (reference is "section" or "card") return "{\"success\":true,\"rows\":[" + doc.RootElement.GetProperty("rows")[0].GetRawText() + ",{\"Id\":\"other\",\"" + field + "\":\"" + pageUId + "\"}]}";
			}
			return result;
		});

		// Act
		_sut.DeleteSection("sandbox", new("UsrCustomerRequests", "Contact"));

		// Assert
		schemaDeletes.Should().BeEmpty(because: "a shared page is not owned exclusively by the deleted section");
	}

	[TestCase(false, false)]
	[TestCase(true, false)]
	[TestCase(true, true)]
	[Description("Retains shared entity bindings, and refuses opted-in shared entity removal before all mutations even with distinct bindings.")]
	public void DeleteSection_ShouldProtectSharedEntity_WhenAnotherSectionUsesIt(bool deleteEntity, bool distinctBinding) {
		// Arrange
		const string entityUId = "731ef26f-5a01-4e9d-8586-2e83b5ae6999";
		List<string> writes = [];
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>()).Returns(call => {
			string url = call.ArgAt<string>(0);
			string body = call.ArgAt<string>(1);
			if (url.EndsWith("/Delete", StringComparison.Ordinal) || url.EndsWith("/DeleteWorkspaceItem", StringComparison.Ordinal)) writes.Add(body);
			if (body.Length == 0) return "{\"items\":[{\"uId\":\"" + entityUId + "\",\"name\":\"Contact\",\"type\":3}]}";
			string response = BuildMockResponse(body);
			if (url.EndsWith("/Select", StringComparison.Ordinal) && body.Contains("\"rootSchemaName\":\"SysModule\"", StringComparison.Ordinal)) {
				using JsonDocument doc = JsonDocument.Parse(response);
				return "{\"success\":true,\"rows\":[" + doc.RootElement.GetProperty("rows")[0].GetRawText()
					+ ",{\"Id\":\"other\",\"SysModuleEntityId\":\"" + (distinctBinding ? "other-binding" : SysModuleEntityId)
					+ "\",\"EntitySchemaUId\":\"" + entityUId + "\"}]}";
			}
			return response;
		});

		// Act
		Action act = () => _sut.DeleteSection("sandbox", new("UsrCustomerRequests", "Contact", deleteEntity));

		// Assert
		if (deleteEntity) {
			act.Should().Throw<InvalidOperationException>().WithMessage("*used by another section*", because: "entity opt-in cannot remove another section's entity");
			writes.Should().BeEmpty(because: "shared entity refusal must happen before mutation");
		} else {
			act.Should().NotThrow(because: "removing a section with a shared entity is safe when the entity and binding are retained");
			writes.Should().NotContain(body => body.Contains("\"rootSchemaName\":\"SysModuleEntity\"", StringComparison.Ordinal), because: "another section still owns the binding");
		}
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
		if (!isDeleteQuery && requestBody.Contains("\"rootSchemaName\":\"SysModuleEdit\"", StringComparison.Ordinal)) {
            return """{"success":true,"rows":[]}""";
		}
		if (!isDeleteQuery && requestBody.Contains("\"rootSchemaName\":\"SysModule\"", StringComparison.Ordinal)) {
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
