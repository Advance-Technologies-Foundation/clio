using System;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[NonParallelizable]
[Category("Unit")]
[Property("Module", "Command")]
public class LoadPackagesToFileSystemCommandTests {

	#region Fields: Private

	private IFileDesignModePackages _fileDesignModePackages;
	private ILogger _logger;
	private LoadPackagesToFileSystemCommand _command;

	#endregion

	#region Methods: Public

	[SetUp]
	public void Setup() {
		_fileDesignModePackages = Substitute.For<IFileDesignModePackages>();
		_logger = Substitute.For<ILogger>();
		_command = new LoadPackagesToFileSystemCommand(_fileDesignModePackages, _logger);
	}

	[Test]
	[Description("Execute should return 0 on success")]
	public void Execute_ShouldReturnZero_WhenLoadSucceeds() {
		// Arrange
		_fileDesignModePackages.LoadPackagesToFileSystem().Returns(FileDesignModeLoadResult.Completed);

		// Act
		int result = _command.Execute(new LoadPackagesToFileSystemOptions());

		// Assert
		result.Should().Be(0, because: "a completed load must report success to the caller");
		_fileDesignModePackages.Received(1).LoadPackagesToFileSystem();
	}

	[Test]
	[Description("Execute should return 1 when the loader reports a failed load instead of throwing")]
	public void Execute_ShouldReturnOne_WhenLoadReportsFailure() {
		// Arrange
		_fileDesignModePackages.LoadPackagesToFileSystem().Returns(FileDesignModeLoadResult.LoadRefused);

		// Act
		int result = _command.Execute(new LoadPackagesToFileSystemOptions());

		// Assert
		result.Should().Be(1,
			because: "a load refused by the environment (for example disabled file design mode) must not be " +
			"reported to the caller as exit code 0");
		_fileDesignModePackages.Received(1).LoadPackagesToFileSystem();
	}

	[Test]
	[Description("Execute reports a disabled file design mode as an error itself, because the loader stays silent on that cause for the turn-fsm off caller that treats it as its goal state.")]
	public void Execute_ShouldReportError_WhenFileDesignModeIsDisabled() {
		// Arrange
		_fileDesignModePackages.LoadPackagesToFileSystem().Returns(FileDesignModeLoadResult.FileDesignModeDisabled);

		// Act
		int result = _command.Execute(new LoadPackagesToFileSystemOptions());

		// Assert
		// The non-zero exit code and the Error log message are both published failure signals of the
		// command-execution-result contract, so they must agree.
		result.Should().Be(1,
			because: "a standalone LoadPackagesToFileSystem call over an environment with file design mode disabled loaded nothing");
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("file system") && message.Contains("disabled file design mode")));
	}

	[Test]
	[Description("Execute should return 1 when LoadPackagesToFileSystem throws")]
	public void Execute_ShouldReturnOne_WhenLoadThrowsException() {
		_fileDesignModePackages.When(x => x.LoadPackagesToFileSystem()).Do(_ => throw new Exception("fs failed"));

		int result = _command.Execute(new LoadPackagesToFileSystemOptions());

		result.Should().Be(1);
	}

	[Test]
	[Description("Execute should log only the message without stack trace in normal mode")]
	public void Execute_ShouldLogMessageOnly_WhenExceptionOccurs_InNormalMode() {
		bool originalDebugMode = Program.IsDebugMode;
		Program.IsDebugMode = false;
		try {
			_fileDesignModePackages.When(x => x.LoadPackagesToFileSystem()).Do(_ => throw new Exception("fs failed"));

			_command.Execute(new LoadPackagesToFileSystemOptions());

			_logger.Received(1).WriteError("fs failed");
			_logger.DidNotReceive().WriteError(Arg.Is<string>(s => s.Contains("   at ")));
		} finally {
			Program.IsDebugMode = originalDebugMode;
		}
	}

	[Test]
	[Description("Execute should log full stack trace in debug mode")]
	public void Execute_ShouldLogFullStackTrace_WhenExceptionOccurs_InDebugMode() {
		bool originalDebugMode = Program.IsDebugMode;
		Program.IsDebugMode = true;
		try {
			_fileDesignModePackages.When(x => x.LoadPackagesToFileSystem()).Do(_ => throw new Exception("fs failed"));

			_command.Execute(new LoadPackagesToFileSystemOptions());

			_logger.Received(1).WriteError(Arg.Is<string>(s => s.Contains("   at ")));
		} finally {
			Program.IsDebugMode = originalDebugMode;
		}
	}

	#endregion

}
