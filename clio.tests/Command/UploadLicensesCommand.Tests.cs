using Clio.Command;
using Clio.Command.PackageCommand;
using Clio.Common;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public class UploadLicensesCommandTestCase
{
    [SetUp]
    public void SetUp() =>
        _command = new UploadLicensesCommandTestable(
            Substitute.For<IApplicationClient>(),
            Substitute.For<EnvironmentSettings>());

    private UploadLicensesCommandTestable _command;

    [Test]
    public void TestProceedResponse_SuccessResponse_DoesNotThrow()
    {
        string response = "{\"success\": true}";
        UploadLicensesOptions options = new();
        Assert.DoesNotThrow(() => _command.TestProceedResponse(response, options));
    }

    [Test]
    public void TestProceedResponse_ErrorResponseWithErrorInfo_ThrowsException()
    {
        string response = @"
				{
					""success"": false,
					""errorInfo"": {
						""message"": ""Invalid license key"",
						""errorCode"": ""INVALID_KEY""
					}
				}";
        UploadLicensesOptions options = new();
        LicenseInstallationException? ex = Assert.Throws<LicenseInstallationException>(() =>
            _command.TestProceedResponse(response, options));
        Assert.That(
            ex.Message,
            Is.EqualTo("License not installed. ErrorCode: INVALID_KEY, Message: Invalid license key"));
    }

    [Test]
    public void TestProceedResponse_ErrorResponseWithoutErrorInfo_ThrowsException()
    {
        string response = "{\"success\": false}";
        UploadLicensesOptions options = new();
        LicenseInstallationException? ex = Assert.Throws<LicenseInstallationException>(() =>
            _command.TestProceedResponse(response, options));
        Assert.That(ex.Message, Is.EqualTo("License not installed: Unknown error details"));
    }

    [Test]
    public void TestProceedResponse_ResponseWithoutSuccessProperty_DoesNotThrow()
    {
        string response = "{\"errorInfo\": { \"message\": \"Error occurred\" }}";
        UploadLicensesOptions options = new();
        Assert.DoesNotThrow(() => _command.TestProceedResponse(response, options));
    }

    [Test]
    public void TestProceedResponse_AuthenticationFailed_ThrowsLicenseInstallationException()
    {
        string response =
            "{\"Message\":\"Authentication failed.\",\"StackTrace\":null,\"ExceptionType\":\"System.InvalidOperationException\"}";
        UploadLicensesOptions options = new();
        LicenseInstallationException? ex = Assert.Throws<LicenseInstallationException>(() =>
            _command.TestProceedResponse(response, options));
        Assert.That(ex.Message, Is.EqualTo("License not installed: Authentication failed."));
    }
}

public class UploadLicensesCommandTestable(IApplicationClient applicationClient, EnvironmentSettings settings)
    : UploadLicensesCommand(applicationClient, settings)
{
    public void TestProceedResponse(string response, UploadLicensesOptions options) =>
        ProceedResponse(response, options);
}
