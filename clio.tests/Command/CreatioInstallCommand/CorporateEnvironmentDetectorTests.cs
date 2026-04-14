using System.Threading.Tasks;
using Clio.Command.CreatioInstallCommand;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.CreatioInstallCommand;

[TestFixture]
[Category("Unit")]
[Property("Module", "CreatioInstallCommand")]
public class CorporateEnvironmentDetectorTests{
	[Test]
	[Description("Should return true when current Windows identity belongs to tscrm domain.")]
	public void IsCorporateEnvironment_ReturnsTrue_WhenUserIsInTscrmDomain() {
		// Arrange
		IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();
		IOperationSystem operationSystem = Substitute.For<IOperationSystem>();
		operationSystem.IsWindows.Returns(true);
		processExecutor
			.ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(options =>
				options.Program == "cmd" && options.Arguments.Contains("whoami")))
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 0,
				StandardOutput = "tscrm\\developer"
			}));
		ICorporateEnvironmentDetector detector = new CorporateEnvironmentDetector(processExecutor, operationSystem);

		// Act
		bool result = detector.IsCorporateEnvironment();

		// Assert
		result.Should().BeTrue("because tscrm domain membership is enough to enable corporate-only script execution");
		processExecutor.DidNotReceive()
			.ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(options => options.Program == "ping"));
	}

	[Test]
	[Description("Should return true when tscrm.com ping succeeds even if identity is not from tscrm domain.")]
	public void IsCorporateEnvironment_ReturnsTrue_WhenCorporateHostIsReachable() {
		// Arrange
		IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();
		IOperationSystem operationSystem = Substitute.For<IOperationSystem>();
		operationSystem.IsWindows.Returns(true);
		processExecutor
			.ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(options =>
				options.Program == "cmd" && options.Arguments.Contains("whoami")))
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 0,
				StandardOutput = "other\\developer"
			}));
		processExecutor
			.ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(options => options.Program == "ping"))
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 0
			}));
		ICorporateEnvironmentDetector detector = new CorporateEnvironmentDetector(processExecutor, operationSystem);

		// Act
		bool result = detector.IsCorporateEnvironment();

		// Assert
		result.Should().BeTrue("because network reachability to tscrm.com is a supported alternative condition");
		processExecutor.Received(1)
			.ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(options => options.Program == "ping"));
	}

	[Test]
	[Description("Should return false when not on Windows, and corporate host is unreachable.")]
	public void IsCorporateEnvironment_ReturnsFalse_WhenNoCorporateSignalsAreAvailable() {
		// Arrange
		IProcessExecutor processExecutor = Substitute.For<IProcessExecutor>();
		IOperationSystem operationSystem = Substitute.For<IOperationSystem>();
		operationSystem.IsWindows.Returns(false);
		processExecutor
			.ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(options => options.Program == "ping"))
			.Returns(Task.FromResult(new ProcessExecutionResult {
				Started = true,
				ExitCode = 1
			}));
		ICorporateEnvironmentDetector detector = new CorporateEnvironmentDetector(processExecutor, operationSystem);

		// Act
		bool result = detector.IsCorporateEnvironment();

		// Assert
		result.Should().BeFalse("because script execution must be blocked outside corporate environment");
		processExecutor.DidNotReceive()
			.ExecuteAndCaptureAsync(Arg.Is<ProcessExecutionOptions>(options =>
				options.Program == "cmd" && options.Arguments.Contains("whoami")));
	}
}
