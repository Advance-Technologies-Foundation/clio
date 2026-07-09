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
		int result = _command.Execute(new LoadPackagesToFileSystemOptions());

		result.Should().Be(0);
		_fileDesignModePackages.Received(1).LoadPackagesToFileSystem();
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
