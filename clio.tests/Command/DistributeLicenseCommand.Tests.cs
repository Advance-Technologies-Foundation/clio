namespace Clio.Tests.Command;

using System;
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

	private DistributeLicenseCommandTestable _command;

	[SetUp]
	public void SetUp() {
		_command = new DistributeLicenseCommandTestable(Substitute.For<IApplicationClient>(),
			Substitute.For<EnvironmentSettings>());
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
	[Description("A malformed --add-user value throws instead of silently reaching the server")]
	public void GetRequestData_InvalidAddUserGuid_ThrowsArgumentException() {
		// Arrange
		var options = new DistributeLicenseOptions {
			PackageId = "9c40e123-0a44-4cd2-94de-57341b8c3592",
			AddUser = new[] { "not-a-guid" }
		};

		// Act
		Action act = () => _command.TestGetRequestData(options);

		// Assert
		act.Should().Throw<ArgumentException>("user ids must be valid Guids before being sent to SaveLicenseData");
	}

	[Test]
	[Description("A malformed --package-id value throws instead of silently reaching the server")]
	public void GetRequestData_InvalidPackageId_ThrowsArgumentException() {
		// Arrange
		var options = new DistributeLicenseOptions {
			PackageId = "not-a-guid",
			AddUser = new[] { "7f3b869f-34f3-4f20-ab4d-7480a5fdf647" }
		};

		// Act
		Action act = () => _command.TestGetRequestData(options);

		// Assert
		act.Should().Throw<ArgumentException>("the package id must be a valid Guid before being sent to SaveLicenseData");
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
