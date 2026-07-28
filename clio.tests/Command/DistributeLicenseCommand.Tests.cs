namespace Clio.Tests.Command;

using System;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.PackageCommand;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class DistributeLicenseCommandTestCase
{

	private const string GetLicensesUrl = "http://localhost/ServiceModel/LicenseManagerProxyService.svc/GetLicenses";
	private const string GetUsersListUrl = "http://localhost/ServiceModel/LicenseManagerProxyService.svc/GetUsersList";

	private IApplicationClient _applicationClient;
	private DistributeLicenseCommandTestable _command;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		var settings = Substitute.For<EnvironmentSettings>();
		settings.Uri = "http://localhost";
		settings.IsNetCore = true;
		_command = new DistributeLicenseCommandTestable(_applicationClient, settings);
	}

	[Test]
	[Description("Building the request throws when neither add-user nor remove-user is specified")]
	public void GetRequestData_NoUsersSpecified_ThrowsArgumentException() {
		// Arrange
		var options = new DistributeLicenseOptions {
			PackageId = "9c40e123-0a44-4cd2-94de-57341b8c3592"
		};

		// Act
		Action act = () => _command.TestGetRequestData(options);

		// Assert
		act.Should().Throw<ArgumentException>("at least one add-user or remove-user is required to build a valid request");
	}

	[Test]
	[Description("Building the request serializes added users, deleted users, packageId and the fixed source")]
	public void GetRequestData_AddedAndRemovedUsers_SerializesExpectedPayload() {
		// Arrange
		var options = new DistributeLicenseOptions {
			PackageId = "9c40e123-0a44-4cd2-94de-57341b8c3592",
			AddUser = new[] { "7f3b869f-34f3-4f20-ab4d-7480a5fdf647" },
			RemoveUser = new[] { "11111111-1111-1111-1111-111111111111" }
		};

		// Act
		string requestData = _command.TestGetRequestData(options);

		// Assert
		requestData.Should().Be(
			"{\"deletedUsers\":[\"11111111-1111-1111-1111-111111111111\"],"
			+ "\"addedUsers\":[\"7f3b869f-34f3-4f20-ab4d-7480a5fdf647\"],"
			+ "\"packageId\":\"9c40e123-0a44-4cd2-94de-57341b8c3592\",\"source\":1}",
			"the request body must match the LicenseManagerProxyService.svc/SaveLicenseData contract");
	}

	[Test]
	[Description("Building the request with only add-user leaves deletedUsers empty")]
	public void GetRequestData_OnlyAddedUsers_SerializesEmptyDeletedUsers() {
		// Arrange
		var options = new DistributeLicenseOptions {
			PackageId = "9c40e123-0a44-4cd2-94de-57341b8c3592",
			AddUser = new[] { "7f3b869f-34f3-4f20-ab4d-7480a5fdf647" }
		};

		// Act
		string requestData = _command.TestGetRequestData(options);

		// Assert
		requestData.Should().Be(
			"{\"deletedUsers\":[],\"addedUsers\":[\"7f3b869f-34f3-4f20-ab4d-7480a5fdf647\"],"
			+ "\"packageId\":\"9c40e123-0a44-4cd2-94de-57341b8c3592\",\"source\":1}",
			"deletedUsers must serialize as an empty array when no --remove-user was supplied");
	}

	[Test]
	[Description("A success response does not throw")]
	public void ProceedResponse_SuccessResponse_DoesNotThrow() {
		// Arrange
		var response = "{\"success\": true}";
		var options = new DistributeLicenseOptions();

		// Act
		Action act = () => _command.TestProceedResponse(response, options);

		// Assert
		act.Should().NotThrow("a success:true response means the license package was saved");
	}

	[Test]
	[Description("An error response with errorInfo throws with the server-provided message and code")]
	public void ProceedResponse_ErrorResponseWithErrorInfo_ThrowsException() {
		// Arrange
		var response = @"
				{
					""success"": false,
					""errorInfo"": {
						""message"": ""User already licensed"",
						""errorCode"": ""ALREADY_LICENSED""
					}
				}";
		var options = new DistributeLicenseOptions();

		// Act
		Action act = () => _command.TestProceedResponse(response, options);

		// Assert
		act.Should().Throw<LicenseInstallationException>("the server reported a failure via errorInfo")
			.WithMessage("License data not saved. ErrorCode: ALREADY_LICENSED, Message: User already licensed");
	}

	[Test]
	[Description("An error response without errorInfo throws a generic exception")]
	public void ProceedResponse_ErrorResponseWithoutErrorInfo_ThrowsException() {
		// Arrange
		var response = "{\"success\": false}";
		var options = new DistributeLicenseOptions();

		// Act
		Action act = () => _command.TestProceedResponse(response, options);

		// Assert
		act.Should().Throw<LicenseInstallationException>("success:false without errorInfo still needs to fail loudly")
			.WithMessage("License data not saved: Unknown error details");
	}

	[Test]
	[Description("An error response with a JSON null errorInfo (a valid, common shape) throws the generic exception instead of an InvalidOperationException from TryGetProperty on a null element")]
	public void ProceedResponse_ErrorResponseWithNullErrorInfo_ThrowsLicenseInstallationException() {
		// Arrange
		var response = "{\"success\": false, \"errorInfo\": null}";
		var options = new DistributeLicenseOptions();

		// Act
		Action act = () => _command.TestProceedResponse(response, options);

		// Assert
		act.Should().Throw<LicenseInstallationException>(
				"errorInfo:null must be treated the same as a missing errorInfo, not crash while reading its fields")
			.WithMessage("License data not saved: Unknown error details");
	}

	[Test]
	[Description("An authentication failure response throws a dedicated exception message")]
	public void ProceedResponse_AuthenticationFailed_ThrowsLicenseInstallationException() {
		// Arrange
		var response = "{\"Message\":\"Authentication failed.\",\"StackTrace\":null,\"ExceptionType\":\"System.InvalidOperationException\"}";
		var options = new DistributeLicenseOptions();

		// Act
		Action act = () => _command.TestProceedResponse(response, options);

		// Assert
		act.Should().Throw<LicenseInstallationException>("the raw response indicates the session was not authenticated")
			.WithMessage("License data not saved: Authentication failed.");
	}

	[Test]
	[Description("A non-JSON login-redirect page containing the auth-failed marker throws before any JSON parsing is attempted")]
	public void ProceedResponse_NonJsonAuthenticationFailedResponse_ThrowsLicenseInstallationException() {
		// Arrange
		var response = "<html><body>Authentication failed. Please log in again.</body></html>";
		var options = new DistributeLicenseOptions();

		// Act
		Action act = () => _command.TestProceedResponse(response, options);

		// Assert
		act.Should().Throw<LicenseInstallationException>("an expired session redirects to an HTML login page, not JSON")
			.WithMessage("License data not saved: Authentication failed.");
	}

	[Test]
	[Description("Blank entries in --add-user are ignored rather than sent to the server")]
	public void GetRequestData_BlankAddUserEntry_IsIgnored() {
		// Arrange
		var options = new DistributeLicenseOptions {
			PackageId = "9c40e123-0a44-4cd2-94de-57341b8c3592",
			AddUser = new[] { "7f3b869f-34f3-4f20-ab4d-7480a5fdf647", "  ", null }
		};

		// Act
		string requestData = _command.TestGetRequestData(options);

		// Assert
		requestData.Should().Be(
			"{\"deletedUsers\":[],\"addedUsers\":[\"7f3b869f-34f3-4f20-ab4d-7480a5fdf647\"],"
			+ "\"packageId\":\"9c40e123-0a44-4cd2-94de-57341b8c3592\",\"source\":1}",
			"blank/null entries are not valid user ids and must not reach the server");
	}

	[Test]
	[Description("A --add-user value that does not resolve to any GetUsersList entry throws")]
	public void GetRequestData_UnresolvableAddUserName_ThrowsArgumentException() {
		// Arrange
		ArrangeUsersListRows();
		var options = new DistributeLicenseOptions {
			PackageId = "9c40e123-0a44-4cd2-94de-57341b8c3592",
			AddUser = new[] { "not-a-guid-or-a-real-user" }
		};

		// Act
		Action act = () => _command.TestGetRequestData(options);

		// Assert
		act.Should().Throw<ArgumentException>("a name that resolves to zero GetUsersList entries cannot be sent to SaveLicenseData");
	}

	[Test]
	[Description("A --package-id value that does not resolve via GetLicenses throws")]
	public void GetRequestData_UnresolvablePackageName_ThrowsArgumentException() {
		// Arrange
		_applicationClient.ExecutePostRequest(GetLicensesUrl, "{}", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns(JsonSerializer.Serialize(Array.Empty<object>()));
		var options = new DistributeLicenseOptions {
			PackageId = "not-a-guid-or-a-real-package",
			AddUser = new[] { "7f3b869f-34f3-4f20-ab4d-7480a5fdf647" }
		};

		// Act
		Action act = () => _command.TestGetRequestData(options);

		// Assert
		act.Should().Throw<ArgumentException>("a name that resolves to zero GetLicenses entries cannot be sent to SaveLicenseData");
	}

	[Test]
	[Description("The same user id in both --add-user and --remove-user is rejected as an ambiguous request")]
	public void GetRequestData_UserInBothAddAndRemove_ThrowsArgumentException() {
		// Arrange
		var options = new DistributeLicenseOptions {
			PackageId = "9c40e123-0a44-4cd2-94de-57341b8c3592",
			AddUser = new[] { "7f3b869f-34f3-4f20-ab4d-7480a5fdf647" },
			RemoveUser = new[] { "7f3b869f-34f3-4f20-ab4d-7480a5fdf647" }
		};

		// Act
		Action act = () => _command.TestGetRequestData(options);

		// Assert
		act.Should().Throw<ArgumentException>("a user cannot be both added and removed in the same request");
	}

	[Test]
	[Description("A --add-user name resolves via the real GetUsersList shape, where id/name live nested under sysAdminUnit.value/displayValue")]
	public void GetRequestData_AddUserByName_ResolvesViaGetUsersList() {
		// Arrange
		ArrangeUsersListRows(
			("8ab1343f-cb58-49c7-95a2-058b5f60acd3", "Mandrill"),
			("7f3b869f-34f3-4f20-ab4d-7480a5fdf647", "Supervisor"),
			("52958c15-14f4-4ef0-86ee-bb33a98fb828", "SysPortalConnection"));
		var options = new DistributeLicenseOptions {
			PackageId = "9c40e123-0a44-4cd2-94de-57341b8c3592",
			AddUser = new[] { "Supervisor" }
		};

		// Act
		string requestData = _command.TestGetRequestData(options);

		// Assert
		requestData.Should().Be(
			"{\"deletedUsers\":[],\"addedUsers\":[\"7f3b869f-34f3-4f20-ab4d-7480a5fdf647\"],"
			+ "\"packageId\":\"9c40e123-0a44-4cd2-94de-57341b8c3592\",\"source\":1}",
			"the 'Supervisor' name must resolve to sysAdminUnit.value before the request is serialized");
	}

	[Test]
	[Description("Name resolution passes the command's configured request timeout, max attempts and retry delay to the lookup call instead of the client's own defaults, so --timeout is honored and a stalled lookup endpoint cannot hang indefinitely")]
	public void GetRequestData_NameResolution_UsesConfiguredTimeoutAndRetrySettings() {
		// Arrange
		ArrangeUsersListRows(("7f3b869f-34f3-4f20-ab4d-7480a5fdf647", "Supervisor"));
		_command.RequestTimeout = 12345;
		_command.MaxAttempts = 4;
		_command.DelaySec = 7;
		var options = new DistributeLicenseOptions {
			PackageId = "9c40e123-0a44-4cd2-94de-57341b8c3592",
			AddUser = new[] { "Supervisor" }
		};

		// Act
		_command.TestGetRequestData(options);

		// Assert
		_applicationClient.Received(1).ExecutePostRequest(GetUsersListUrl, "{}", 12345, 4, 7);
	}

	[Test]
	[Description("A --add-user name matching more than one GetUsersList entry is rejected as ambiguous")]
	public void GetRequestData_AmbiguousAddUserName_ThrowsArgumentException() {
		// Arrange
		ArrangeUsersListRows(
			("11111111-1111-1111-1111-111111111111", "John"),
			("22222222-2222-2222-2222-222222222222", "John"));
		var options = new DistributeLicenseOptions {
			PackageId = "9c40e123-0a44-4cd2-94de-57341b8c3592",
			AddUser = new[] { "John" }
		};

		// Act
		Action act = () => _command.TestGetRequestData(options);

		// Assert
		act.Should().Throw<ArgumentException>("more than one GetUsersList entry matching the same name is ambiguous");
	}

	[Test]
	[Description("An HTML response from GetUsersList (e.g. an unreachable service) is wrapped in an actionable ArgumentException instead of leaking a raw JsonException")]
	public void GetRequestData_NonJsonGetUsersListResponse_ThrowsActionableArgumentException() {
		// Arrange
		_applicationClient.ExecutePostRequest(GetUsersListUrl, "{}", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns("<html><body>Unexpected error</body></html>");
		var options = new DistributeLicenseOptions {
			PackageId = "9c40e123-0a44-4cd2-94de-57341b8c3592",
			AddUser = new[] { "Supervisor" }
		};

		// Act
		Action act = () => _command.TestGetRequestData(options);

		// Assert
		act.Should().Throw<ArgumentException>("an HTML response means GetUsersList could not be parsed, which must not surface as a raw JsonException")
			.WithMessage("*Pass the Guid directly via --add-user/--remove-user*");
	}

	[Test]
	[Description("An HTML response from GetLicenses is wrapped in an actionable ArgumentException instead of leaking a raw JsonException")]
	public void GetRequestData_NonJsonGetLicensesResponse_ThrowsActionableArgumentException() {
		// Arrange
		_applicationClient.ExecutePostRequest(GetLicensesUrl, "{}", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns("<html><body>Unexpected error</body></html>");
		var options = new DistributeLicenseOptions {
			PackageId = "studio creatio on-site subscription",
			AddUser = new[] { "7f3b869f-34f3-4f20-ab4d-7480a5fdf647" }
		};

		// Act
		Action act = () => _command.TestGetRequestData(options);

		// Assert
		act.Should().Throw<ArgumentException>("an HTML response means GetLicenses could not be parsed, which must not surface as a raw JsonException")
			.WithMessage("*Pass the Guid directly via --package-id*");
	}

	[Test]
	[Description("A --package-id name resolves via the real GetLicenses shape: entries live under 'licenses', and packageId (not the license record's own id) is the value SaveLicenseData expects")]
	public void GetRequestData_PackageIdByName_ResolvesViaGetLicenses() {
		// Arrange
		var licenses = new object[] {
			new {
				id = "68671333-f9bd-4343-a218-dd636adcd193", // the license record's own id: must NOT be sent
				packageId = "9c40e123-0a44-4cd2-94de-57341b8c3592",
				packageName = "studio creatio on-site subscription"
			},
			new {
				id = "11111111-1111-1111-1111-111111111111",
				packageId = "22222222-2222-2222-2222-222222222222",
				packageName = "Other package"
			}
		};
		_applicationClient.ExecutePostRequest(GetLicensesUrl, "{}", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(JsonSerializer.Serialize(new { success = true, errorInfo = (object)null, licenses }));
		var options = new DistributeLicenseOptions {
			PackageId = "studio creatio on-site subscription",
			AddUser = new[] { "7f3b869f-34f3-4f20-ab4d-7480a5fdf647" }
		};

		// Act
		string requestData = _command.TestGetRequestData(options);

		// Assert
		requestData.Should().Be(
			"{\"deletedUsers\":[],\"addedUsers\":[\"7f3b869f-34f3-4f20-ab4d-7480a5fdf647\"],"
			+ "\"packageId\":\"9c40e123-0a44-4cd2-94de-57341b8c3592\",\"source\":1}",
			"packageId, not the license record's own id, is what LicenseManagerProxyService.svc/SaveLicenseData expects");
	}

	[Test]
	[Description("Multiple license rows for the same package (e.g. different terms/tiers) share one packageName and packageId and must not be reported as ambiguous")]
	public void GetRequestData_PackageNameWithMultipleTermsForSamePackage_IsNotAmbiguous() {
		// Arrange
		var licenses = new object[] {
			new {
				id = "68671333-f9bd-4343-a218-dd636adcd193",
				packageId = "9c40e123-0a44-4cd2-94de-57341b8c3592",
				packageName = "studio creatio on-site subscription"
			},
			new {
				id = "1c4ad873-1c79-44fd-a772-81dc7500bef2", // different license-record id, same package
				packageId = "9c40e123-0a44-4cd2-94de-57341b8c3592",
				packageName = "studio creatio on-site subscription"
			}
		};
		_applicationClient.ExecutePostRequest(GetLicensesUrl, "{}", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(JsonSerializer.Serialize(new { success = true, errorInfo = (object)null, licenses }));
		var options = new DistributeLicenseOptions {
			PackageId = "studio creatio on-site subscription",
			AddUser = new[] { "7f3b869f-34f3-4f20-ab4d-7480a5fdf647" }
		};

		// Act
		string requestData = _command.TestGetRequestData(options);

		// Assert
		requestData.Should().Contain(
			"\"packageId\":\"9c40e123-0a44-4cd2-94de-57341b8c3592\"",
			"two rows resolving to the same packageId must be deduplicated instead of raising an ambiguity error");
	}

	private void ArrangeUsersListRows(params (string Id, string Name)[] users) {
		var payload = users.Select(u => new {
			email = "",
			jobTitle = "",
			source = 0,
			sysAdminUnit = new {
				displayValue = u.Name,
				primaryColorValue = (string)null,
				primaryImageValue = "00000000-0000-0000-0000-000000000000",
				value = u.Id
			}
		});
		_applicationClient.ExecutePostRequest(GetUsersListUrl, "{}", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(JsonSerializer.Serialize(new { errorInfo = (object)null, success = true, users = payload }));
	}
}

public class DistributeLicenseCommandTestable : DistributeLicenseCommand
{
	public DistributeLicenseCommandTestable(IApplicationClient applicationClient, EnvironmentSettings settings)
		: base(applicationClient, settings) {
	}

	public string TestGetRequestData(DistributeLicenseOptions options) {
		return GetRequestData(options);
	}

	public void TestProceedResponse(string response, DistributeLicenseOptions options) {
		ProceedResponse(response, options);
	}
}
