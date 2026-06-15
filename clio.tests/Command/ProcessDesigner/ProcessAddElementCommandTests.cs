using System;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.ProcessDesigner;
using Clio.Command.ProcessModel;
using Clio.Common;
using Clio.Common.BrowserSession;
using Clio.Common.ProcessDesigner;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessDesigner;

[TestFixture]
[Property("Module", "Command")]
public sealed class ProcessAddElementCommandTests : BaseCommandTests<ProcessAddElementOptions> {
	private IProcessGraphValidator _validator;
	private IBrowserSessionService _sessionService;
	private IAuthenticatedBrowserLauncher _launcher;
	private IProcessDesignerDriver _driver;
	private ISettingsRepository _settingsRepository;
	private ILogger _logger;
	private ProcessAddElementRequest _capturedRequest;
	private ProcessAddElementCommand _command;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_validator = Substitute.For<IProcessGraphValidator>();
		_sessionService = Substitute.For<IBrowserSessionService>();
		_launcher = Substitute.For<IAuthenticatedBrowserLauncher>();
		_driver = Substitute.For<IProcessDesignerDriver>();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_validator);
		containerBuilder.AddSingleton(_sessionService);
		containerBuilder.AddSingleton(_launcher);
		containerBuilder.AddSingleton(_driver);
		containerBuilder.AddSingleton(_settingsRepository);
		containerBuilder.AddSingleton(_logger);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		_capturedRequest = null;
		// Happy-path defaults; individual tests override the relevant collaborator.
		_settingsRepository.GetEnvironment(Arg.Any<EnvironmentOptions>())
			.Returns(new EnvironmentSettings { Uri = "https://dev.creatio.com" });
		_validator.Validate(Arg.Any<ProcessGraph>()).Returns(new ProcessGraphValidationResult(false, []));
		_sessionService.GetSessionPathAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<string>(), Arg.Any<bool>(),
			Arg.Any<CancellationToken>()).Returns(Task.FromResult("/tmp/session.storageState.json"));
		_launcher.LaunchAndKeepOpenAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(new LaunchResult(9222)));
		_driver.AddReadDataElementAsync(Arg.Any<ProcessAddElementRequest>(), Arg.Any<CancellationToken>())
			.Returns(callInfo => {
				_capturedRequest = callInfo.Arg<ProcessAddElementRequest>();
				return Task.FromResult(new ProcessAddElementResult(true, "UsrProcess_x", "uid",
					_capturedRequest.Caption, null));
			});
		_command = Container.GetRequiredService<ProcessAddElementCommand>();
	}

	[TearDown]
	public override void TearDown() {
		_validator.ClearReceivedCalls();
		_sessionService.ClearReceivedCalls();
		_launcher.ClearReceivedCalls();
		_driver.ClearReceivedCalls();
		_settingsRepository.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	private static ProcessAddElementOptions Options(string elementType = "read-data", string readObject = "Contact",
		string caption = null, string processId = null) =>
		new() { Environment = "dev", ElementType = elementType, ReadObject = readObject, ProcessCaption = caption, ProcessId = processId };

	[Test]
	[Category("Unit")]
	[Description("Rejects an unsupported element-type before validating or opening a browser (slice supports only read-data).")]
	public void Execute_ShouldErrorBeforeValidating_WhenElementTypeUnsupported() {
		// Act
		int result = _command.Execute(Options(elementType: "user-task"));

		// Assert
		result.Should().Be(1, because: "this slice supports only read-data");
		_logger.Received(1).WriteError(Arg.Is<string>(v => v.StartsWith("Error:", StringComparison.Ordinal)));
		_validator.DidNotReceive().Validate(Arg.Any<ProcessGraph>());
	}

	[Test]
	[Category("Unit")]
	[Description("Aborts before opening a browser when the planned graph fails validation.")]
	public void Execute_ShouldAbortBeforeBrowser_WhenPlannedGraphInvalid() {
		// Arrange
		_validator.Validate(Arg.Any<ProcessGraph>()).Returns(new ProcessGraphValidationResult(
			true, [new ProcessGraphFinding(ProcessGraphSeverity.Error, "R1", "start has an incoming flow")]));

		// Act
		int result = _command.Execute(Options());

		// Assert
		result.Should().Be(1, because: "an invalid planned graph must abort before any browser is opened");
		_sessionService.DidNotReceive().GetSessionPathAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<string>(),
			Arg.Any<bool>(), Arg.Any<CancellationToken>());
		_launcher.DidNotReceiveWithAnyArgs().LaunchAndKeepOpenAsync(default, default, default);
	}

	[Test]
	[Category("Unit")]
	[Description("Reports a forms-auth error and does not launch a browser when no session can be obtained.")]
	public void Execute_ShouldError_WhenNoFormsAuthSession() {
		// Arrange
		_sessionService.GetSessionPathAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<string>(), Arg.Any<bool>(),
			Arg.Any<CancellationToken>()).Returns(Task.FromException<string>(
				new CreatioAuthenticationException("authentication failed")));

		// Act
		int result = _command.Execute(Options());

		// Assert
		result.Should().Be(1, because: "a missing forms-auth session blocks driving the designer");
		_logger.Received(1).WriteError(Arg.Is<string>(v => v.Contains("forms-auth browser session")));
		_launcher.DidNotReceiveWithAnyArgs().LaunchAndKeepOpenAsync(default, default, default);
	}

	[Test]
	[Category("Unit")]
	[Description("Reports a Chromium-not-found error and does not drive the designer when the browser is missing.")]
	public void Execute_ShouldError_WhenChromiumNotFound() {
		// Arrange
		_launcher.LaunchAndKeepOpenAsync(Arg.Any<EnvironmentSettings>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromException<LaunchResult>(new ChromiumNotFoundException("Chromium not found. Install a browser or set CHROME_PATH.")));

		// Act
		int result = _command.Execute(Options());

		// Assert
		result.Should().Be(1, because: "no Chromium means the designer cannot be driven");
		_logger.Received(1).WriteError(Arg.Is<string>(v => v.Contains("Chromium not found")));
		_driver.DidNotReceiveWithAnyArgs().AddReadDataElementAsync(default, default);
	}

	[Test]
	[Category("Unit")]
	[Description("Reports the driver's failure (e.g. invalid connection) and never claims a successful save.")]
	public void Execute_ShouldError_WhenDriverReportsFailure() {
		// Arrange
		_driver.AddReadDataElementAsync(Arg.Any<ProcessAddElementRequest>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(new ProcessAddElementResult(false, null, null, "cap",
				"Error: the appended connection is invalid (.djs-validate-outline).")));

		// Act
		int result = _command.Execute(Options());

		// Assert
		result.Should().Be(1, because: "a driver failure must not be reported as success");
		_logger.Received(1).WriteError(Arg.Is<string>(v => v.Contains("invalid")));
	}

	[Test]
	[Category("Unit")]
	[Description("On success it auto-generates a caption when omitted and returns the saved identity JSON.")]
	public void Execute_ShouldSucceedAndAutoGenerateCaption_WhenCaptionOmitted() {
		// Act
		int result = _command.Execute(Options(caption: null));

		// Assert
		result.Should().Be(0, because: "a successful drive returns zero");
		_capturedRequest.Should().NotBeNull(because: "the driver must be invoked on the happy path");
		_capturedRequest!.Caption.Should().StartWith("clio-pae-",
			because: "an omitted caption is auto-generated as a deterministic readback handle");
		_logger.Received(1).WriteInfo(Arg.Is<string>(json => json.Contains("UsrProcess_x") && json.Contains("\"success\": true")));
	}

	[Test]
	[Category("Unit")]
	[Description("A supplied caption is forwarded verbatim to the driver.")]
	public void Execute_ShouldForwardProvidedCaption_WhenCaptionGiven() {
		// Act
		int result = _command.Execute(Options(caption: "My deterministic caption"));

		// Assert
		result.Should().Be(0, because: "a successful drive returns zero");
		_capturedRequest!.Caption.Should().Be("My deterministic caption",
			because: "a provided caption must be used as-is");
	}
}
