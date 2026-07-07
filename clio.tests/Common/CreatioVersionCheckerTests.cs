using System;
using Clio.Common;
using Clio.Tests.Command;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture(Category = "Unit")]
[Property("Module", "Common")]
public class CreatioVersionCheckerTests : BaseClioModuleTests
{

	#region Constants: Private

	private const string TestHint = "Update Creatio to version 10.0.0 or later.";

	#endregion

	#region Fields: Private

	private readonly ICreatioVersionProvider _creatioVersionProviderMock
		= Substitute.For<ICreatioVersionProvider>();

	#endregion

	#region Nested types: Test fixtures

	private sealed class NoRequirementOptions { }

	[RequiresCreatioVersion("10.0.0")]
	private sealed class ClassRequirementOptions { }

	[RequiresCreatioVersion("10.0.0", Hint = TestHint)]
	private sealed class ClassRequirementWithHintOptions { }

	// Misuse: a malformed minimum version is a developer error in the attribute declaration.
	[RequiresCreatioVersion("10.x")]
	private sealed class MalformedMinVersionOptions { }

	// Property-level requirement: enforced only when the bool flag is true.
	private sealed class PropertyRequirementOptions {
		[RequiresCreatioVersion("10.0.0")]
		public bool UseGatedPath { get; set; }
	}

	// Misuse: [RequiresCreatioVersion] on a non-bool property must fail fast.
	private sealed class NonBoolPropertyRequirementOptions {
		[RequiresCreatioVersion("10.0.0")]
		public string Target { get; set; }
	}

