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
public class ClioGatewayTests : BaseClioModuleTests
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

	#region Methods: Protected

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder){
		containerBuilder.AddSingleton<IApplicationPackageListProvider>(_applicationPackageListProviderMock);
		base.AdditionalRegistrations(containerBuilder);
	}

	#endregion

	[Test]
	public void GetInstalledVersion_Should_LowestVersion_When_BothInstalled(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(3, 0, 0), NetFrameworkClioPkgName),
			_createPackageInfo(new Version(2, 0, 0), Net6ClioPkgName),
			_createPackageInfo(new Version(2, 2, 2), "not_cliogate")
		]);
		IClioGateway clioGateway = Container.GetRequiredService<IClioGateway>();

		// Act
		PackageVersion actualPackageInfo = clioGateway.GetInstalledVersion();

		// Assert
		actualPackageInfo.Should().BeEquivalentTo(new PackageVersion(new Version(2, 0, 0), string.Empty));
	}

	[Test]
	public void GetInstalledVersion_Should_ReturnPackageInfo(){
		// Arrange

		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(2, 2, 2), "not_cliogate"),
			_createPackageInfo(new Version(1, 1, 1), Net6ClioPkgName)
		]);
		IClioGateway clioGateway = Container.GetRequiredService<IClioGateway>();

		// Act
		PackageVersion actualPackageInfo = clioGateway.GetInstalledVersion();

		// Assert
		actualPackageInfo.ToString().Should().BeEquivalentTo("1.1.1");
	}

	[Test]
	public void GetInstalledVersion_Should_ReturnPackageInfoNetCore(){
		// Arrange

		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(2, 2, 2), "not_cliogate"),
			_createPackageInfo(new Version(1, 1, 1), NetFrameworkClioPkgName)
		]);
		IClioGateway clioGateway = Container.GetRequiredService<IClioGateway>();

		// Act
		PackageVersion actualPackageInfo = clioGateway.GetInstalledVersion();

		// Assert
		actualPackageInfo.ToString().Should().BeEquivalentTo("1.1.1");
	}

	[Test]
	public void IsCompatibleWith_Should_BeTey_When_CheckedLowerThanExisting(){
		// Arrange

		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(2, 2, 2), "not_cliogate"),
			_createPackageInfo(new Version(1, 1, 1), NetFrameworkClioPkgName)
		]);
		IClioGateway clioGateway = Container.GetRequiredService<IClioGateway>();

		// Act
		bool actualPackageInfo = clioGateway.IsCompatibleWith("1.0.0");

		// Assert
		actualPackageInfo.Should().BeTrue();
	}

	[Test]
	public void IsCompatibleWith_ShouldBeFalse_When_ClioGate_NotInstalled(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(2, 2, 2), "not_cliogate")
		]);
		IClioGateway clioGateway = Container.GetRequiredService<IClioGateway>();

		// Act
		bool actualPackageInfo = clioGateway.IsCompatibleWith("1.0.0");

		// Assert
		actualPackageInfo.Should().BeFalse();
	}

	[Test]
	public void IsCompatibleWith_ShouldReturnFalse_When_CheckedHigherThanExisting(){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(2, 2, 2), NetFrameworkClioPkgName),
			_createPackageInfo(new Version(2, 2, 2), "not_cliogate")
		]);
		IClioGateway clioGateway = Container.GetRequiredService<IClioGateway>();

		// Act
		bool actualPackageInfo = clioGateway.IsCompatibleWith("3.0.0");

		// Assert
		actualPackageInfo.Should().BeFalse();
	}

	[Test]
	public void IsCompatibleWith_ShouldReturnFalse_When_CheckedHigherThanExisting_NetCore(){
		// Arrange

		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(2, 2, 2), Net6ClioPkgName),
			_createPackageInfo(new Version(2, 2, 2), "not_cliogate")
		]);
		IClioGateway clioGateway = Container.GetRequiredService<IClioGateway>();

		// Act
		bool actualPackageInfo = clioGateway.IsCompatibleWith("3.0.0");

		// Assert
		actualPackageInfo.Should().BeFalse();
	}
	
	[TestCase("2.2.2.3", "3.0.0", false)]
	[TestCase("2.2.2", "3.0.0.0", false)]
	[TestCase("2.2", "3.0.0.0", false)]
	[TestCase("2.2.2.3", "3.0", false)]
	
	[TestCase("4.2.2.3", "3.0.0", true)]
	[TestCase("4.2.2", "3.0.0.0", true)]
	[TestCase("4.2", "3.0.0.0", true)]
	[TestCase("4.2.2.3", "3.0", true)]
	[TestCase("3.0.0", "3.0", true)]
	[TestCase("3.0.0.0", "3.0", true)]
	public void IsCompatibleWith_ShouldReturnFalse_When_CheckedHigherThanExisting_4DigitVersion(
		string installedVersion, string requiredVersion, bool expectedResult){
		// Arrange

		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(installedVersion), Net6ClioPkgName),
		]);
		IClioGateway clioGateway = Container.GetRequiredService<IClioGateway>();

		// Act
		bool actualPackageInfo = clioGateway.IsCompatibleWith(requiredVersion);

		// Assert
		actualPackageInfo.Should().Be(expectedResult);
	}
	
	[TestCase("1.0.0", "2.0.0", false, "To use this command, you need to install the cliogate package version 2.0.0 or higher.")]
	[TestCase("2.0.0", "1.0.0", true, null)]
	[TestCase("2.0.0", "2.0.0", true, null)]
	public void CheckCompatibleVersion_Should_BehaveAsExpected(string installedVersion, string requiredVersion, bool shouldNotThrow, string expectedMessage){
		// Arrange
		_applicationPackageListProviderMock.GetPackages().Returns([
			_createPackageInfo(new Version(installedVersion), NetFrameworkClioPkgName)
		]);
		IClioGateway clioGateway = Container.GetRequiredService<IClioGateway>();
	
		// Act
		Action act = () => clioGateway.CheckCompatibleVersion(requiredVersion);
	
		// Assert
		if (shouldNotThrow) {
			act.Should().NotThrow();
		} else {
			act.Should().Throw<NotSupportedException>()
				.WithMessage(expectedMessage);
		}
	}

}