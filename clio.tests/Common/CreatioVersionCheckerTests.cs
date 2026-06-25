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
		_creatioVersionProviderMock.GetCoreVersion().Returns((Version)null);
	}

	[Test]
	[Description("EnsureRequirements does not throw when the environment version is newer than the required minimum.")]
	public void EnsureRequirements_ShouldNotThrow_WhenCurrentVersionIsNewer() {
		// Arrange
		_creatioVersionProviderMock.GetCoreVersion().Returns(new Version(10, 1, 0));
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
		_creatioVersionProviderMock.GetCoreVersion().Returns(new Version(10, 0, 0));
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
		_creatioVersionProviderMock.GetCoreVersion().Returns(new Version(9, 9, 0));
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
		_creatioVersionProviderMock.GetCoreVersion().Returns(new Version(0, 0, 0, 0));
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new ClassRequirementOptions());

		// Assert
		act.Should().NotThrow(because: "a development build (0.0.0.0) is treated as compatible with any requirement");
	}

	[Test]
	[Description("EnsureRequirements throws version-undeterminable (fail-closed) when the environment version cannot be determined.")]
	public void EnsureRequirements_ShouldThrowVersionUndeterminable_WhenVersionIsNull() {
		// Arrange
		_creatioVersionProviderMock.GetCoreVersion().Returns((Version)null);
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new ClassRequirementOptions());

		// Assert
		act.Should().Throw<CreatioVersionRequirementException>(because: "an undeterminable version must fail closed")
			.Which.ErrorCode.Should().Be(CreatioVersionRequirementException.VersionUndeterminableCode,
				because: "an undeterminable version must report the version-undeterminable error code");
	}

	[Test]
	[Description("EnsureRequirements appends the attribute Hint to the version-too-old message when a Hint is set.")]
	public void EnsureRequirements_ShouldAppendHint_WhenVersionTooOldAndHintSet() {
		// Arrange
		_creatioVersionProviderMock.GetCoreVersion().Returns(new Version(9, 0, 0));
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
		_creatioVersionProviderMock.GetCoreVersion().Returns(new Version(9, 0, 0));
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
		_creatioVersionProviderMock.DidNotReceive().GetCoreVersion();
	}

	[Test]
	[Description("EnsureRequirements does not resolve the version when the options type declares no requirement.")]
	public void EnsureRequirements_ShouldNotResolveVersion_WhenTypeHasNoAttribute() {
		// Arrange
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();

		// Act
		checker.EnsureRequirements(new NoRequirementOptions());

		// Assert
		_creatioVersionProviderMock.DidNotReceive().GetCoreVersion();
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
		_creatioVersionProviderMock.DidNotReceive().GetCoreVersion();
	}

	[Test]
	[Description("EnsureRequirements fails fast with InvalidOperationException when the attribute declares a malformed minimum version (developer error, not version-too-old).")]
	public void EnsureRequirements_ShouldThrowInvalidOperation_WhenMinVersionIsMalformed() {
		// Arrange
		_creatioVersionProviderMock.GetCoreVersion().Returns(new Version(10, 0, 0));
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
	[Description("IsCompatible returns false (does not throw) when the environment version cannot be determined.")]
	public void IsCompatible_ShouldReturnFalse_WhenVersionIsUndeterminable() {
		// Arrange
		_creatioVersionProviderMock.GetCoreVersion().Returns((Version)null);
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();

		// Act
		bool actual = checker.IsCompatible("10.0.0");

		// Assert
		actual.Should().BeFalse(
			because: "an undeterminable version is not compatible and IsCompatible must never throw");
	}

	[Test]
	[Description("IsCompatible returns true when the environment version meets the required minimum.")]
	public void IsCompatible_ShouldReturnTrue_WhenCurrentVersionMeetsMinimum() {
		// Arrange
		_creatioVersionProviderMock.GetCoreVersion().Returns(new Version(10, 0, 0));
		ICreatioVersionChecker checker = Container.GetRequiredService<ICreatioVersionChecker>();

		// Act
		bool actual = checker.IsCompatible("10.0.0");

		// Assert
		actual.Should().BeTrue(because: "an equal version satisfies a minimum-version requirement");
	}

	#endregion

}
