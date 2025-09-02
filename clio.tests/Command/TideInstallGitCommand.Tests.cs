using System;
using System.Text.Json;
using System.Net.Http;
using Autofac;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Description("Tests for TideInstallGitCommand that installs Git to the T.I.D.E. environment")]
public class TideInstallGitCommandTestCase : BaseCommandTests<TideInstallGitCommandOptions>
{
    #region Fields: Private

    private readonly IApplicationClient _applicationClientMock = Substitute.For<IApplicationClient>();
    private readonly ILogger _loggerMock = Substitute.For<ILogger>();

    #endregion

    #region Methods: Protected

    protected override void AdditionalRegistrations(ContainerBuilder containerBuilder)
    {
        containerBuilder.RegisterInstance(_applicationClientMock).As<IApplicationClient>();
        containerBuilder.RegisterInstance(_loggerMock).As<ILogger>();
        base.AdditionalRegistrations(containerBuilder);
    }

    #endregion

    #region Test Methods

    [Test]
    [Description("Should use HTTP GET method for the request")]
    public void HttpMethod_ShouldBeGet()
    {
        // Arrange & Act
        var command = new TideInstallGitCommandTestable();

        // Assert
        command.HttpMethod.Should().Be(HttpMethod.Get, "because the command should use GET method to call the T.I.D.E. service");
    }

    [Test]
    [Description("Should return empty JSON object as request data")]
    public void GetRequestData_ShouldReturnEmptyJsonObject()
    {
        // Arrange
        var command = new TideInstallGitCommandTestable();
        var options = new TideInstallGitCommandOptions();

        // Act
        var requestData = command.GetRequestData(options);

        // Assert
        requestData.Should().Be("{}", "because the command should send an empty JSON object");
    }

    [Test]
    [Description("Should log success message when response indicates success")]
    public void ProceedResponse_ShouldLogSuccessMessage_WhenResponseIsSuccessful()
    {
        // Arrange
        var command = new TideInstallGitCommandTestable();
        command.Logger = _loggerMock;
        var options = new TideInstallGitCommandOptions();
        var successResponse = JsonSerializer.Serialize(new { success = true });

        // Act
        command.ProceedResponse(successResponse, options);

        // Assert
        _loggerMock.Received(1).WriteInfo("Git installation process completed successfully");
    }

    [Test]
    [Description("Should log error message when response indicates failure")]
    public void ProceedResponse_ShouldLogErrorMessage_WhenResponseFails()
    {
        // Arrange
        var command = new TideInstallGitCommandTestable();
        command.Logger = _loggerMock;
        var options = new TideInstallGitCommandOptions();
        var errorMessage = "Git installation failed due to missing dependencies";
        var failureResponse = JsonSerializer.Serialize(new { success = false, errorMessage });

        // Act
        command.ProceedResponse(failureResponse, options);

        // Assert
        _loggerMock.Received(1).WriteError($"Git installation process failed: {errorMessage}");
    }

    [Test]
    [Description("Should log error message when response fails without error details")]
    public void ProceedResponse_ShouldLogGenericErrorMessage_WhenResponseFailsWithoutErrorDetails()
    {
        // Arrange
        var command = new TideInstallGitCommandTestable();
        command.Logger = _loggerMock;
        var options = new TideInstallGitCommandOptions();
        var failureResponse = JsonSerializer.Serialize(new { success = false });

        // Act
        command.ProceedResponse(failureResponse, options);

        // Assert
        _loggerMock.Received(1).WriteError("Git installation process failed");
    }

    [Test]
    [Description("Should handle null or empty response gracefully")]
    public void ProceedResponse_ShouldHandleNullResponse_Gracefully()
    {
        // Arrange
        var command = new TideInstallGitCommandTestable();
        command.Logger = _loggerMock;
        var options = new TideInstallGitCommandOptions();

        // Act
        command.ProceedResponse(null, options);

        // Assert
        _loggerMock.Received(1).WriteError("Empty response received from server");
    }

    [Test]
    [Description("Should handle empty response gracefully")]
    public void ProceedResponse_ShouldHandleEmptyResponse_Gracefully()
    {
        // Arrange
        var command = new TideInstallGitCommandTestable();
        command.Logger = _loggerMock;
        var options = new TideInstallGitCommandOptions();

        // Act
        command.ProceedResponse("", options);

        // Assert
        _loggerMock.Received(1).WriteError("Empty response received from server");
    }

    [Test]
    [Description("Should use correct service path for T.I.D.E. Git installation")]
    public void ServicePath_ShouldBeCorrect_ForTideInstallGit()
    {
        // Arrange & Act
        var command = new TideInstallGitCommandTestable();

        // Assert
        command.ServicePath.Should().Be("/rest/Tide/InstallConsoleGit",
            "because it should use the correct service path for installing Git in T.I.D.E.");
    }

    [Test]
    [Description("Should handle invalid JSON response gracefully")]
    public void ProceedResponse_ShouldHandleInvalidJson_Gracefully()
    {
        // Arrange
        var command = new TideInstallGitCommandTestable();
        command.Logger = _loggerMock;
        var options = new TideInstallGitCommandOptions();
        var invalidJsonResponse = "{ invalid json }";

        // Act
        command.ProceedResponse(invalidJsonResponse, options);

        // Assert
        _loggerMock.Received(1).WriteInfo("Git installation process completed");
    }

    [Test]
    [Description("Should handle general exceptions gracefully")]
    public void ProceedResponse_ShouldHandleExceptions_Gracefully()
    {
        // Arrange
        var command = new TideInstallGitCommandTestable();
        command.Logger = _loggerMock;
        var options = new TideInstallGitCommandOptions();
        var response = "valid response"; // This will cause an exception in JsonSerializer.Deserialize<JsonElement>

        // Act
        command.ProceedResponse(response, options);

        // Assert
        _loggerMock.Received(1).WriteInfo("Git installation process completed");
    }

    #endregion
}

// Testable version of TideInstallGitCommand to access protected methods
public class TideInstallGitCommandTestable : TideInstallGitCommand
{
    public TideInstallGitCommandTestable(IApplicationClient applicationClient, EnvironmentSettings environmentSettings)
        : base(applicationClient, environmentSettings)
    {
    }

    public TideInstallGitCommandTestable() : base()
    {
    }

    public new string GetRequestData(TideInstallGitCommandOptions options)
    {
        return base.GetRequestData(options);
    }

    public new void ProceedResponse(string response, TideInstallGitCommandOptions options)
    {
        base.ProceedResponse(response, options);
    }

    public new string ServicePath
    {
        get => base.ServicePath;
        set => base.ServicePath = value;
    }

    public new HttpMethod HttpMethod => base.HttpMethod;
}