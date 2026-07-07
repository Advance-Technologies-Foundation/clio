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
	[Description("Execute should emit the unified BL-1 envelope (via FormatEnvelope) when --json is set without --legacy-form")]
	public void Execute_ShouldEmitUnifiedEnvelope_WhenJsonWithoutLegacyForm() {
		// Arrange
		_packageListProvider.GetPackages().Returns(Array.Empty<PackageInfo>());
		_jsonResponseFormater.FormatEnvelope(Arg.Any<string>(), Arg.Any<System.Collections.Generic.IEnumerable<PackageInfo>>())
			.Returns("{\"schemaVersion\":\"1.0\"}");

		// Act
		int result = _command.Execute(new PkgListOptions { Json = true });

		// Assert
		result.Should().Be(0, because: "a successful listing returns exit code 0");
		_jsonResponseFormater.Received(1).FormatEnvelope("list-packages",
			Arg.Any<System.Collections.Generic.IEnumerable<PackageInfo>>());
		_jsonResponseFormater.DidNotReceive().Format(Arg.Any<System.Collections.Generic.IEnumerable<PackageInfo>>());
		_logger.Received(1).WriteLine("{\"schemaVersion\":\"1.0\"}");
	}

	[Test]
	[Description("Execute should emit the legacy {value,success,errorInfo} shape (via Format) when --json --legacy-form is set")]
	public void Execute_ShouldEmitLegacyShape_WhenJsonWithLegacyForm() {
		// Arrange
		_packageListProvider.GetPackages().Returns(Array.Empty<PackageInfo>());

		// Act
		int result = _command.Execute(new PkgListOptions { Json = true, LegacyForm = true });

		// Assert
		result.Should().Be(0, because: "a successful listing returns exit code 0 regardless of output shape");
		_jsonResponseFormater.Received(1).Format(Arg.Any<System.Collections.Generic.IEnumerable<PackageInfo>>());
		_jsonResponseFormater.DidNotReceive().FormatEnvelope(Arg.Any<string>(),
			Arg.Any<System.Collections.Generic.IEnumerable<PackageInfo>>());
	}

	[Test]
	[Description("Execute should emit an error envelope with a stable code (via FormatEnvelope) when --json is set and the provider throws")]
	public void Execute_ShouldEmitErrorEnvelope_WhenJsonAndProviderThrows() {
		// Arrange
		_packageListProvider.GetPackages().Returns(_ => throw new Exception("boom"));

		// Act
		int result = _command.Execute(new PkgListOptions { Json = true });

		// Assert
		result.Should().Be(1, because: "a failure returns a non-zero exit code");
		_jsonResponseFormater.Received(1).FormatEnvelope("list-packages",
			Clio.Common.CommandErrorCodes.UnexpectedError, Arg.Any<string>());
	}

	[Test]
	[Description("Execute should use the legacy error path (WriteInfo + Format(exception)) when --json --legacy-form is set and the provider throws")]
	public void Execute_ShouldUseLegacyErrorPath_WhenJsonLegacyFormAndProviderThrows() {
		// Arrange
		_packageListProvider.GetPackages().Returns(_ => throw new Exception("boom"));

		// Act
		int result = _command.Execute(new PkgListOptions { Json = true, LegacyForm = true });

		// Assert
		result.Should().Be(1, because: "a failure returns a non-zero exit code");
		_jsonResponseFormater.Received(1).Format(Arg.Any<Exception>());
		_jsonResponseFormater.DidNotReceive().FormatEnvelope(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Description("Execute should NOT emit any JSON (neither envelope nor legacy) when --json is not set — text output regression guard")]
	public void Execute_ShouldNotEmitJson_WhenJsonNotSet() {
		// Arrange
		_packageListProvider.GetPackages().Returns(Array.Empty<PackageInfo>());

		// Act
		int result = _command.Execute(new PkgListOptions());

		// Assert
		result.Should().Be(0, because: "listing succeeds");
		_jsonResponseFormater.DidNotReceive().FormatEnvelope(Arg.Any<string>(),
			Arg.Any<System.Collections.Generic.IEnumerable<PackageInfo>>());
		_jsonResponseFormater.DidNotReceive().Format(Arg.Any<System.Collections.Generic.IEnumerable<PackageInfo>>());
	}

	[Test]
	[Description("Execute non-JSON with packages should render the table (Name/Version/Maintainer) and the summary line, without any JSON — AC#3/C1 golden for the non-empty table render")]
	public void Execute_ShouldRenderTableAndSummary_WhenNonJsonWithPackages() {
		// Arrange
		var settings = new EnvironmentSettings { Uri = "https://example-host" };
		var command = new GetPkgListCommand(settings, _packageListProvider, _jsonResponseFormater, _logger);
		var package = new PackageInfo(
			new Clio.Package.PackageDescriptor { Name = "FooPkg", PackageVersion = "1.2.3", Maintainer = "Acme" },
			"path", new System.Collections.Generic.List<string>());
		_packageListProvider.GetPackages().Returns(new[] { package });

		// Act
		int result = command.Execute(new PkgListOptions());

		// Assert
		result.Should().Be(0, because: "listing succeeds");
		_logger.Received(1).WriteInfo(Arg.Is<string>(s =>
			s != null && s.Contains("FooPkg") && s.Contains("1.2.3") && s.Contains("Acme")));
		_logger.Received(1).WriteInfo("Find 1 packages in https://example-host");
		_logger.DidNotReceive().WriteLine(Arg.Is<string>(s => s != null && s.Contains("schemaVersion")));
		_jsonResponseFormater.DidNotReceive().FormatEnvelope(Arg.Any<string>(),
			Arg.Any<System.Collections.Generic.IEnumerable<PackageInfo>>());
	}

	[Test]
	[Description("Execute non-JSON should emit the exact 'Find N packages' summary line and no JSON — AC#3/C1 text-output regression guard for the changed json-branch")]
	public void Execute_ShouldEmitExactSummaryLine_WhenNonJson() {
		// Arrange
		var settings = new EnvironmentSettings { Uri = "https://example-host" };
		var command = new GetPkgListCommand(settings, _packageListProvider, _jsonResponseFormater, _logger);
		_packageListProvider.GetPackages().Returns(Array.Empty<PackageInfo>());

		// Act
		int result = command.Execute(new PkgListOptions());

		// Assert
		result.Should().Be(0, because: "listing succeeds");
		_logger.Received(1).WriteInfo("Find 0 packages in https://example-host");
		_logger.DidNotReceive().WriteLine(Arg.Is<string>(s => s != null && s.Contains("schemaVersion")));
		_jsonResponseFormater.DidNotReceive().FormatEnvelope(Arg.Any<string>(),
			Arg.Any<System.Collections.Generic.IEnumerable<PackageInfo>>());
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
