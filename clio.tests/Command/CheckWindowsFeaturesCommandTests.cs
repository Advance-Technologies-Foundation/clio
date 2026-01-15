using System.Collections.Generic;
using Autofac;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Category("CommandTests")]
[Platform(Include = "Win")]
public class CheckWindowsFeaturesCommandTests : BaseCommandTests<CheckWindowsFeaturesOptions>
{
	private IWindowsFeatureManager _windowsFeatureManager;
	private CheckWindowsFeaturesCommand _sut;

	protected override void AdditionalRegistrations(ContainerBuilder containerBuilder)
	{
		base.AdditionalRegistrations(containerBuilder);
		_windowsFeatureManager = Substitute.For<IWindowsFeatureManager>();
		containerBuilder.RegisterInstance(_windowsFeatureManager).As<IWindowsFeatureManager>();
	}

	public override void Setup()
	{
		base.Setup();
		_sut = Container.Resolve<CheckWindowsFeaturesCommand>();
	}

#region Success Tests

	[Test]
	[Description("Should return success code when all features are installed")]
	public void Execute_AllFeaturesInstalled_ReturnsSuccessCode()
	{
		// Arrange
		var options = new CheckWindowsFeaturesOptions();
		var installedFeature = new WindowsFeature { Name = "Feature1", Installed = true };
		_windowsFeatureManager.GetMissedComponents().Returns(new List<WindowsFeature>());
		_windowsFeatureManager.GetRequiredComponent().Returns(new List<WindowsFeature> { installedFeature });
		_windowsFeatureManager.IsNetFramework472OrHigherInstalled().Returns(true);
		_windowsFeatureManager.GetNetFrameworkVersion().Returns("4.8");

		// Act
		var actual = _sut.Execute(options);

		// Assert
		actual.Should().Be(0, "because all required features are installed and .NET Framework 4.7.2+ is present");
	}

#endregion

#region Missing Features Tests

	[Test]
	[Description("Should return error code when some features are missing")]
	public void Execute_MissingFeatures_ReturnsErrorCode()
	{
		// Arrange
		var options = new CheckWindowsFeaturesOptions();
		var missingFeature = new WindowsFeature { Name = "Feature1", Installed = false };
		_windowsFeatureManager.GetMissedComponents().Returns(new List<WindowsFeature> { missingFeature });
		_windowsFeatureManager.GetRequiredComponent().Returns(new List<WindowsFeature> { missingFeature });
		_windowsFeatureManager.IsNetFramework472OrHigherInstalled().Returns(true);
		_windowsFeatureManager.GetNetFrameworkVersion().Returns("4.8");

		// Act
		var actual = _sut.Execute(options);

		// Assert
		actual.Should().Be(1, "because some required features are missing");
	}

	[Test]
	[Description("Should list all required features regardless of installation status")]
	public void Execute_WithMixedFeatures_LogsAllFeatures()
	{
		// Arrange
		var options = new CheckWindowsFeaturesOptions();
		var installedFeature = new WindowsFeature { Name = "Feature1", Installed = true };
		var missingFeature = new WindowsFeature { Name = "Feature2", Installed = false };
		_windowsFeatureManager.GetMissedComponents().Returns(new List<WindowsFeature> { missingFeature });
		_windowsFeatureManager.GetRequiredComponent().Returns(new List<WindowsFeature> { installedFeature, missingFeature });
		_windowsFeatureManager.IsNetFramework472OrHigherInstalled().Returns(true);
		_windowsFeatureManager.GetNetFrameworkVersion().Returns("4.8");

		// Act
		var actual = _sut.Execute(options);

		// Assert
		actual.Should().Be(1, "because some features are missing");
		_windowsFeatureManager.Received(1).GetMissedComponents();
		_windowsFeatureManager.Received(1).GetRequiredComponent();
	}

#endregion

#region Edge Cases

	[Test]
	[Description("Should return success code when no features are required (empty list)")]
	public void Execute_NoRequiredFeatures_ReturnsSuccessCode()
	{
		// Arrange
		var options = new CheckWindowsFeaturesOptions();
		_windowsFeatureManager.GetMissedComponents().Returns(new List<WindowsFeature>());
		_windowsFeatureManager.GetRequiredComponent().Returns(new List<WindowsFeature>());
		_windowsFeatureManager.IsNetFramework472OrHigherInstalled().Returns(true);
		_windowsFeatureManager.GetNetFrameworkVersion().Returns("4.8");

		// Act
		var actual = _sut.Execute(options);

		// Assert
		actual.Should().Be(0, "because there are no required features and .NET Framework is sufficient");
	}

