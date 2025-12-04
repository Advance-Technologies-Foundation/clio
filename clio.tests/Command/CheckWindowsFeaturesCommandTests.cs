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

		// Act
		var actual = _sut.Execute(options);

		// Assert
		actual.Should().Be(0, "because all required features are installed");
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

		// Act
		var actual = _sut.Execute(options);

		// Assert
		actual.Should().Be(0, "because there are no required features");
	}

	[Test]
	[Description("Should verify all required components are checked")]
	public void Execute_CallsGetRequiredComponent()
	{
		// Arrange
		var options = new CheckWindowsFeaturesOptions();
		_windowsFeatureManager.GetMissedComponents().Returns(new List<WindowsFeature>());
		_windowsFeatureManager.GetRequiredComponent().Returns(new List<WindowsFeature>());

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

		// Act
		var actual = _sut.Execute(options);

		// Assert
		_windowsFeatureManager.Received(1).GetMissedComponents();
	}

#endregion
}