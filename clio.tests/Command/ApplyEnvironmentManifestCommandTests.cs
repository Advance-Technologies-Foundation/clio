using System;
using System.Collections.Generic;
using ATF.Repository.Mock;
using ATF.Repository.Providers;
using Clio.Command;
using Clio.Common;
using Clio.Package;
using CreatioModel;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Unit coverage for <c>apply-manifest</c>'s delivery discipline: one refused entry must not abandon the rest of
/// the manifest, and a run that did not deliver every entry must not answer exit code 0.
/// </summary>
/// <remarks>
/// The refusals are driven through the two collaborators the command already exposes: the
/// <see cref="IApplicationInstaller"/> interface, and <c>SetWebServiceUrlCommand.Execute</c>, which is virtual
/// because it overrides the abstract command entry point. Every stage routes its entries through the same
/// record-and-continue helper, so the feature and system-setting stages need no additional reach into the
/// command to be covered by the same guarantee.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ApplyEnvironmentManifestCommandTests {

	private const string ManifestPath = "manifest.yaml";

	private IEnvironmentManager _environmentManager;
	private IApplicationInstaller _applicationInstaller;
	private SetWebServiceUrlCommand _setWebServiceUrlCommand;
	private ISysSettingsManager _sysSettingsManager;
	private ILogger _logger;
	private ApplyEnvironmentManifestCommand _sut;

	[SetUp]
	public void Setup() {
		_environmentManager = Substitute.For<IEnvironmentManager>();
		_applicationInstaller = Substitute.For<IApplicationInstaller>();
		_logger = Substitute.For<ILogger>();
		_sysSettingsManager = Substitute.For<ISysSettingsManager>();
		_sysSettingsManager
			.UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>())
			.Returns(true);
		_setWebServiceUrlCommand =
			Substitute.For<SetWebServiceUrlCommand>(new DataProviderMock(), new EnvironmentSettings());

		_environmentManager.GetApplicationsFromManifest(ManifestPath).Returns([]);
		_environmentManager.FindApplicationsInAppHub(ManifestPath).Returns([]);
		_environmentManager.GetFeaturesFromManifest(ManifestPath).Returns([]);
		_environmentManager.GetSettingsFromManifest(ManifestPath).Returns([]);
		_environmentManager.GetWebServicesFromManifest(ManifestPath).Returns([]);

		_sut = BuildSut();
	}

	private ApplyEnvironmentManifestCommand BuildSut(params string[] remoteApplications) {
		DataProviderMock provider = new();
		List<Dictionary<string, object>> rows = [];
		foreach (string name in remoteApplications) {
			rows.Add(new Dictionary<string, object> {
				["Id"] = Guid.NewGuid(),
				["Name"] = name,
				["Code"] = name
			});
		}
		provider.MockItems(nameof(SysInstalledApp)).Returns(rows);
		provider.MockItems(nameof(SysAdminUnit)).Returns([]);
		provider.MockItems(nameof(AppFeature)).Returns([]);
		provider.MockItems(nameof(AppFeatureState)).Returns([]);
		provider.MockItems(nameof(AdminUnitFeatureState)).Returns([]);
		EnvironmentSettings environmentSettings = new();
		return new ApplyEnvironmentManifestCommand(
			_environmentManager,
			_applicationInstaller,
			Substitute.For<FeatureCommand>(
				Substitute.For<IApplicationClient>(), environmentSettings, provider,
				Substitute.For<IServiceUrlBuilder>(), Substitute.For<IFeatureStateService>()),
			new SysSettingsCommand(_sysSettingsManager, _logger, Substitute.For<IFileSystem>()),
			_setWebServiceUrlCommand,
			provider,
			environmentSettings,
			_logger);
	}

	[TearDown]
	public void TearDown() {
		_environmentManager.ClearReceivedCalls();
		_applicationInstaller.ClearReceivedCalls();
		_setWebServiceUrlCommand.ClearReceivedCalls();
		_sysSettingsManager.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("Answers exit code 0 and reports nothing when every manifest entry applies.")]
	public void Execute_ShouldSucceedSilently_WhenEveryEntryApplies() {
		// Arrange
		ArrangeApplications("First.zip", "Second.zip");
		ArrangeWebServices("Service1");

		// Act
		int exitCode = _sut.Execute(Options());

		// Assert
		exitCode.Should().Be(0, because: "nothing was refused, so the manifest was applied in full");
		_logger.DidNotReceive().WriteError(Arg.Any<string>());
	}

	[Test]
	[Description("Installs the remaining applications after one of them is refused, instead of abandoning the stage at the first failure.")]
	public void Execute_ShouldApplyTheRemainingEntries_WhenOneIsRefused() {
		// Arrange
		ArrangeApplications("First.zip", "Second.zip");
		RefuseInstall("First.zip");

		// Act
		_sut.Execute(Options());

		// Assert
		_applicationInstaller.Received(1).Install("Second.zip", Arg.Any<EnvironmentSettings>());
	}

	[Test]
	[Description("Answers exit code 1 and names the refused entry, so a refused write is not reported as a clean run.")]
	public void Execute_ShouldFailNamingTheRefusedEntry_WhenAnEntryIsRefused() {
		// Arrange
		ArrangeApplications("First.zip");
		RefuseInstall("First.zip");

		// Act
		int exitCode = _sut.Execute(Options());

		// Assert
		exitCode.Should().Be(1,
			because: "the run did not deliver what the manifest asked for, and only a non-zero code tells a script that apart from a clean apply");
		_logger.Received().WriteError(Arg.Is<string>(message =>
			message.Contains("First.zip") && message.Contains("refused the installation")));
	}

	[Test]
	[Description("Applies the later stages after an earlier stage refuses an entry, so an application failure does not silently drop the web services the manifest names.")]
	public void Execute_ShouldApplyLaterStages_WhenAnEarlierStageRefusesAnEntry() {
		// Arrange
		ArrangeApplications("First.zip");
		RefuseInstall("First.zip");
		ArrangeWebServices("Service1");

		// Act
		int exitCode = _sut.Execute(Options());

		// Assert
		exitCode.Should().Be(1, because: "the application entry was refused");
		_setWebServiceUrlCommand.Received(1).Execute(
			Arg.Is<SetWebServiceUrlOptions>(options => options.WebServiceName == "Service1"));
	}

	[Test]
	[Description("Reports refusals from different stages together rather than stopping at the first, so one run tells the operator the full list to fix.")]
	public void Execute_ShouldReportRefusalsFromEveryStage_WhenSeveralAreRefused() {
		// Arrange
		ArrangeApplications("First.zip");
		RefuseInstall("First.zip");
		ArrangeWebServices("Service1");
		_setWebServiceUrlCommand.Execute(Arg.Any<SetWebServiceUrlOptions>()).Returns(1);

		// Act
		int exitCode = _sut.Execute(Options());

		// Assert
		exitCode.Should().Be(1, because: "two entries did not land");
		_logger.Received().WriteError(Arg.Is<string>(message => message.Contains("First.zip")));
		_logger.Received().WriteError(Arg.Is<string>(message => message.Contains("Service1")));
		_logger.Received().WriteError(Arg.Is<string>(message => message.Contains("except for 2 entries") && message.Contains("exits with code 1")));
	}

	[Test]
	[Description("Records an entry whose collaborator throws as well as one that reports refusal by return value, so neither failure channel goes unreported.")]
	public void Execute_ShouldRecordAFailure_WhenTheCollaboratorThrows() {
		// Arrange
		ArrangeApplications("First.zip");
		ThrowOnInstall("First.zip", "the archive is corrupt");

		// Act
		int exitCode = _sut.Execute(Options());

		// Assert
		exitCode.Should().Be(1, because: "an entry that raised an error did not reach the environment either");
		_logger.Received().WriteError(Arg.Is<string>(message =>
			message.Contains("First.zip") && message.Contains("the archive is corrupt")));
	}

	[Test]
	[Description("Records a system setting the environment did not apply, which the setting stage reports by return value rather than by raising an error.")]
	public void Execute_ShouldRecordAFailure_WhenASystemSettingIsNotApplied() {
		// Arrange
		_environmentManager.GetSettingsFromManifest(ManifestPath).Returns([
			new CreatioManifestSetting { Code = "MaxFileSize", Value = "10" }
		]);
		_sysSettingsManager
			.UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>())
			.Returns(false);

		// Act
		int exitCode = _sut.Execute(Options());

		// Assert
		exitCode.Should().Be(1,
			because: "the manifest named a value the environment did not take, so the run did not deliver it");
		_logger.Received().WriteError(Arg.Is<string>(message =>
			message.Contains("MaxFileSize") && message.Contains("did not update it")));
	}

	[Test]
	[Description("Records a manifest entry whose role does not exist as a failure, so a per-role state that was never written is not reported as applied.")]
	public void Execute_ShouldRecordAFailure_WhenTheManifestNamesAnUnknownRole() {
		// Arrange
		_environmentManager.GetFeaturesFromManifest(ManifestPath).Returns([
			new Feature {
				Code = "Feature1",
				Value = true,
				UserValues = new Dictionary<string, bool> { ["Ghost role"] = false }
			}
		]);
		// Act
		int exitCode = _sut.Execute(Options());

		// Assert
		exitCode.Should().Be(1,
			because: "the manifest named a per-role state that never reached the environment, so it belongs in the report like every other non-delivery — a warning alone leaves the caller answering 0");
		_logger.Received().WriteError(Arg.Is<string>(message =>
			message.Contains("Feature1") && message.Contains("Ghost role")));
	}

	[Test]
	[Description("Uninstalls the remaining applications missing from the manifest after one uninstall is refused, so the removal stage is not abandoned either.")]
	public void Execute_ShouldUninstallTheRemainingApplications_WhenOneUninstallIsRefused() {
		// Arrange
		ArrangeApplications("Kept.zip");
		_sut = BuildSut("Kept.zip", "StaleFirst", "StaleSecond");
		_applicationInstaller.UnInstall(
			Arg.Is<SysInstalledApp>(app => app.Name == "StaleFirst"), Arg.Any<EnvironmentSettings>())
			.Returns(false);

		// Act
		int exitCode = _sut.Execute(Options());

		// Assert
		exitCode.Should().Be(1, because: "one uninstall was refused");
		_applicationInstaller.Received(1).UnInstall(
			Arg.Is<SysInstalledApp>(app => app.Name == "StaleSecond"), Arg.Any<EnvironmentSettings>());
	}

	private static ApplyEnvironmentManifestOptions Options() {
		return new ApplyEnvironmentManifestOptions { ManifestFilePath = ManifestPath };
	}

	private void ArrangeApplications(params string[] zipFileNames) {
		List<SysInstalledApp> apps = [];
		foreach (string zipFileName in zipFileNames) {
			apps.Add(new SysInstalledApp {
				Name = zipFileName, Code = zipFileName, ZipFileName = zipFileName, Aliases = []
			});
		}
		_environmentManager.GetApplicationsFromManifest(ManifestPath).Returns(apps);
		_environmentManager.FindApplicationsInAppHub(ManifestPath).Returns(apps);
		_applicationInstaller.Install(Arg.Any<string>(), Arg.Any<EnvironmentSettings>()).Returns(true);
		_applicationInstaller.UnInstall(Arg.Any<SysInstalledApp>(), Arg.Any<EnvironmentSettings>()).Returns(true);
	}

	private void RefuseInstall(string zipFileName) {
		_applicationInstaller.Install(zipFileName, Arg.Any<EnvironmentSettings>()).Returns(false);
	}

	private void ThrowOnInstall(string zipFileName, string reason) {
		_applicationInstaller
			.When(installer => installer.Install(zipFileName, Arg.Any<EnvironmentSettings>()))
			.Do(_ => throw new InvalidOperationException(reason));
	}

	private void ArrangeWebServices(params string[] names) {
		List<CreatioManifestWebService> webServices = [];
		foreach (string name in names) {
			webServices.Add(new CreatioManifestWebService { Name = name, Url = $"https://example.com/{name}" });
		}
		_environmentManager.GetWebServicesFromManifest(ManifestPath).Returns(webServices);
	}
}
