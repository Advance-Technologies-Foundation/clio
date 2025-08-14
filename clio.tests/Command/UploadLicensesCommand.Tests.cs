namespace Clio.Tests.Command;

using Clio.Command;
using Clio.Command.PackageCommand;
using Clio.Common;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
public class UploadLicensesCommandTestCase
{
	private UploadLicensesCommandTestable _command;

	[SetUp]
	public void SetUp() {
		_command = new UploadLicensesCommandTestable(Substitute.For<IApplicationClient>(),
			Substitute.For<EnvironmentSettings>());
	}

	[Test]
	public void TestProceedResponse_SuccessResponse_DoesNotThrow() {
		var response = "{\"success\": true}";
		var options = new UploadLicensesOptions();
		Assert.DoesNotThrow(() => _command.TestProceedResponse(response, options));
	}

	[Test]
	public void TestProceedResponse_ErrorResponseWithErrorInfo_ThrowsException() {
		var response = @"
				{
					""success"": false,
					""errorInfo"": {
						""message"": ""Invalid license key"",
						""errorCode"": ""INVALID_KEY""
					}
				}";
		var options = new UploadLicensesOptions();
		var ex = Assert.Throws<LicenseInstallationException>(() =>
			_command.TestProceedResponse(response, options));
		Assert.That(ex.Message,
			Is.EqualTo("License not installed. ErrorCode: INVALID_KEY, Message: Invalid license key"));
	}

	[Test]
	public void TestProceedResponse_ErrorResponseWithoutErrorInfo_ThrowsException() {
		var response = "{\"success\": false}";
		var options = new UploadLicensesOptions();
		var ex = Assert.Throws<LicenseInstallationException>(() =>
			_command.TestProceedResponse(response, options));
		Assert.That(ex.Message, Is.EqualTo("License not installed: Unknown error details"));
	}

	[Test]
	public void TestProceedResponse_ResponseWithoutSuccessProperty_DoesThrow() {
		var response = "{\"errorInfo\": { \"message\": \"Error occurred\" }}";
		var options = new UploadLicensesOptions();
		Assert.Throws<LicenseInstallationException>(() => _command.TestProceedResponse(response, options));
	}

	[Test]
	public void TestProceedResponse_AuthenticationFailed_ThrowsLicenseInstallationException() {
		var response = "{\"Message\":\"Authentication failed.\",\"StackTrace\":null,\"ExceptionType\":\"System.InvalidOperationException\"}";
		var options = new UploadLicensesOptions();
		var ex = Assert.Throws<LicenseInstallationException>(() =>
			_command.TestProceedResponse(response, options));
		Assert.That(ex.Message, Is.EqualTo("License not installed: Authentication failed."));
	}
}

public class UploadLicensesCommandTestable : UploadLicensesCommand
{
	public UploadLicensesCommandTestable(IApplicationClient applicationClient, EnvironmentSettings settings)
		: base(applicationClient, settings) {
	}

	public void TestProceedResponse(string response, UploadLicensesOptions options) {
		ProceedResponse(response, options);
	}
}