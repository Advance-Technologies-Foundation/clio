using System.Threading;
using Clio.Common;
using Clio.Common.Responses;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Package;

/// <summary>
/// Covers the failure reporting of the file-system-mode package loader: every branch that does not load
/// the packages must be observable by the caller, because both <c>pkg-to-db</c> and
/// <c>pkg-to-file-system</c> derive their exit code from the returned value.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public class FileDesignModePackagesTests {

	#region Constants: Private

	private const string GetIsFileDesignModeUrl = "/ServiceModel/WorkspaceExplorerService.svc/GetIsFileDesignMode";
	private const string LoadPackagesToDbUrl = "/ServiceModel/AppInstallerService.svc/LoadPackagesToDB";
	private const string LoadPackagesToFileSystemUrl = "/ServiceModel/AppInstallerService.svc/LoadPackagesToFileSystem";

	#endregion

	#region Fields: Private

	private IApplicationClient _applicationClient;
	private IJsonConverter _jsonConverter;
	private ILogger _logger;
	private FileDesignModeFileDesignModePackages _sut;

	#endregion

	#region Methods: Private

	private void ArrangeFileDesignModeProbe(bool success, bool value) {
		_applicationClient
			.ExecutePostRequest(GetIsFileDesignModeUrl, string.Empty, Timeout.Infinite, Arg.Any<int>(), Arg.Any<int>())
			.Returns("is-file-design-mode-response");
		_jsonConverter.DeserializeObject<BoolResponse>("is-file-design-mode-response")
			.Returns(new BoolResponse {
				Success = success,
				Value = value,
				ErrorInfo = success
					? null
					: new ErrorInfo {
						Message = "probe refused",
						ErrorCode = "ProbeError"
					}
			});
	}

	private void ArrangeLoadResponse(string endpointUrl, bool success) {
		_applicationClient
			.ExecutePostRequest(endpointUrl, string.Empty, Timeout.Infinite, Arg.Any<int>(), Arg.Any<int>())
			.Returns("load-response");
		_jsonConverter.DeserializeObject<BaseResponse>("load-response")
			.Returns(new BaseResponse {
				Success = success,
				ErrorInfo = success
					? null
					: new ErrorInfo {
						Message = "platform refused the load",
						ErrorCode = "LoadError"
					}
			});
	}

	#endregion

	#region Methods: Public

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_jsonConverter = Substitute.For<IJsonConverter>();
		_logger = Substitute.For<ILogger>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		serviceUrlBuilder.Build(Arg.Any<string>()).Returns(callInfo => callInfo.Arg<string>());
		_sut = new FileDesignModeFileDesignModePackages(_applicationClient, _jsonConverter, _logger, serviceUrlBuilder);
	}

	[TearDown]
	public void TearDown() {
		_applicationClient.ClearReceivedCalls();
		_jsonConverter.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("LoadPackagesToDb reports failure and skips the load request when file design mode is disabled")]
	public void LoadPackagesToDb_ShouldReportFailure_WhenFileDesignModeIsDisabled() {
		// Arrange
		ArrangeFileDesignModeProbe(success: true, value: false);

		// Act
		bool result = _sut.LoadPackagesToDb();

		// Assert
		result.Should().BeFalse(
			because: "an environment with disabled file design mode cannot load packages, and the caller must " +
			"be able to turn that into a non-zero exit code");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("disabled file design mode")));
		_applicationClient.DidNotReceive().ExecutePostRequest(LoadPackagesToDbUrl, Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("LoadPackagesToDb reports failure when the platform rejects the load request")]
	public void LoadPackagesToDb_ShouldReportFailure_WhenPlatformRejectsTheLoad() {
		// Arrange
		ArrangeFileDesignModeProbe(success: true, value: true);
		ArrangeLoadResponse(LoadPackagesToDbUrl, success: false);

		// Act
		bool result = _sut.LoadPackagesToDb();

		// Assert
		result.Should().BeFalse(
			because: "a load the platform refused did not happen and must not be reported as a completed load");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("platform refused the load")));
	}

	[Test]
	[Description("LoadPackagesToDb reports failure and does not claim disabled file design mode when the mode probe itself fails")]
	public void LoadPackagesToDb_ShouldReportFailure_WhenFileDesignModeProbeFails() {
		// Arrange
		ArrangeFileDesignModeProbe(success: false, value: false);

		// Act
		bool result = _sut.LoadPackagesToDb();

		// Assert
		result.Should().BeFalse(
			because: "an unreadable file design mode state leaves the load unperformed and must reach the caller " +
			"as a failure");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("Get file design mode ended with error") && message.Contains("probe refused")));
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("file design mode state is unknown")));
		_logger.DidNotReceive().WriteError(Arg.Is<string>(message => message.Contains("disabled file design mode")));
		_applicationClient.DidNotReceive().ExecutePostRequest(LoadPackagesToDbUrl, Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("LoadPackagesToDb reports success when file design mode is enabled and the platform completes the load")]
	public void LoadPackagesToDb_ShouldReportSuccess_WhenPlatformCompletesTheLoad() {
		// Arrange
		ArrangeFileDesignModeProbe(success: true, value: true);
		ArrangeLoadResponse(LoadPackagesToDbUrl, success: true);

		// Act
		bool result = _sut.LoadPackagesToDb();

		// Assert
		result.Should().BeTrue(because: "a completed load must be reported as a success");
		_logger.DidNotReceive().WriteError(Arg.Any<string>());
	}

	[Test]
	[Description("LoadPackagesToFileSystem reports failure when file design mode is disabled")]
	public void LoadPackagesToFileSystem_ShouldReportFailure_WhenFileDesignModeIsDisabled() {
		// Arrange
		ArrangeFileDesignModeProbe(success: true, value: false);

		// Act
		bool result = _sut.LoadPackagesToFileSystem();

		// Assert
		result.Should().BeFalse(
			because: "the file system export shares the failure reporting of the database import");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("file system") && message.Contains("disabled file design mode")));
		_applicationClient.DidNotReceive().ExecutePostRequest(LoadPackagesToFileSystemUrl, Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Description("LoadPackagesToFileSystem reports success when file design mode is enabled and the platform completes the export")]
	public void LoadPackagesToFileSystem_ShouldReportSuccess_WhenPlatformCompletesTheExport() {
		// Arrange
		ArrangeFileDesignModeProbe(success: true, value: true);
		ArrangeLoadResponse(LoadPackagesToFileSystemUrl, success: true);

		// Act
		bool result = _sut.LoadPackagesToFileSystem();

		// Assert
		result.Should().BeTrue(because: "a completed export must be reported as a success");
		_logger.DidNotReceive().WriteError(Arg.Any<string>());
	}

	#endregion

}
