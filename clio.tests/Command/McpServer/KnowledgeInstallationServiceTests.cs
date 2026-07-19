using System;
using System.Collections.Generic;
using System.IO;
using Clio.Command.McpServer.Knowledge;
using Clio.UserEnvironment;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class KnowledgeInstallationServiceTests {
	private IKnowledgeInstallationStore _store = null!;
	private IKnowledgeBundlePackageClient _packageClient = null!;
	private IKnowledgeBundleRuntime _runtime = null!;
	private ISettingsRepository _settingsRepository = null!;
	private ServiceProvider _container = null!;
	private IKnowledgeInstallationService _service = null!;

	[SetUp]
	public void SetUp() {
		_store = Substitute.For<IKnowledgeInstallationStore>();
		_packageClient = Substitute.For<IKnowledgeBundlePackageClient>();
		_runtime = Substitute.For<IKnowledgeBundleRuntime>();
		_settingsRepository = Substitute.For<ISettingsRepository>();
		string clioDirectory = Path.Combine(Path.GetTempPath(), "clio");
		_settingsRepository.AppSettingsFilePath.Returns(Path.Combine(clioDirectory, "appsettings.json"));
		_store.GetRootPath().Returns(Path.Combine(clioDirectory, "knowledge"));
		ServiceCollection services = new();
		services.AddSingleton(_store);
		services.AddSingleton(_packageClient);
		services.AddSingleton(_runtime);
		services.AddSingleton(_settingsRepository);
		services.AddSingleton<IKnowledgeInstallationService, KnowledgeInstallationService>();
		_container = services.BuildServiceProvider();
		_service = _container.GetRequiredService<IKnowledgeInstallationService>();
	}

	[TearDown]
	public void TearDown() {
		_container.Dispose();
	}

	[Test]
	[Description("Downloads, validates, and publishes the latest stable package when no knowledge installation exists.")]
	public void Install_ShouldValidateBeforePublishing_WhenCacheIsMissing() {
		// Arrange
		byte[] bundle = [1, 2, 3];
		_store.ReadCurrent(out Arg.Any<string?>()).Returns((KnowledgeCurrentState?)null);
		_packageClient.GetConfiguration().Returns(new KnowledgeBundlePackageConfiguration("feed", "Clio.Knowledge"));
		_packageClient.DownloadNext(
			Arg.Any<IReadOnlySet<string>>(), null, null, null, null, Arg.Any<int?>())
			.Returns(new KnowledgeBundlePackageDownloadResult(
				KnowledgeBundlePackageDownloadStatus.Downloaded, "1.0.0", bundle));
		_runtime.Validate(Arg.Any<Stream>(), "1.0.0").Returns(new KnowledgeBundleValidationResult(
			KnowledgeBundleActivationStatus.Activated, KnowledgeBundleRejectionCode.None, 10, null));
		_store.Publish("Clio.Knowledge", "1.0.0", 10, "feed", bundle, false, null)
			.Returns(new KnowledgeInstallationResult(
				KnowledgeInstallationStatus.Installed, "installed", "1.0.0", "C:\\clio\\knowledge"));

		// Act
		KnowledgeInstallationResult result = _service.Install();

		// Assert
		result.Status.Should().Be(KnowledgeInstallationStatus.Installed,
			because: "a verified first package should be persisted as the active installation");
		_runtime.Received(1).Validate(Arg.Any<Stream>(), "1.0.0");
		_store.Received(1).Publish("Clio.Knowledge", "1.0.0", 10, "feed", bundle, false, null);
	}

	[Test]
	[Description("Refuses to publish a downloaded package when bundle verification fails.")]
	public void Install_ShouldRejectCandidate_WhenValidationFails() {
		// Arrange
		_store.ReadCurrent(out Arg.Any<string?>()).Returns((KnowledgeCurrentState?)null);
		_packageClient.GetConfiguration().Returns(new KnowledgeBundlePackageConfiguration("feed", "Clio.Knowledge"));
		_packageClient.DownloadNext(
			Arg.Any<IReadOnlySet<string>>(),
			Arg.Is<string?>(value => value == null),
			Arg.Is<string?>(_ => true),
			Arg.Is<string?>(_ => true),
			Arg.Is<string?>(_ => true),
			Arg.Any<int?>())
			.Returns(
				new KnowledgeBundlePackageDownloadResult(
					KnowledgeBundlePackageDownloadStatus.Downloaded, "1.0.0", [1, 2, 3]),
				new KnowledgeBundlePackageDownloadResult(
					KnowledgeBundlePackageDownloadStatus.NoCandidate, null, null));
		_runtime.Validate(Arg.Any<Stream>(), "1.0.0").Returns(new KnowledgeBundleValidationResult(
			KnowledgeBundleActivationStatus.Rejected,
			KnowledgeBundleRejectionCode.InvalidSignature,
			null,
			"invalid signature"));

		// Act
		KnowledgeInstallationResult result = _service.Install();

		// Assert
		result.Status.Should().Be(KnowledgeInstallationStatus.Rejected,
			because: "untrusted bytes must not reach the persistent installation store");
		_store.DidNotReceive().Publish(
			Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<bool>(),
			Arg.Any<KnowledgeVersionPointer?>());
	}

	[Test]
	[Description("Continues in descending order when the newest stable package is invalid and a lower package is compatible.")]
	public void Install_ShouldPublishLowerCompatiblePackage_WhenNewestPackageIsRejected() {
		// Arrange
		byte[] validBundle = [4, 5, 6];
		string root = _store.GetRootPath();
		_store.ReadCurrent(out Arg.Any<string?>()).Returns((KnowledgeCurrentState?)null);
		_packageClient.GetConfiguration().Returns(new KnowledgeBundlePackageConfiguration("feed", "Clio.Knowledge"));
		_packageClient.DownloadNext(
			Arg.Any<IReadOnlySet<string>>(),
			Arg.Is<string?>(value => value == null),
			Arg.Is<string?>(_ => true),
			Arg.Is<string?>(_ => true),
			Arg.Is<string?>(_ => true),
			Arg.Any<int?>())
			.Returns(
				new KnowledgeBundlePackageDownloadResult(
					KnowledgeBundlePackageDownloadStatus.Rejected, "2.0.0", null, "catalog"),
				new KnowledgeBundlePackageDownloadResult(
					KnowledgeBundlePackageDownloadStatus.Downloaded, "1.0.0", validBundle, "catalog"));
		_runtime.Validate(Arg.Any<Stream>(), "1.0.0").Returns(new KnowledgeBundleValidationResult(
			KnowledgeBundleActivationStatus.Activated, KnowledgeBundleRejectionCode.None, 10, null));
		_store.Publish("Clio.Knowledge", "1.0.0", 10, "feed", validBundle, false, null)
			.Returns(new KnowledgeInstallationResult(
				KnowledgeInstallationStatus.Installed, "installed", "1.0.0", root));

		// Act
		KnowledgeInstallationResult result = _service.Install();

		// Assert
		result.Status.Should().Be(KnowledgeInstallationStatus.Installed,
			because: "one invalid higher package must not block the highest remaining compatible package");
		_packageClient.Received(2).DownloadNext(
			Arg.Any<IReadOnlySet<string>>(),
			Arg.Is<string?>(value => value == null),
			Arg.Is<string?>(_ => true),
			Arg.Is<string?>(_ => true),
			Arg.Is<string?>(_ => true),
			Arg.Any<int?>());
	}

	[Test]
	[Description("Repairs only the active immutable version and never turns install-knowledge into an implicit update.")]
	public void Install_ShouldRepairExactActiveVersion_WhenNewerPackageExists() {
		// Arrange
		KnowledgeCurrentState current = State("1.0.0", 10);
		byte[] repairBundle = [7, 8, 9];
		string root = _store.GetRootPath();
		_store.ReadCurrent(out Arg.Any<string?>()).Returns(current);
		_store.TryReadCandidate(current.Active, out Arg.Any<InstalledKnowledgeCandidate?>(), out Arg.Any<string?>())
			.Returns(callInfo => {
				callInfo[1] = null;
				callInfo[2] = "active bundle is missing";
				return false;
			});
		_packageClient.GetConfiguration().Returns(new KnowledgeBundlePackageConfiguration("feed", "Clio.Knowledge"));
		_packageClient.DownloadNext(
			Arg.Any<IReadOnlySet<string>>(),
			Arg.Is<string?>(value => value == null),
			Arg.Is<string?>(_ => true),
			Arg.Is<string?>(_ => true),
			Arg.Is<string?>(_ => true),
			Arg.Any<int?>())
			.Returns(
				new KnowledgeBundlePackageDownloadResult(
					KnowledgeBundlePackageDownloadStatus.Downloaded, "2.0.0", [2], "catalog"),
				new KnowledgeBundlePackageDownloadResult(
					KnowledgeBundlePackageDownloadStatus.Downloaded, "1.0.0", repairBundle, "catalog"));
		_runtime.Validate(Arg.Any<Stream>(), "1.0.0").Returns(new KnowledgeBundleValidationResult(
			KnowledgeBundleActivationStatus.Activated, KnowledgeBundleRejectionCode.None, 10, null));
		_store.Publish("Clio.Knowledge", "1.0.0", 10, "feed", repairBundle, true, current.Active)
			.Returns(new KnowledgeInstallationResult(
				KnowledgeInstallationStatus.Updated, "repaired", "1.0.0", root));

		// Act
		KnowledgeInstallationResult result = _service.Install();

		// Assert
		result.PackageVersion.Should().Be("1.0.0",
			because: "install-knowledge must repair the active version instead of activating a newer package");
		_runtime.DidNotReceive().Validate(Arg.Any<Stream>(), "2.0.0");
	}

	[Test]
	[Description("Reports an unknown update state instead of claiming up-to-date when the remote catalog is unavailable.")]
	public void Update_ShouldReturnUnavailable_WhenCatalogCannotBeChecked() {
		// Arrange
		KnowledgeCurrentState current = State("1.0.0", 10);
		_store.ReadCurrent(out Arg.Any<string?>()).Returns(current);
		ConfigureValidCurrent(current);
		_packageClient.GetCatalog().Returns(new KnowledgeBundlePackageCatalogResult(
			false, null, "feed timed out"));

		// Act
		KnowledgeInstallationResult result = _service.Update();

		// Assert
		result.Status.Should().Be(KnowledgeInstallationStatus.Unavailable,
			because: "network failure cannot prove that the installed version is current");
		result.Message.Should().Contain("feed timed out",
			because: "operators need the actual update-check diagnostic");
	}

	[Test]
	[Description("Returns up-to-date without downloading when the catalog has no version newer than the active installation.")]
	public void Update_ShouldNotDownload_WhenActiveVersionIsLatest() {
		// Arrange
		KnowledgeCurrentState current = State("1.0.0", 10);
		_store.ReadCurrent(out Arg.Any<string?>()).Returns(current);
		ConfigureValidCurrent(current);
		_packageClient.GetCatalog().Returns(new KnowledgeBundlePackageCatalogResult(true, "1.0.0"));

		// Act
		KnowledgeInstallationResult result = _service.Update();

		// Assert
		result.Status.Should().Be(KnowledgeInstallationStatus.UpToDate,
			because: "the remote catalog confirms there is no strictly newer stable package");
		_packageClient.DidNotReceive().DownloadNext(
			Arg.Any<IReadOnlySet<string>>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(),
			Arg.Any<int?>());
	}

	[Test]
	[Description("Reports the visible settings path, installed content directory, validation status, and remote update availability.")]
	public void GetInfo_ShouldDescribeValidInstallation_AndAvailableUpdate() {
		// Arrange
		KnowledgeCurrentState current = State("1.0.0", 10);
		_store.ReadCurrent(out Arg.Any<string?>()).Returns(current);
		InstalledKnowledgeCandidate candidate = new(
			current.Active,
			Path.Combine(_store.GetRootPath(), "versions", "1.0.0", "bundle.zip"),
			[1, 2, 3]);
		_store.TryReadCandidate(current.Active, out Arg.Any<InstalledKnowledgeCandidate?>(), out Arg.Any<string?>())
			.Returns(callInfo => {
				callInfo[1] = candidate;
				callInfo[2] = null;
				return true;
			});
		_runtime.Validate(Arg.Any<Stream>(), "1.0.0").Returns(new KnowledgeBundleValidationResult(
			KnowledgeBundleActivationStatus.Activated, KnowledgeBundleRejectionCode.None, 10, null));
		_store.ReadActiveMetadata(current, out Arg.Any<string?>()).Returns(callInfo => {
			callInfo[1] = null;
			return new KnowledgeInstallMetadata(
				1, "Clio.Knowledge", "1.0.0", 10, "feed", new string('a', 64), DateTimeOffset.UtcNow);
		});
		_store.TryValidateInstallation(current, out Arg.Any<string?>()).Returns(callInfo => {
			callInfo[1] = null;
			return true;
		});
		_packageClient.GetCatalog().Returns(new KnowledgeBundlePackageCatalogResult(true, "1.1.0"));

		// Act
		KnowledgeInstallationInfo info = _service.GetInfo(checkUpdates: true);

		// Assert
		info.SettingsFilePath.Should().Be(_settingsRepository.AppSettingsFilePath,
			because: "operators must see which non-hidden configuration file owns the path");
		info.ActiveContentPath.Should().Be(Path.GetDirectoryName(candidate.BundlePath),
			because: "agents need the actual extracted content directory");
		info.IsValid.Should().BeTrue(because: "the archive digest, manifest, and sequence all verified");
		info.UpdateAvailability.Should().Be(KnowledgeUpdateAvailability.Available,
			because: "the catalog contains a strictly newer stable package");
	}

	private void ConfigureValidCurrent(KnowledgeCurrentState current) {
		InstalledKnowledgeCandidate candidate = new(
			current.Active,
			Path.Combine(_store.GetRootPath(), "versions", current.Active.PackageVersion, "bundle.zip"),
			[1, 2, 3]);
		_store.TryReadCandidate(current.Active, out Arg.Any<InstalledKnowledgeCandidate?>(), out Arg.Any<string?>())
			.Returns(callInfo => {
				callInfo[1] = candidate;
				callInfo[2] = null;
				return true;
			});
		_runtime.Validate(Arg.Any<Stream>(), current.Active.PackageVersion).Returns(new KnowledgeBundleValidationResult(
			KnowledgeBundleActivationStatus.Activated,
			KnowledgeBundleRejectionCode.None,
			current.Active.Sequence,
			null));
		_store.ReadActiveMetadata(current, out Arg.Any<string?>()).Returns(callInfo => {
			callInfo[1] = null;
			return new KnowledgeInstallMetadata(
				1,
				"Clio.Knowledge",
				current.Active.PackageVersion,
				current.Active.Sequence,
				"feed",
				current.Active.BundleDigest,
				DateTimeOffset.UtcNow);
		});
		_store.TryValidateInstallation(current, out Arg.Any<string?>()).Returns(callInfo => {
			callInfo[1] = null;
			return true;
		});
	}

	private static KnowledgeCurrentState State(string version, ulong sequence) => new(
		1,
		new KnowledgeVersionPointer(
			version,
			sequence,
			$"versions/{version}",
			new string('a', 64),
			DateTimeOffset.UtcNow),
		null);
}
