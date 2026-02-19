using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Category("CommandTests")]
[Platform(Include = "Win")]
public class ManageWindowsFeaturesCommandTests : BaseCommandTests<ManageWindowsFeaturesOptions>
{
	private IWindowsFeatureManager _windowsFeatureManager;
	private ManageWindowsFeaturesCommand _sut;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder)
	{
		base.AdditionalRegistrations(containerBuilder);
		_windowsFeatureManager = Substitute.For<IWindowsFeatureManager>();
		containerBuilder.AddSingleton<IWindowsFeatureManager>(_windowsFeatureManager);
	}

	public override void Setup()
	{
		base.Setup();
		_sut = Container.GetRequiredService<ManageWindowsFeaturesCommand>();
	}

#region Check Mode Tests

	[Test]
	[Description("Should check required features and return success code when all features are installed")]
	public void Execute_CheckModeWithAllFeaturesInstalled_ReturnsSuccessCode()
	{
		// Arrange
		var options = new ManageWindowsFeaturesOptions { CheckMode = true };
		var installedFeature = new WindowsFeature { Name = "Feature1", Installed = true };
		_windowsFeatureManager.GetMissedComponents().Returns(new List<WindowsFeature>());
		_windowsFeatureManager.GetRequiredComponent().Returns(new List<WindowsFeature> { installedFeature });

		// Act
		var actual = _sut.Execute(options);

		// Assert
		actual.Should().Be(0, "because all required features are installed");
	}

	[Test]
	[Description("Should check required features and return error code when some features are missing")]
	public void Execute_CheckModeWithMissingFeatures_ReturnsErrorCode()
	{
		// Arrange
		var options = new ManageWindowsFeaturesOptions { CheckMode = true };
		var missingFeature = new WindowsFeature { Name = "Feature1", Installed = false };
		_windowsFeatureManager.GetMissedComponents().Returns(new List<WindowsFeature> { missingFeature });
		_windowsFeatureManager.GetRequiredComponent().Returns(new List<WindowsFeature> { missingFeature });

		// Act
		var actual = _sut.Execute(options);

		// Assert
		actual.Should().Be(1, "because some required features are missing");
	}

#endregion

#region Install Mode Tests

	[Test]
	[Description("Should install missing features and return success code")]
	public void Execute_InstallModeWithSuccess_ReturnsSuccessCode()
	{
		// Arrange
		var options = new ManageWindowsFeaturesOptions { InstallMode = true };
		_windowsFeatureManager.When(x => x.InstallMissingFeatures()).Do(x => { });

		// Act
		var actual = _sut.Execute(options);

		// Assert
		actual.Should().Be(0, "because installation completed successfully");
		_windowsFeatureManager.Received(1).InstallMissingFeatures();
	}

	[Test]
	[Description("Should return error code when installation fails")]
	public void Execute_InstallModeWithException_ReturnsErrorCode()
	{
		// Arrange
		var options = new ManageWindowsFeaturesOptions { InstallMode = true };
		_windowsFeatureManager
			.When(x => x.InstallMissingFeatures())
			.Do(x => throw new Exception("Installation failed"));

		// Act
		var actual = _sut.Execute(options);

		// Assert
		actual.Should().Be(1, "because installation failed with exception");
	}

#endregion

#region Uninstall Mode Tests

	[Test]
	[Description("Should uninstall features and return success code")]
	public void Execute_UninstallModeWithSuccess_ReturnsSuccessCode()
	{
		// Arrange
		var options = new ManageWindowsFeaturesOptions { UninstallMode = true };
		_windowsFeatureManager.When(x => x.UninstallMissingFeatures()).Do(x => { });

		// Act
		var actual = _sut.Execute(options);

		// Assert
		actual.Should().Be(0, "because uninstallation completed successfully");
		_windowsFeatureManager.Received(1).UninstallMissingFeatures();
	}

	[Test]
	[Description("Should return error code when uninstallation fails")]
	public void Execute_UninstallModeWithException_ReturnsErrorCode()
	{
		// Arrange
		var options = new ManageWindowsFeaturesOptions { UninstallMode = true };
		_windowsFeatureManager
			.When(x => x.UninstallMissingFeatures())
			.Do(x => throw new Exception("Uninstallation failed"));

		// Act
		var actual = _sut.Execute(options);

		// Assert
		actual.Should().Be(1, "because uninstallation failed with exception");
	}

#endregion

#region No Mode Tests

	[Test]
	[Description("Should return success when no mode is specified")]
	public void Execute_NoModeSpecified_ReturnsSuccessCode()
	{
		// Arrange
		var options = new ManageWindowsFeaturesOptions
		{
			CheckMode = false,
			InstallMode = false,
			UninstallMode = false
		};

		// Act
		var actual = _sut.Execute(options);

		// Assert
		actual.Should().Be(0, "because command returns success when no mode is specified");
	}

#endregion
}