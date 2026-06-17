using System;
using System.Collections.Generic;
using System.Globalization;
using Clio.Common;
using Clio.Package;
using Clio.Project.NuGet;
using Clio.Tests.Command;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture(Category = "Unit")]
[Property("Module", "Common")]
public class RequiredPackageCheckerTests : BaseClioModuleTests
{

	#region Constants: Private

	private const string Net6ClioPkgName = "cliogate_netcore";
	private const string NetFrameworkClioPkgName = "cliogate";

	#endregion

	#region Fields: Private

	private readonly IApplicationPackageListProvider _applicationPackageListProviderMock
		= Substitute.For<IApplicationPackageListProvider>();

	private readonly Func<Version, string, PackageInfo> _createPackageInfo = (version, name) => {
		PackageDescriptor descriptor = new() {
			DependsOn = new List<PackageDependency>(),
			UId = Guid.NewGuid(),
			Maintainer = "Fake_Maintainer",
			ModifiedOnUtc = DateTime.UtcNow.ToString(CultureInfo.InvariantCulture),
			Name = name,
			PackageVersion = version.ToString(),
			ProjectPath = string.Empty
		};
		return new PackageInfo(descriptor, string.Empty, new List<string>());
	};

	#endregion

	#region Nested types: Test fixtures

	private sealed class NoRequirementOptions { }

	[RequiresPackage("MyPkg", "2.0.0")]
	private sealed class SingleVersionedRequirementOptions { }

	[RequiresPackage("MyPkg")]
	private sealed class PresenceOnlyRequirementOptions { }

	[RequiresPackage("cliogate", "1.0.0")]
	private sealed class CliogateRequirementOptions { }

	[RequiresPackage("MyPkg", "2.0.0")]
	[RequiresPackage("OtherPkg", "1.0.0")]
	private sealed class MultipleRequirementsOptions { }

	private const string TestHint = "Run 'clio install-gate' to fix this.";

	[RequiresPackage("MyPkg", "2.0.0", Hint = TestHint)]
	private sealed class VersionedRequirementWithHintOptions { }

	[RequiresPackage("MyPkg", Hint = TestHint)]
	private sealed class PresenceOnlyRequirementWithHintOptions { }

	[RequiresPackage("MyPkg", "2.0.x")]
	private sealed class MalformedVersionRequirementOptions { }

	// Property-level (presence-only) requirement: enforced only when the bool flag is true.
	private sealed class PropertyRequirementOptions {
		[RequiresPackage("MyPkg")]
		public bool UseGatedPath { get; set; }
	}

	// Property-level VERSIONED requirement: enforced (with a version comparison) only when the bool flag is true.
	private sealed class VersionedPropertyRequirementOptions {
		[RequiresPackage("SomePkg", "2.0.0")]
		public bool UseGatedPath { get; set; }
	}

	// Mixed: a class-level requirement (always) plus a property-level requirement (conditional).
	[RequiresPackage("AlwaysPkg")]
	private sealed class MixedRequirementOptions {
		[RequiresPackage("FlaggedPkg")]
		public bool UseGatedPath { get; set; }
	}

