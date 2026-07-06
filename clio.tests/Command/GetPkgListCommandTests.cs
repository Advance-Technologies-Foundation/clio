using System;
using System.Net;
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
public class GetPkgListCommandTests {

	#region Fields: Private

	private IApplicationPackageListProvider _packageListProvider;
	private IJsonResponseFormater _jsonResponseFormater;
	private ILogger _logger;
	private GetPkgListCommand _command;

	#endregion

	#region Methods: Public

	[SetUp]
	public void Setup() {
		_packageListProvider = Substitute.For<IApplicationPackageListProvider>();
		_jsonResponseFormater = Substitute.For<IJsonResponseFormater>();
		_logger = Substitute.For<ILogger>();
		_command = new GetPkgListCommand(new EnvironmentSettings(), _packageListProvider, _jsonResponseFormater, _logger);
	}

	[Test]
	[Description("Execute should return 1 when the package provider throws an exception")]
	public void Execute_ShouldReturnOne_WhenProviderThrowsException() {
		_packageListProvider.GetPackages().Returns(_ => throw new Exception("Something failed"));

		int result = _command.Execute(new PkgListOptions());

		result.Should().Be(1);
	}

	[Test]
	[Description("Execute should use WriteError (not WriteInfo) when exception occurs — regression for the old WriteInfo(e.ToString()) behavior")]
	public void Execute_ShouldUseWriteError_NotWriteInfo_WhenExceptionOccurs() {
		bool originalDebugMode = Program.IsDebugMode;
		Program.IsDebugMode = false;
		try {
			_packageListProvider.GetPackages().Returns(_ => throw new Exception("error"));

			_command.Execute(new PkgListOptions());

			_logger.Received(1).WriteError(Arg.Any<string>());
			_logger.DidNotReceive().WriteInfo(Arg.Is<string>(s => s.Contains("Exception")));
		} finally {
			Program.IsDebugMode = originalDebugMode;
		}
	}

	[Test]
	[Description("Execute should log only the exception message without stack trace in normal mode")]
	public void Execute_ShouldLogMessageOnly_WhenExceptionOccurs_InNormalMode() {
		bool originalDebugMode = Program.IsDebugMode;
		Program.IsDebugMode = false;
		try {
			_packageListProvider.GetPackages().Returns(_ => throw new Exception("Test error message"));

			_command.Execute(new PkgListOptions());

			_logger.Received(1).WriteError("Test error message");
			_logger.DidNotReceive().WriteError(Arg.Is<string>(s => s.Contains("   at ")));
		} finally {
			Program.IsDebugMode = originalDebugMode;
		}
	}

	[Test]
	[Description("Execute should log the full stack trace when exception occurs in debug mode")]
	public void Execute_ShouldLogFullStackTrace_WhenExceptionOccurs_InDebugMode() {
		bool originalDebugMode = Program.IsDebugMode;
		Program.IsDebugMode = true;
		try {
			_packageListProvider.GetPackages().Returns(_ => throw new Exception("Debug error"));

			_command.Execute(new PkgListOptions());

			_logger.Received(1).WriteError(Arg.Is<string>(s => s.Contains("   at ")));
		} finally {
			Program.IsDebugMode = originalDebugMode;
		}
	}

	[Test]
	[Description("Execute should log a user-friendly 'Cannot connect' message when the site is unreachable (connection refused)")]
	public void Execute_ShouldLogFriendlyConnectMessage_WhenConnectionRefused() {
		bool originalDebugMode = Program.IsDebugMode;
		Program.IsDebugMode = false;
		try {
			var exception = new WebException("Connection refused (localhost:1616)", WebExceptionStatus.ConnectFailure);
			_packageListProvider.GetPackages().Returns(_ => throw exception);

			_command.Execute(new PkgListOptions());

			_logger.Received(1).WriteError(Arg.Is<string>(s =>
				s.StartsWith("Cannot connect to the application:") &&
				s.Contains("Make sure the site is running")));
		} finally {
			Program.IsDebugMode = originalDebugMode;
		}
	}

	#endregion

}
