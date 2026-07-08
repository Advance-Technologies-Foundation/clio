using System;
using Clio.Command;
using Clio.Command.RelatedPages;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

// Plain fixture rather than BaseCommandTests<RegisterRelatedPageOptions>: register-related-page is an
// MCP-only command (no [Verb], not wired in Program.cs), so it is intentionally absent from README.md
// and the BaseCommandTests README check does not apply to it.
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class RegisterRelatedPageCommandTests {

	private IRelatedPageService _service;
	private ILogger _logger;
	private RegisterRelatedPageCommand _command;

	[SetUp]
	public void SetUp() {
		_service = Substitute.For<IRelatedPageService>();
		_logger = Substitute.For<ILogger>();
		_command = new RegisterRelatedPageCommand(_service, _logger);
	}

	[TearDown]
	public void TearDown() {
		_service.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	private static RegisterRelatedPageOptions ValidOptions() =>
		new() {
			Environment = "dev",
			PackageName = "UsrPackage",
			EntitySchemaName = "UsrEntity",
			PageSchemaName = "UsrEntity_FormPage",
			SchemaType = RelatedPageSchemaType.Mobile,
			IsDefault = true
		};

	[Test]
	[Description("A complete request registers the page through the service and returns the success exit code.")]
	public void Execute_ShouldRegisterAndReturnSuccess_WhenAllRequiredFieldsProvided() {
		// Arrange
		RegisterRelatedPageOptions options = ValidOptions();
		_service.Register(Arg.Any<RelatedPageRegistration>()).Returns(
			new RelatedPageResult("UsrEntity", "UsrEntity_FormPage", Guid.NewGuid(), "MobileRelatedPage", true));

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "a successful registration must return the success exit code");
		// the command must forward the parsed options to the related-page service unchanged
		_service.Received(1).Register(Arg.Is<RelatedPageRegistration>(r =>
			r.PackageName == "UsrPackage"
			&& r.EntitySchemaName == "UsrEntity"
			&& r.PageSchemaName == "UsrEntity_FormPage"
			&& r.SchemaType == RelatedPageSchemaType.Mobile
			&& r.IsDefault));
	}

	[Test]
	[Description("A missing environment name fails validation before any registration is attempted.")]
	public void Execute_ShouldReturnErrorExitCode_WhenEnvironmentIsMissing() {
		// Arrange
		RegisterRelatedPageOptions options = ValidOptions();
		options.Environment = string.Empty;

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1,
			because: "a request without an environment name is invalid and must not register anything");
		_service.DidNotReceive().Register(Arg.Any<RelatedPageRegistration>());
		// the user must be told which required field is missing
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("environment")));
	}

	[Test]
	[Description("A missing package name fails validation and reports the error.")]
	public void Execute_ShouldReturnErrorExitCode_WhenPackageNameIsMissing() {
		// Arrange
		RegisterRelatedPageOptions options = ValidOptions();
		options.PackageName = string.Empty;

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1,
			because: "a request without a package name is invalid");
		_service.DidNotReceive().Register(Arg.Any<RelatedPageRegistration>());
		// the error must name the missing package-name argument
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("package-name")));
	}

	[Test]
	[Description("A missing page schema name fails validation and reports the error.")]
	public void Execute_ShouldReturnErrorExitCode_WhenPageSchemaNameIsMissing() {
		// Arrange
		RegisterRelatedPageOptions options = ValidOptions();
		options.PageSchemaName = string.Empty;

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1,
			because: "a request without a page schema name is invalid");
		_service.DidNotReceive().Register(Arg.Any<RelatedPageRegistration>());
		// the error must name the missing page-schema-name argument
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("page-schema-name")));
	}

	[Test]
	[Description("A service failure is surfaced as an error exit code rather than an unhandled exception.")]
	public void Execute_ShouldReturnErrorExitCode_WhenServiceThrows() {
		// Arrange
		RegisterRelatedPageOptions options = ValidOptions();
		_service.Register(Arg.Any<RelatedPageRegistration>())
			.Returns(_ => throw new InvalidOperationException("page not found"));

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(1,
			because: "a failure during registration must be reported as a non-zero exit code");
		// the service failure message must reach the user
		_logger.Received().WriteError(Arg.Is<string>(m => m.Contains("page not found")));
	}
}