	[Test]
	[Description("Should verify all required components are checked")]
	public void Execute_CallsGetRequiredComponent()
	{
		// Arrange
		var options = new CheckWindowsFeaturesOptions();
		_windowsFeatureManager.GetMissedComponents().Returns(new List<WindowsFeature>());
		_windowsFeatureManager.GetRequiredComponent().Returns(new List<WindowsFeature>());
		_windowsFeatureManager.IsNetFramework472OrHigherInstalled().Returns(true);
		_windowsFeatureManager.GetNetFrameworkVersion().Returns("4.8");

		// Act
		var actual = _sut.Execute(options);

		// Assert
		_windowsFeatureManager.Received(1).GetRequiredComponent();
	}

	[Test]
	[Description("Should verify missed components are checked")]
	public void Execute_CallsGetMissedComponents()
	{
		// Arrange
		var options = new CheckWindowsFeaturesOptions();
		_windowsFeatureManager.GetMissedComponents().Returns(new List<WindowsFeature>());
		_windowsFeatureManager.GetRequiredComponent().Returns(new List<WindowsFeature>());
		_windowsFeatureManager.IsNetFramework472OrHigherInstalled().Returns(true);
		_windowsFeatureManager.GetNetFrameworkVersion().Returns("4.8");

		// Act
		var actual = _sut.Execute(options);

		// Assert
		_windowsFeatureManager.Received(1).GetMissedComponents();
	}

#endregion

#region .NET Framework Tests

	[Test]
	[Description("Should return error code when .NET Framework 4.7.2 or higher is not installed")]
	public void Execute_MissingNetFramework_ReturnsErrorCode()
	{
		// Arrange
		var options = new CheckWindowsFeaturesOptions();
		_windowsFeatureManager.GetMissedComponents().Returns(new List<WindowsFeature>());
		_windowsFeatureManager.GetRequiredComponent().Returns(new List<WindowsFeature>());
		_windowsFeatureManager.IsNetFramework472OrHigherInstalled().Returns(false);
		_windowsFeatureManager.GetNetFrameworkVersion().Returns("4.6.1");

		// Act
		var actual = _sut.Execute(options);

		// Assert
		actual.Should().Be(1, "because .NET Framework 4.7.2 or higher is required");
	}

	[Test]
	[Description("Should return error code when both .NET Framework and Windows features are missing")]
	public void Execute_MissingNetFrameworkAndFeatures_ReturnsErrorCode()
	{
		// Arrange
		var options = new CheckWindowsFeaturesOptions();
		var missingFeature = new WindowsFeature { Name = "Feature1", Installed = false };
		_windowsFeatureManager.GetMissedComponents().Returns(new List<WindowsFeature> { missingFeature });
		_windowsFeatureManager.GetRequiredComponent().Returns(new List<WindowsFeature> { missingFeature });
		_windowsFeatureManager.IsNetFramework472OrHigherInstalled().Returns(false);
		_windowsFeatureManager.GetNetFrameworkVersion().Returns("Not installed");

		// Act
		var actual = _sut.Execute(options);

		// Assert
		actual.Should().Be(1, "because both .NET Framework 4.7.2+ and Windows features are missing");
	}

	[Test]
	[Description("Should check .NET Framework version")]
	public void Execute_CallsIsNetFramework472OrHigherInstalled()
	{
		// Arrange
		var options = new CheckWindowsFeaturesOptions();
		_windowsFeatureManager.GetMissedComponents().Returns(new List<WindowsFeature>());
		_windowsFeatureManager.GetRequiredComponent().Returns(new List<WindowsFeature>());
		_windowsFeatureManager.IsNetFramework472OrHigherInstalled().Returns(true);
		_windowsFeatureManager.GetNetFrameworkVersion().Returns("4.8");

		// Act
		var actual = _sut.Execute(options);

		// Assert
		_windowsFeatureManager.Received(1).IsNetFramework472OrHigherInstalled();
		_windowsFeatureManager.Received(1).GetNetFrameworkVersion();
	}

#endregion
}