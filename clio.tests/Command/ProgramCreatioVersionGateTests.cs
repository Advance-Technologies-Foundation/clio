using System;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ProgramCreatioVersionGateTests {

	private ICreatioVersionChecker _checker;

	[SetUp]
	public void SetUp() {
		_checker = Substitute.For<ICreatioVersionChecker>();
	}

	private sealed class SomeOptions { }

	[Test]
	[Description("TryGetCreatioVersionRequirementError refuses dispatch and surfaces the stable ErrorCode (version-too-old) in the message when the environment runs an older core version.")]
	public void TryGetCreatioVersionRequirementError_ShouldReturnTrueWithErrorCode_WhenVersionTooOld() {
		// Arrange
		SomeOptions options = new();
		_checker
			.When(c => c.EnsureRequirements(options))
			.Do(_ => throw new CreatioVersionRequirementException(
				"This command requires Creatio 10.0.0 or later. The target environment runs 8.1.5.",
				CreatioVersionRequirementException.VersionTooOldCode));

		// Act
		bool blocked = Clio.Program.TryGetCreatioVersionRequirementError(options, _checker, out string errorMessage);

		// Assert
		blocked.Should().BeTrue(
			because: "an unmet version requirement must refuse dispatch at the chokepoint, mapped to the distinct version exit code");
		errorMessage.Should().Contain("This command requires Creatio 10.0.0 or later.",
			because: "the user-facing message must explain the unmet requirement");
		errorMessage.Should().Contain(CreatioVersionRequirementException.VersionTooOldCode,
			because: "the stable machine-readable ErrorCode must be embedded so automation can branch without parsing the human message");
	}

	[Test]
	[Description("TryGetCreatioVersionRequirementError refuses dispatch and surfaces the version-undeterminable ErrorCode when the environment's core version could not be determined (fail-closed).")]
	public void TryGetCreatioVersionRequirementError_ShouldReturnTrueWithErrorCode_WhenVersionUndeterminable() {
		// Arrange
		SomeOptions options = new();
		_checker
			.When(c => c.EnsureRequirements(options))
			.Do(_ => throw new CreatioVersionRequirementException(
				"Could not determine the Creatio platform version of the target environment.",
				CreatioVersionRequirementException.VersionUndeterminableCode));

		// Act
		bool blocked = Clio.Program.TryGetCreatioVersionRequirementError(options, _checker, out string errorMessage);

		// Assert
		blocked.Should().BeTrue(
			because: "an undeterminable version must fail closed and refuse dispatch rather than run against an unknown platform");
		errorMessage.Should().Contain(CreatioVersionRequirementException.VersionUndeterminableCode,
			because: "the version-undeterminable ErrorCode must be surfaced so the failure class is machine-distinguishable from version-too-old");
	}

	[Test]
	[Description("CreatioVersionRequirementExitCode is distinct from the generic failure code 1 so callers can branch on a version-gate refusal specifically.")]
	public void CreatioVersionRequirementExitCode_ShouldBeDistinctFromGenericFailureCode() {
		// Arrange / Act
		int exitCode = Clio.Program.CreatioVersionRequirementExitCode;

		// Assert
		exitCode.Should().NotBe(0,
			because: "a version-gate refusal is a failure, not success");
		exitCode.Should().NotBe(1,
			because: "the version-gate refusal must use a DISTINCT, stable exit code, not the generic failure code used elsewhere");
	}

	[Test]
	[Description("TryGetCreatioVersionRequirementError does NOT catch InvalidOperationException — a malformed [RequiresCreatioVersion] declaration is a developer error that must surface as a normal error, not be mapped to the version exit code.")]
	public void TryGetCreatioVersionRequirementError_ShouldNotCatch_WhenCheckerThrowsInvalidOperationException() {
		// Arrange
		SomeOptions options = new();
		_checker
			.When(c => c.EnsureRequirements(options))
			.Do(_ => throw new InvalidOperationException("[RequiresCreatioVersion] declares an invalid minimum version '10.x'."));

		// Act
		Action act = () => Clio.Program.TryGetCreatioVersionRequirementError(options, _checker, out _);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "a malformed attribute is a developer error; it must propagate as a normal error and NOT be conflated with a version-gate refusal");
	}

	[Test]
	[Description("TryGetCreatioVersionRequirementError allows dispatch when the checker reports all requirements satisfied (e.g. only the legacy version source answered with a compatible version).")]
	public void TryGetCreatioVersionRequirementError_ShouldReturnFalse_WhenRequirementsSatisfied() {
		// Arrange
		SomeOptions options = new();

		// Act
		bool blocked = Clio.Program.TryGetCreatioVersionRequirementError(options, _checker, out string errorMessage);

		// Assert
		blocked.Should().BeFalse(
			because: "a satisfied version requirement must let the command dispatch normally");
		errorMessage.Should().BeNull(
			because: "no error message is produced when dispatch is permitted");
		_checker.Received(1).EnsureRequirements(options);
	}

	[Test]
	[Description("TryGetCreatioVersionRequirementError allows dispatch and does not throw when the checker is null.")]
	public void TryGetCreatioVersionRequirementError_ShouldReturnFalse_WhenCheckerIsNull() {
		// Arrange
		SomeOptions options = new();

		// Act
		bool blocked = Clio.Program.TryGetCreatioVersionRequirementError(options, checker: null, out string errorMessage);

		// Assert
		blocked.Should().BeFalse(
			because: "a missing checker must fail open at dispatch rather than block every command");
		errorMessage.Should().BeNull(
			because: "no error message is produced when there is no checker to consult");
	}
}