	// Two requirements: a class-level floor of 10.0.0 plus a property-level floor of 11.0.0 triggered by
	// the bool flag. The strictest (11.0.0) is the one that must be enforced and reported.
	[RequiresCreatioVersion("10.0.0")]
	private sealed class MultiRequirementOptions {
		[RequiresCreatioVersion("11.0.0")]
		public bool UseGatedPath { get; set; }
	}

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		// CreatioVersionChecker is intentionally not registered in production BindingsModule yet
		// (its concrete provider lands in a later increment); register it here so the SUT is resolved
		// from the container per the command/common test convention.
		containerBuilder.AddSingleton<ICreatioVersionProvider>(_creatioVersionProviderMock);
		containerBuilder.AddTransient<ICreatioVersionChecker, CreatioVersionChecker>();
		base.AdditionalRegistrations(containerBuilder);
	}

	#endregion

	#region Methods: Public

	[TearDown]
	public void ClearMockState() {
		_creatioVersionProviderMock.ClearReceivedCalls();
		_creatioVersionProviderMock.Resolve().Returns(CreatioVersionResolution.ReachableWithoutVersion());
	}

	// Builds a Resolved resolution carrying the given version, the shape the provider returns on success.
	private static CreatioVersionResolution Resolved(Version version) =>
		CreatioVersionResolution.Resolved(version);

	[Test]
	[Description("EnsureRequirements does not throw when the environment version is newer than the required minimum.")]
	public void EnsureRequirements_ShouldNotThrow_WhenCurrentVersionIsNewer() {
		// Arrange
		_creatioVersionProviderMock.Resolve().Returns(Resolved(new Version(10, 1, 0)));
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new ClassRequirementOptions());

		// Assert
		act.Should().NotThrow(because: "10.1.0 is greater than the required minimum 10.0.0");
	}

	[Test]
	[Description("EnsureRequirements does not throw when the environment version equals the required minimum.")]
	public void EnsureRequirements_ShouldNotThrow_WhenCurrentVersionIsEqual() {
		// Arrange
		_creatioVersionProviderMock.Resolve().Returns(Resolved(new Version(10, 0, 0)));
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new ClassRequirementOptions());

		// Assert
		act.Should().NotThrow(because: "an equal version satisfies a minimum-version requirement");
	}

	[Test]
	[Description("EnsureRequirements throws version-too-old when the environment version is below the required minimum.")]
	public void EnsureRequirements_ShouldThrowVersionTooOld_WhenCurrentVersionIsOlder() {
		// Arrange
		_creatioVersionProviderMock.Resolve().Returns(Resolved(new Version(9, 9, 0)));
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new ClassRequirementOptions());

		// Assert
		act.Should().Throw<CreatioVersionRequirementException>(because: "9.9.0 is lower than the required 10.0.0")
			.Which.ErrorCode.Should().Be(CreatioVersionRequirementException.VersionTooOldCode,
				because: "an older environment version must report the version-too-old error code");
	}

	[Test]
	[Description("EnsureRequirements does not throw for a development build (0.0.0.0) even when a minimum is required.")]
	public void EnsureRequirements_ShouldNotThrow_WhenCurrentVersionIsDevBuild() {
		// Arrange
		_creatioVersionProviderMock.Resolve().Returns(Resolved(new Version(0, 0, 0, 0)));
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new ClassRequirementOptions());

		// Assert
		act.Should().NotThrow(because: "a development build (0.0.0.0) is treated as compatible with any requirement");
	}

	[Test]
	[Description("EnsureRequirements throws version-undeterminable (fail-closed) when a source responded but produced no usable version (ReachableWithoutVersion).")]
	public void EnsureRequirements_ShouldThrowVersionUndeterminable_WhenReachableWithoutVersion() {
		// Arrange
		_creatioVersionProviderMock.Resolve().Returns(CreatioVersionResolution.ReachableWithoutVersion());
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new ClassRequirementOptions());

		// Assert
		act.Should().Throw<CreatioVersionRequirementException>(because: "a reachable environment with no usable version must fail closed")
			.Which.ErrorCode.Should().Be(CreatioVersionRequirementException.VersionUndeterminableCode,
				because: "a reachable-but-no-version environment must report the version-undeterminable error code");
	}

	[Test]
	[Description("EnsureRequirements throws version-check-failed (fail-closed) when no source responded at all (ProbeFailed); the message hints at connectivity/access, NOT updating Creatio.")]
	public void EnsureRequirements_ShouldThrowVersionCheckFailed_WhenProbeFailed() {
		// Arrange
		_creatioVersionProviderMock.Resolve().Returns(CreatioVersionResolution.ProbeFailed());
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new ClassRequirementOptions());

		// Assert
		CreatioVersionRequirementException exception = act.Should()
			.Throw<CreatioVersionRequirementException>(because: "an uncheckable environment must fail closed")
			.Which;
		exception.ErrorCode.Should().Be(CreatioVersionRequirementException.VersionCheckFailedCode,
			because: "when no source responded the gate must report the version-check-failed error code, distinct from version-too-old and version-undeterminable");
		exception.Message.Should().MatchEquivalentOf("*connect*",
			because: "the check-failed message must hint at connectivity/access, the actual failure class");
		exception.Message.Should().NotContainEquivalentOf("Update Creatio",
			because: "a connectivity/access failure must NOT advise the user to update Creatio (that is the version-too-old wording)");
	}

	[Test]
	[Description("EnsureRequirements does NOT append the attribute Hint to the version-check-failed message: the Hint ('how to satisfy the version requirement') is coherent only on the too-old branch, not on a connectivity/access failure.")]
	public void EnsureRequirements_ShouldNotAppendHint_WhenProbeFailedAndHintSet() {
		// Arrange
		_creatioVersionProviderMock.Resolve().Returns(CreatioVersionResolution.ProbeFailed());
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new ClassRequirementWithHintOptions());

		// Assert
		CreatioVersionRequirementException exception = act.Should()
			.Throw<CreatioVersionRequirementException>(because: "an uncheckable environment must fail closed even when a Hint is set")
			.Which;
		exception.ErrorCode.Should().Be(CreatioVersionRequirementException.VersionCheckFailedCode,
			because: "a no-response environment reports version-check-failed regardless of any declared Hint");
		exception.Message.Should().NotContain(TestHint,
			because: "the update-style Hint must NOT be appended to a connectivity/access failure message");
	}

	[Test]
	[Description("EnsureRequirements appends the attribute Hint to the version-too-old message when a Hint is set.")]
	public void EnsureRequirements_ShouldAppendHint_WhenVersionTooOldAndHintSet() {
		// Arrange
		_creatioVersionProviderMock.Resolve().Returns(Resolved(new Version(9, 0, 0)));
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new ClassRequirementWithHintOptions());

		// Assert
		act.Should().Throw<CreatioVersionRequirementException>(because: "9.0.0 is lower than the required 10.0.0")
			.Which.Message.Should().Contain(TestHint,
				because: "the actionable Hint must be appended so the user knows how to fix the version requirement");
	}

	[Test]
	[Description("EnsureRequirements enforces a property-level requirement when its bool flag is true.")]
	public void EnsureRequirements_ShouldThrow_WhenPropertyFlagTrueAndVersionTooOld() {
		// Arrange
		_creatioVersionProviderMock.Resolve().Returns(Resolved(new Version(9, 0, 0)));
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new PropertyRequirementOptions { UseGatedPath = true });

		// Assert
		act.Should().Throw<CreatioVersionRequirementException>(
				because: "the flag selecting the gated path is true so its version requirement is enforced")
			.Which.ErrorCode.Should().Be(CreatioVersionRequirementException.VersionTooOldCode,
				because: "the triggered property-level requirement is unmet by the older environment");
	}

	[Test]
	[Description("EnsureRequirements skips a property-level requirement and never resolves the version when its bool flag is false.")]
	public void EnsureRequirements_ShouldNotThrowOrResolveVersion_WhenPropertyFlagFalse() {
		// Arrange
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new PropertyRequirementOptions { UseGatedPath = false });

		// Assert
		act.Should().NotThrow(
			because: "a false flag does not select the gated path, so its version requirement is not enforced");
		_creatioVersionProviderMock.DidNotReceive().Resolve();
	}

	[Test]
	[Description("EnsureRequirements does not resolve the version when the options type declares no requirement.")]
	public void EnsureRequirements_ShouldNotResolveVersion_WhenTypeHasNoAttribute() {
		// Arrange
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();

		// Act
		checker.EnsureRequirements(new NoRequirementOptions());

		// Assert
		_creatioVersionProviderMock.DidNotReceive().Resolve();
	}

	[Test]
	[Description("EnsureRequirements fails fast with InvalidOperationException when a non-bool property carries the requirement.")]
	public void EnsureRequirements_ShouldThrowInvalidOperation_WhenNonBoolPropertyIsDecorated() {
		// Arrange
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new NonBoolPropertyRequirementOptions { Target = "x" });

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "only bool properties may carry a conditional [RequiresCreatioVersion]")
			.WithMessage("*Target*",
				because: "the misused property must be named so the developer can fix it");
		_creatioVersionProviderMock.DidNotReceive().Resolve();
	}

	[Test]
	[Description("EnsureRequirements fails fast with InvalidOperationException when the attribute declares a malformed minimum version (developer error, not version-too-old).")]
	public void EnsureRequirements_ShouldThrowInvalidOperation_WhenMinVersionIsMalformed() {
		// Arrange
		_creatioVersionProviderMock.Resolve().Returns(Resolved(new Version(10, 0, 0)));
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new MalformedMinVersionOptions());

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a malformed attribute MinVersion is a developer error and must not be conflated with an unmet environment version")
			.WithMessage("*10.x*",
				because: "the invalid value must be named so the developer can fix the attribute declaration");
	}

	[Test]
	[Description("IsCompatible returns false (does not throw) when a source responded but the version is undeterminable (ReachableWithoutVersion).")]
	public void IsCompatible_ShouldReturnFalse_WhenReachableWithoutVersion() {
		// Arrange
		_creatioVersionProviderMock.Resolve().Returns(CreatioVersionResolution.ReachableWithoutVersion());
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();

		// Act
		bool actual = checker.IsCompatible("10.0.0");

		// Assert
		actual.Should().BeFalse(
			because: "an undeterminable version is not compatible and IsCompatible must never throw");
	}

	[Test]
	[Description("IsCompatible returns false (does not throw) when no source responded so the version check could not be performed (ProbeFailed).")]
	public void IsCompatible_ShouldReturnFalse_WhenProbeFailed() {
		// Arrange
		_creatioVersionProviderMock.Resolve().Returns(CreatioVersionResolution.ProbeFailed());
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();

		// Act
		bool actual = checker.IsCompatible("10.0.0");

		// Assert
		actual.Should().BeFalse(
			because: "an uncheckable environment is not compatible and IsCompatible must never throw");
	}

	[Test]
	[Description("IsCompatible returns true when the environment version meets the required minimum.")]
	public void IsCompatible_ShouldReturnTrue_WhenCurrentVersionMeetsMinimum() {
		// Arrange
		_creatioVersionProviderMock.Resolve().Returns(Resolved(new Version(10, 0, 0)));
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();

		// Act
		bool actual = checker.IsCompatible("10.0.0");

		// Assert
		actual.Should().BeTrue(because: "an equal version satisfies a minimum-version requirement");
	}

	[Test]
	[Description("EnsureRequirements does not throw for a 3-part development build (0.0.0) — the dev-build bypass recognises a 3-part build (Revision -1) as well as a 4-part 0.0.0.0.")]
	public void EnsureRequirements_ShouldNotThrow_WhenCurrentVersionIsThreePartDevBuild() {
		// Arrange
		_creatioVersionProviderMock.Resolve().Returns(Resolved(new Version(0, 0, 0)));
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new ClassRequirementOptions());

		// Assert
		act.Should().NotThrow(
			because: "a 3-part 0.0.0 (Revision -1) is a development build and must be treated as compatible with any requirement");
	}

	[Test]
	[Description("IsCompatible throws InvalidOperationException on a malformed minVersion (developer error), exactly as EnsureRequirements classifies it — it must not silently return false.")]
	public void IsCompatible_ShouldThrowInvalidOperation_WhenMinVersionIsMalformed() {
		// Arrange
		_creatioVersionProviderMock.Resolve().Returns(Resolved(new Version(10, 0, 0)));
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();

		// Act
		Action act = () => checker.IsCompatible("10.x");

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "a malformed minimum version is a developer error and must fail fast, never be conflated with an incompatible environment")
			.WithMessage("*10.x*",
				because: "the invalid value must be named so the developer can fix the call site");
	}

	[Test]
	[Description("EnsureRequirements enforces the STRICTEST of multiple triggered requirements: with a 10.0.0 class floor and a triggered 11.0.0 property floor on an env running 10.5, it throws version-too-old reporting 11.0.0 (the max).")]
	public void EnsureRequirements_ShouldThrowVersionTooOldAgainstStrictest_WhenMultipleRequirementsTriggered() {
		// Arrange
		_creatioVersionProviderMock.Resolve().Returns(Resolved(new Version(10, 5, 0)));
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new MultiRequirementOptions { UseGatedPath = true });

		// Assert
		CreatioVersionRequirementException exception = act.Should()
			.Throw<CreatioVersionRequirementException>(
				because: "10.5.0 satisfies the 10.0.0 class floor but not the stricter triggered 11.0.0 property floor")
			.Which;
		exception.ErrorCode.Should().Be(CreatioVersionRequirementException.VersionTooOldCode,
			because: "the strictest triggered requirement is unmet so the version-too-old code must be reported");
		exception.Message.Should().Contain("11.0.0",
			because: "the message must name the strictest required floor (11.0.0), not the weaker first-declared 10.0.0");
	}

	[Test]
	[Description("EnsureRequirements names the STRICTEST floor in the undeterminable message: with a 10.0.0 class floor and a triggered 11.0.0 property floor and an undeterminable env, the message names 11.0.0.")]
	public void EnsureRequirements_ShouldReportStrictestInUndeterminableMessage_WhenMultipleRequirementsTriggered() {
		// Arrange
		_creatioVersionProviderMock.Resolve().Returns(CreatioVersionResolution.ReachableWithoutVersion());
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new MultiRequirementOptions { UseGatedPath = true });

		// Assert
		CreatioVersionRequirementException exception = act.Should()
			.Throw<CreatioVersionRequirementException>(
				because: "an undeterminable version must fail closed regardless of how many requirements are triggered")
			.Which;
		exception.ErrorCode.Should().Be(CreatioVersionRequirementException.VersionUndeterminableCode,
			because: "an undeterminable version must report the version-undeterminable error code");
		exception.Message.Should().Contain("11.0.0",
			because: "the undeterminable message must name the strictest required floor (11.0.0), not the weaker first-declared 10.0.0");
	}

	#endregion

}
