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
		Action act = () => checker.EnsureRequirements(typeof(SingleVersionedRequirementOptions));

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
		Action act = () => checker.EnsureRequirements(typeof(SingleVersionedRequirementOptions));

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
		Action act = () => checker.EnsureRequirements(typeof(SingleVersionedRequirementOptions));

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
		Action act = () => checker.EnsureRequirements(typeof(SingleVersionedRequirementOptions));

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
		Action act = () => checker.EnsureRequirements(typeof(CliogateRequirementOptions));

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
		Action act = () => checker.EnsureRequirements(typeof(PresenceOnlyRequirementOptions));

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
		Action act = () => checker.EnsureRequirements(typeof(PresenceOnlyRequirementOptions));

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
		Action act = () => checker.EnsureRequirements(typeof(VersionedRequirementWithHintOptions));

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
		Action act = () => checker.EnsureRequirements(typeof(PresenceOnlyRequirementWithHintOptions));

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
		Action act = () => checker.EnsureRequirements(typeof(SingleVersionedRequirementOptions));

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
		Action act = () => checker.EnsureRequirements(typeof(MalformedVersionRequirementOptions));

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
		Action act = () => checker.EnsureRequirements(typeof(MultipleRequirementsOptions));

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
		checker.EnsureRequirements(typeof(NoRequirementOptions));

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
		checker.EnsureRequirements(typeof(MultipleRequirementsOptions));

		// Assert
		_applicationPackageListProviderMock.Received(1).GetPackages();
	}

	#endregion

}
