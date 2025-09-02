using System;
using System.Text.Json;
using Autofac;
using Clio.Command;
using Clio.Command.StartProcess;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Description("Tests for TideInstallClioCommand that installs clio to the T.I.D.E. environment")]
public class TideInstallClioCommandTestCase : BaseCommandTests<TideInstallClioCommandOptions>
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
    [Description("Should send correct process request to run AtfProcess_TryInstallClio")]
    public void GetRequestData_ShouldReturnCorrectProcessArgs_WithAtfProcessTryInstallClioSchemaName()
    {
        // Arrange
        var command = new TideInstallClioCommandTestable();
        var options = new TideInstallClioCommandOptions();

        // Act
        var requestData = command.GetRequestData(options);

        // Assert
        var processArgs = JsonSerializer.Deserialize<ProcessStartArgs>(requestData);
        processArgs.Should().NotBeNull("because request data should be valid JSON");
        processArgs.SchemaName.Should().Be("AtfProcess_TryInstallClio", "because it should call the correct process");
    }

    [Test]
    [Description("Should log success message when process response indicates success")]
    public void ProceedResponse_ShouldLogSuccessMessage_WhenProcessResponseIsSuccessful()
    {
        // Arrange
        var command = new TideInstallClioCommandTestable();
        command.Logger = _loggerMock;
        var options = new TideInstallClioCommandOptions();
        var successResponse = JsonSerializer.Serialize(new ProcessStartResponse
        {
            ProcessId = Guid.NewGuid(),
            Success = true
        });

        // Act
        command.ProceedResponse(successResponse, options);

        // Assert
        _loggerMock.Received(1).WriteInfo(Arg.Is<string>(msg => msg.Contains("Process started with ID")));
        _loggerMock.Received(1).WriteInfo("Clio installation process completed successfully");
    }

    [Test]
    [Description("Should log error message when process response indicates failure")]
    public void ProceedResponse_ShouldLogErrorMessage_WhenProcessResponseFails()
    {
        // Arrange
        var command = new TideInstallClioCommandTestable();
        command.Logger = _loggerMock;
        var options = new TideInstallClioCommandOptions();
        var errorInfo = "Installation failed due to missing dependencies";
        var failureResponse = JsonSerializer.Serialize(new ProcessStartResponse
        {
            ProcessId = Guid.NewGuid(),
            Success = false,
            ErrorInfo = errorInfo
        });

        // Act
        command.ProceedResponse(failureResponse, options);

        // Assert
        _loggerMock.Received(1).WriteInfo(Arg.Is<string>(msg => msg.Contains("Process started with ID")));
        _loggerMock.Received(1).WriteError($"Clio installation process failed: {errorInfo}");
    }

    [Test]
    [Description("Should handle null or empty response gracefully")]
    public void ProceedResponse_ShouldHandleNullResponse_Gracefully()
    {
        // Arrange
        var command = new TideInstallClioCommandTestable();
        command.Logger = _loggerMock;
        var options = new TideInstallClioCommandOptions();

        // Act & Assert
        Assert.DoesNotThrow(() => command.ProceedResponse(null, options),
            "because command should handle null response without throwing");
        Assert.DoesNotThrow(() => command.ProceedResponse("", options),
            "because command should handle empty response without throwing");
    }

    [Test]
    [Description("Should use correct service path for ProcessEngineService")]
    public void ServicePath_ShouldBeCorrect_ForProcessEngineService()
    {
        // Arrange & Act
        var command = new TideInstallClioCommandTestable();

        // Assert
        command.ServicePath.Should().Be("/ServiceModel/ProcessEngineService.svc/RunProcess",
            "because it should use the correct service path for running processes");
    }

    [Test]
    [Description("Should handle invalid JSON response gracefully")]
    public void ProceedResponse_ShouldHandleInvalidJson_Gracefully()
    {
        // Arrange
        var command = new TideInstallClioCommandTestable();
        command.Logger = _loggerMock;
        var options = new TideInstallClioCommandOptions();
        var invalidJsonResponse = "{ invalid json }";

        // Act & Assert
        Assert.DoesNotThrow(() => command.ProceedResponse(invalidJsonResponse, options),
            "because command should handle invalid JSON without throwing");
        _loggerMock.Received(1).WriteError("Invalid response received from server");
    }

    #endregion
}

// Testable version of TideInstallClioCommand to access protected methods
public class TideInstallClioCommandTestable : TideInstallClioCommand
{
    public TideInstallClioCommandTestable(IApplicationClient applicationClient, EnvironmentSettings environmentSettings)
        : base(applicationClient, environmentSettings)
    {
    }

    public TideInstallClioCommandTestable() : base()
    {
    }

    public new string GetRequestData(TideInstallClioCommandOptions options)
    {
        return base.GetRequestData(options);
    }

    public new void ProceedResponse(string response, TideInstallClioCommandOptions options)
    {
        base.ProceedResponse(response, options);
    }

    public new string ServicePath
    {
        get => base.ServicePath;
        set => base.ServicePath = value;
    }
}