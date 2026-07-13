namespace Clio.Tests.Command;

using System.Collections.Generic;
using System.Linq;
using Clio.Command.Theming;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class CheckThemingAccessCommandTests {

	private ICreatioRightsClient _rightsClient;
	private ICreatioLicenseClient _licenseClient;
	private ILogger _logger;
	private CheckThemingAccessCommand _command;

	[SetUp]
	public void SetUp() {
		_rightsClient = Substitute.For<ICreatioRightsClient>();
		_licenseClient = Substitute.For<ICreatioLicenseClient>();
		_logger = Substitute.For<ILogger>();
		_command = new CheckThemingAccessCommand(_rightsClient, _licenseClient, _logger);
	}

	[Test]
	[Description("Probes exactly the CanManageThemes operation and the CanCustomizeBranding license and reports full theming access when both are granted.")]
	public void GetThemingAccess_ShouldReportFullAccess_WhenOperationAndLicenseGranted() {
		// Arrange
		_rightsClient.GetCanExecuteOperation("CanManageThemes", Arg.Any<CreatioRequestOptions>()).Returns(true);
		_licenseClient.GetLicenseOperationStatuses(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new Dictionary<string, bool> { ["CanCustomizeBranding"] = true });

		// Act
		ThemingAccess access = _command.GetThemingAccess();

		// Assert
		access.CanManageThemes.Should().BeTrue(because: "the operation-right check returned true");
		access.CanCustomizeBranding.Should().BeTrue(because: "the license check returned true");
		_rightsClient.Received(1).GetCanExecuteOperation("CanManageThemes", Arg.Any<CreatioRequestOptions>());
		_licenseClient.Received(1).GetLicenseOperationStatuses(
			Arg.Is<IReadOnlyCollection<string>>(codes => codes.Single() == "CanCustomizeBranding"),
			Arg.Any<CreatioRequestOptions>());
	}

	[Test]
	[Description("Reports canCustomizeBranding=false when the operation right is granted but the CanCustomizeBranding license is absent from the status map.")]
	public void GetThemingAccess_ShouldReportNoBranding_WhenLicenseStatusIsMissing() {
		// Arrange
		_rightsClient.GetCanExecuteOperation("CanManageThemes", Arg.Any<CreatioRequestOptions>()).Returns(true);
		_licenseClient.GetLicenseOperationStatuses(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new Dictionary<string, bool>());

		// Act
		ThemingAccess access = _command.GetThemingAccess();

		// Assert
		access.CanManageThemes.Should().BeTrue(because: "the operation right was reported as granted");
		access.CanCustomizeBranding.Should().BeFalse(because: "the CanCustomizeBranding license was absent from the status map");
	}

	[Test]
	[Description("Reports canCustomizeBranding=false when the status map explicitly denies the CanCustomizeBranding license.")]
	public void GetThemingAccess_ShouldReportNoBranding_WhenLicenseStatusIsFalse() {
		// Arrange
		_rightsClient.GetCanExecuteOperation("CanManageThemes", Arg.Any<CreatioRequestOptions>()).Returns(false);
		_licenseClient.GetLicenseOperationStatuses(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new Dictionary<string, bool> { ["CanCustomizeBranding"] = false });

		// Act
		ThemingAccess access = _command.GetThemingAccess();

		// Assert
		access.CanManageThemes.Should().BeFalse(because: "the operation-right check returned false");
		access.CanCustomizeBranding.Should().BeFalse(because: "an explicit false in the status map means the license is not granted");
	}

	[Test]
	[Description("Logs both verdicts and returns the success exit code when the checks complete.")]
	public void Execute_ShouldLogBothVerdictsAndReturnZero_WhenChecksComplete() {
		// Arrange
		_rightsClient.GetCanExecuteOperation("CanManageThemes", Arg.Any<CreatioRequestOptions>()).Returns(true);
		_licenseClient.GetLicenseOperationStatuses(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CreatioRequestOptions>())
			.Returns(new Dictionary<string, bool> { ["CanCustomizeBranding"] = false });

		// Act
		int exitCode = _command.Execute(new CheckThemingAccessOptions());

		// Assert
		exitCode.Should().Be(0, because: "a completed access probe is a successful command run regardless of the verdicts");
		_logger.Received(1).WriteInfo(Arg.Is<string>(message =>
			message.Contains("CanManageThemes: True") && message.Contains("CanCustomizeBranding: False")));
	}
}