	// Misuse: [RequiresPackage] on a non-bool property must fail fast.
	private sealed class NonBoolPropertyRequirementOptions {
		[RequiresPackage("MyPkg")]
		public string PackageName { get; set; }
	}

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder){
		containerBuilder.AddSingleton<IApplicationPackageListProvider>(_applicationPackageListProviderMock);
		base.AdditionalRegistrations(containerBuilder);
	}

	#endregion

	#region Methods: Public

	[TearDown]
	public void ClearMockState(){
		_applicationPackageListProviderMock.ClearReceivedCalls();
	}

	[Test]
	[Description("IsInstalled returns false when the requested package is not installed.")]
	public void IsInstalled_ShouldReturnFalse_WhenPackageNotInstalled(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(2, 2, 2), "not_my_pkg")
		]);
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		bool actual = checker.IsInstalled("MyPkg");

		// Assert
		actual.Should().BeFalse(because: "the package is absent from the installed package list");
	}

	[Test]
	[Description("IsCompatible returns false when the requested package is not installed.")]
	public void IsCompatible_ShouldReturnFalse_WhenPackageNotInstalled(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(2, 2, 2), "not_my_pkg")
		]);
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		bool actual = checker.IsCompatible("MyPkg", "1.0.0");

		// Assert
		actual.Should().BeFalse(because: "an absent package can never satisfy a version requirement");
	}

	[Test]
	[Description("EnsureRequirements throws when a versioned requirement's package is not installed.")]
	public void EnsureRequirements_ShouldThrow_WhenPackageNotInstalled(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(2, 2, 2), "not_my_pkg")
		]);
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new SingleVersionedRequirementOptions());

		// Assert
		act.Should().Throw<PackageRequirementException>(because: "the required package is missing")
			.WithMessage("*MyPkg*2.0.0*");
	}

	[Test]
	[Description("EnsureRequirements throws when the installed version is lower than required.")]
	public void EnsureRequirements_ShouldThrow_WhenInstalledVersionIsOlder(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(1, 0, 0), "MyPkg")
		]);
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new SingleVersionedRequirementOptions());

		// Assert
		act.Should().Throw<PackageRequirementException>(because: "1.0.0 is lower than the required 2.0.0")
			.WithMessage("*MyPkg*2.0.0*");
	}

	[Test]
	[Description("EnsureRequirements does not throw when the installed version is newer than required.")]
	public void EnsureRequirements_ShouldNotThrow_WhenInstalledVersionIsNewer(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(3, 0, 0), "MyPkg")
		]);
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new SingleVersionedRequirementOptions());

		// Assert
		act.Should().NotThrow(because: "3.0.0 satisfies the minimum required 2.0.0");
	}

	[Test]
	[Description("EnsureRequirements does not throw when the installed version equals the required version.")]
	public void EnsureRequirements_ShouldNotThrow_WhenInstalledVersionIsEqual(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(2, 0, 0), "MyPkg")
		]);
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new SingleVersionedRequirementOptions());

		// Assert
		act.Should().NotThrow(because: "an equal version satisfies a minimum-version requirement");
	}

	[Test]
	[Description("GetInstalledVersion resolves the cliogate package via its cliogate_netcore alias.")]
	public void GetInstalledVersion_ShouldResolveAlias_WhenOnlyNetCoreInstalled(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(1, 1, 1), Net6ClioPkgName),
			_createPackageInfo(new Version(2, 2, 2), "not_cliogate")
		]);
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		PackageVersion actual = checker.GetInstalledVersion("cliogate");

		// Assert
		actual.Should().NotBeNull(because: "the cliogate_netcore alias must resolve the cliogate requirement");
		actual.ToString().Should().Be("1.1.1", because: "the aliased package version must be returned");
	}

	[Test]
	[Description("GetInstalledVersion returns the lowest version when both cliogate aliases are installed.")]
	public void GetInstalledVersion_ShouldReturnLowestVersion_WhenBothAliasesInstalled(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(3, 0, 0), NetFrameworkClioPkgName),
			_createPackageInfo(new Version(2, 0, 0), Net6ClioPkgName)
		]);
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		PackageVersion actual = checker.GetInstalledVersion("cliogate");

		// Assert
		actual.ToString().Should().Be("2.0.0", because: "the lowest installed alias version is the safe lower bound");
	}

	[Test]
	[Description("EnsureRequirements passes for a versioned cliogate requirement satisfied via the alias.")]
	public void EnsureRequirements_ShouldNotThrow_WhenCliogateSatisfiedViaAlias(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(1, 1, 1), Net6ClioPkgName)
		]);
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new CliogateRequirementOptions());

		// Assert
		act.Should().NotThrow(because: "cliogate_netcore 1.1.1 satisfies the cliogate 1.0.0 requirement");
	}

	[Test]
	[Description("Package name matching is case-insensitive.")]
	public void IsInstalled_ShouldReturnTrue_WhenNameDiffersOnlyByCase(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(1, 0, 0), "MyPkg")
		]);
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		bool actual = checker.IsInstalled("mypkg");

		// Assert
		actual.Should().BeTrue(because: "package name comparison must be case-insensitive");
	}

	[Test]
	[Description("A presence-only requirement passes when the package is installed regardless of version.")]
	public void EnsureRequirements_ShouldNotThrow_WhenPresenceOnlyRequirementInstalled(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(0, 0, 1), "MyPkg")
		]);
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new PresenceOnlyRequirementOptions());

		// Assert
		act.Should().NotThrow(because: "a presence-only requirement performs no version comparison");
	}

	[Test]
	[Description("A presence-only requirement throws when the package is not installed.")]
	public void EnsureRequirements_ShouldThrow_WhenPresenceOnlyRequirementMissing(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(1, 0, 0), "not_my_pkg")
		]);
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new PresenceOnlyRequirementOptions());

		// Assert
		act.Should().Throw<PackageRequirementException>(because: "the presence-only package is absent")
			.WithMessage("*MyPkg*");
	}

	[Test]
	[Description("EnsureRequirements appends the attribute Hint to the exception message for a versioned requirement when the package is missing.")]
	public void EnsureRequirements_ShouldAppendHint_WhenVersionedRequirementUnmetAndHintSet(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(1, 0, 0), "not_my_pkg")
		]);
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new VersionedRequirementWithHintOptions());

		// Assert
		act.Should().Throw<PackageRequirementException>(because: "the required package is missing")
			.Which.Message.Should().Contain(TestHint,
				because: "the actionable Hint must be appended to the message so the user knows how to fix it");
	}

	[Test]
	[Description("EnsureRequirements appends the attribute Hint to the exception message for a presence-only requirement when the package is missing.")]
	public void EnsureRequirements_ShouldAppendHint_WhenPresenceOnlyRequirementUnmetAndHintSet(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(1, 0, 0), "not_my_pkg")
		]);
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new PresenceOnlyRequirementWithHintOptions());

		// Assert
		act.Should().Throw<PackageRequirementException>(because: "the presence-only package is missing")
			.Which.Message.Should().Contain(TestHint,
				because: "the actionable Hint must be appended to the presence-only message");
	}

	[Test]
	[Description("EnsureRequirements does not append trailing hint text when the requirement declares no Hint.")]
	public void EnsureRequirements_ShouldNotAppendHint_WhenHintIsNotSet(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(1, 0, 0), "not_my_pkg")
		]);
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new SingleVersionedRequirementOptions());

		// Assert
		act.Should().Throw<PackageRequirementException>(because: "the required package is missing")
			.Which.Message.Should().EndWith("retry.",
				because: "with no Hint the message must end at the base guidance with no appended hint line");
	}

	[Test]
	[Description("EnsureRequirements throws a PackageRequirementException (not a raw FormatException) when the requirement declares a malformed version string.")]
	public void EnsureRequirements_ShouldThrowPackageRequirementException_WhenRequirementVersionIsMalformed(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(3, 0, 0), "MyPkg")
		]);
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new MalformedVersionRequirementOptions());

		// Assert
		act.Should().Throw<PackageRequirementException>(
				because: "a malformed version must flow through the friendly-error path, not escape as a raw FormatException")
			.WithMessage("*MyPkg*2.0.x*",
				because: "the message must name the offending package and the bad version string");
	}

	[Test]
	[Description("IsCompatible throws a PackageRequirementException (not a raw FormatException) when the requested version is malformed.")]
	public void IsCompatible_ShouldThrowPackageRequirementException_WhenVersionIsMalformed(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(3, 0, 0), "MyPkg")
		]);
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		Action act = () => checker.IsCompatible("MyPkg", "2.0.x");

		// Assert
		act.Should().Throw<PackageRequirementException>(
				because: "a malformed version must not surface as a raw FormatException to the caller")
			.WithMessage("*MyPkg*2.0.x*",
				because: "the message must name the offending package and the bad version string");
	}

	[Test]
	[Description("EnsureRequirements throws naming the unsatisfied package when one of several requirements is satisfied and another is not.")]
	public void EnsureRequirements_ShouldThrowNamingUnsatisfiedPackage_WhenOneOfMultipleRequirementsFails(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(3, 0, 0), "MyPkg")
		]);
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new MultipleRequirementsOptions());

		// Assert
		act.Should().Throw<PackageRequirementException>(
				because: "the OtherPkg requirement is unsatisfied while MyPkg is satisfied")
			.WithMessage("*OtherPkg*",
				because: "the exception must name the unsatisfied package so the user knows what to install");
	}

	[Test]
	[Description("EnsureRequirements does not fetch the package list when the type has no requirement attribute.")]
	public void EnsureRequirements_ShouldNotFetchPackages_WhenTypeHasNoAttribute(){
		// Arrange
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		checker.EnsureRequirements(new NoRequirementOptions());

		// Assert
		_applicationPackageListProviderMock.DidNotReceive().GetPackages();
	}

	[Test]
	[Description("Multiple requirements on one type cause a single package-list fetch (cache).")]
	public void EnsureRequirements_ShouldFetchPackagesOnce_WhenMultipleRequirements(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(3, 0, 0), "MyPkg"),
			_createPackageInfo(new Version(2, 0, 0), "OtherPkg")
		]);
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		checker.EnsureRequirements(new MultipleRequirementsOptions());

		// Assert
		_applicationPackageListProviderMock.Received(1).GetPackages();
	}

	[Test]
	[Description("A property-level requirement throws when its bool flag is true and the package is absent.")]
	public void EnsureRequirements_ShouldThrow_WhenPropertyFlagTrueAndPackageMissing(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(1, 0, 0), "not_my_pkg")
		]);
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new PropertyRequirementOptions { UseGatedPath = true });

		// Assert
		act.Should().Throw<PackageRequirementException>(
				because: "the flag selecting the gated path is true so its package requirement is enforced")
			.WithMessage("*MyPkg*",
				because: "the exception must name the package the triggered flag requires");
	}

	[Test]
	[Description("A triggered property-level versioned requirement throws when the installed version is below the required minimum.")]
	public void EnsureRequirements_ShouldThrow_WhenPropertyFlagTrueAndInstalledVersionIsOlder(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(1, 0, 0), "SomePkg")
		]);
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new VersionedPropertyRequirementOptions { UseGatedPath = true });

		// Assert
		act.Should().Throw<PackageRequirementException>(
				because: "the flag selecting the gated path is true and the installed 1.0.0 is below the required 2.0.0")
			.WithMessage("*SomePkg*2.0.0*",
				because: "the exception must name the package and its unmet minimum version");
	}

	[Test]
	[Description("A triggered property-level versioned requirement does not throw when the installed version meets the required minimum.")]
	public void EnsureRequirements_ShouldNotThrow_WhenPropertyFlagTrueAndInstalledVersionMeetsMinimum(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(2, 0, 0), "SomePkg")
		]);
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new VersionedPropertyRequirementOptions { UseGatedPath = true });

		// Assert
		act.Should().NotThrow(
			because: "the gated path is selected and the installed 2.0.0 satisfies the required minimum 2.0.0");
	}

	[Test]
	[Description("A property-level requirement is skipped (no throw) and the package list is never fetched when its bool flag is false.")]
	public void EnsureRequirements_ShouldNotThrowOrFetch_WhenPropertyFlagFalse(){
		// Arrange
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new PropertyRequirementOptions { UseGatedPath = false });

		// Assert
		act.Should().NotThrow(
			because: "a false flag does not select the gated path, so its package requirement is not enforced");
		_applicationPackageListProviderMock.DidNotReceive().GetPackages();
	}

	[Test]
	[Description("A class-level requirement is always enforced even when a property flag on the same type is false.")]
	public void EnsureRequirements_ShouldThrowOnClassRequirement_WhenPropertyFlagFalseAndClassPackageMissing(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(1, 0, 0), "not_my_pkg")
		]);
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new MixedRequirementOptions { UseGatedPath = false });

		// Assert
		act.Should().Throw<PackageRequirementException>(
				because: "the class-level requirement is unconditional regardless of the property flag")
			.WithMessage("*AlwaysPkg*",
				because: "the unconditional class-level package is the one missing");
	}

	[Test]
	[Description("Both a class-level and a triggered property-level requirement are enforced when the flag is true.")]
	public void EnsureRequirements_ShouldThrowOnTriggeredProperty_WhenClassRequirementSatisfiedAndFlagTrue(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(1, 0, 0), "AlwaysPkg")
		]);
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new MixedRequirementOptions { UseGatedPath = true });

		// Assert
		act.Should().Throw<PackageRequirementException>(
				because: "the triggered property-level requirement (FlaggedPkg) is unsatisfied")
			.WithMessage("*FlaggedPkg*",
				because: "the missing flag-gated package must be named even though the class package is present");
	}

	[Test]
	[Description("A non-bool property carrying [RequiresPackage] fails fast with InvalidOperationException naming the property and never fetches packages.")]
	public void EnsureRequirements_ShouldThrowInvalidOperation_WhenNonBoolPropertyIsDecorated(){
		// Arrange
		IRequiredPackageChecker checker = Container.GetRequiredService<IRequiredPackageChecker>();

		// Act
		Action act = () => checker.EnsureRequirements(new NonBoolPropertyRequirementOptions { PackageName = "x" });

		// Assert
		act.Should().Throw<InvalidOperationException>(
				because: "only bool properties may carry a conditional [RequiresPackage]")
			.WithMessage("*PackageName*",
				because: "the misused property must be named so the developer can fix it");
		_applicationPackageListProviderMock.DidNotReceive().GetPackages();
	}

	#endregion

}
