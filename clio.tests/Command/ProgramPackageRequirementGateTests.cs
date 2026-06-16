using System;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ProgramPackageRequirementGateTests {

	private IRequiredPackageChecker _checker;

	[SetUp]
	public void SetUp() {
		_checker = Substitute.For<IRequiredPackageChecker>();
	}

	private sealed class SomeOptions { }

	[Test]
	[Description("TryGetPackageRequirementError refuses dispatch and yields the exception message when the checker reports an unmet requirement.")]
	public void TryGetPackageRequirementError_ShouldReturnTrueWithMessage_WhenRequirementUnmet() {
		// Arrange
		SomeOptions options = new();
		_checker
			.When(c => c.EnsureRequirements(typeof(SomeOptions)))
			.Do(_ => throw new PackageRequirementException("Install the cliogate package."));

		// Act
		bool blocked = Clio.Program.TryGetPackageRequirementError(options, _checker, out string errorMessage);

		// Assert
		blocked.Should().BeTrue(
			because: "an unmet package requirement must refuse dispatch at the chokepoint, which the caller maps to exit code 1");
		errorMessage.Should().Be("Install the cliogate package.",
			because: "the PackageRequirementException message is surfaced to the user via WriteError");
	}

	[Test]
	[Description("TryGetPackageRequirementError refuses dispatch with a readable message (instead of escaping) when the checker throws a non-PackageRequirementException, e.g. an unreachable environment.")]
	public void TryGetPackageRequirementError_ShouldReturnTrueWithReadableMessage_WhenCheckerThrowsNonPackageRequirementException() {
		// Arrange
		SomeOptions options = new();
		_checker
			.When(c => c.EnsureRequirements(typeof(SomeOptions)))
			.Do(_ => throw new InvalidOperationException("Unable to connect to the remote server."));

		// Act
		bool blocked = Clio.Program.TryGetPackageRequirementError(options, _checker, out string errorMessage);

		// Assert
		blocked.Should().BeTrue(
			because: "a verification/HTTP failure must become a clean exit-1 refusal rather than an uncaught exception escaping the CLI gate");
		errorMessage.Should().Contain("Unable to connect to the remote server.",
			because: "the readable form of the underlying failure must be surfaced to the user via WriteError");
	}

	[Test]
	[Description("TryGetPackageRequirementError allows dispatch when the checker reports all requirements satisfied.")]
	public void TryGetPackageRequirementError_ShouldReturnFalse_WhenRequirementsSatisfied() {
		// Arrange
		SomeOptions options = new();

		// Act
		bool blocked = Clio.Program.TryGetPackageRequirementError(options, _checker, out string errorMessage);

		// Assert
		blocked.Should().BeFalse(
			because: "a satisfied (or absent) package requirement must let the command dispatch normally");
		errorMessage.Should().BeNull(
			because: "no error message is produced when dispatch is permitted");
		_checker.Received(1).EnsureRequirements(typeof(SomeOptions));
	}

	[Test]
	[Description("TryGetPackageRequirementError allows dispatch and does not throw when the checker is null.")]
	public void TryGetPackageRequirementError_ShouldReturnFalse_WhenCheckerIsNull() {
		// Arrange
		SomeOptions options = new();

		// Act
		bool blocked = Clio.Program.TryGetPackageRequirementError(options, checker: null, out string errorMessage);

		// Assert
		blocked.Should().BeFalse(
			because: "a missing checker must fail open at dispatch rather than block every command");
		errorMessage.Should().BeNull(
			because: "no error message is produced when there is no checker to consult");
	}
}
