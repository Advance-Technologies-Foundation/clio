using System;
using System.IO;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Text;
using Clio.Command.McpServer.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class KnowledgeInstallationStoreTests {
	private string _root = null!;
	private ServiceProvider _container = null!;
	private IKnowledgeInstallationStore _store = null!;

	[SetUp]
	public void SetUp() {
		_root = Path.Combine(Path.GetTempPath(), $"clio-knowledge-store-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_root);
		IKnowledgeRootPathProvider rootProvider = Substitute.For<IKnowledgeRootPathProvider>();
		rootProvider.GetOrCreateRoot().Returns(_root);
		ServiceCollection services = new();
		services.AddSingleton(rootProvider);
		services.AddSingleton<IFileSystem, FileSystem>();
		services.AddSingleton(new KnowledgeInstallationStoreOptions(5_000));
		services.AddSingleton<IKnowledgeInstallationStore, KnowledgeInstallationStore>();
		_container = services.BuildServiceProvider();
		_store = _container.GetRequiredService<IKnowledgeInstallationStore>();
	}

	[TearDown]
	public void TearDown() {
		_container.Dispose();
		if (Directory.Exists(_root)) {
			Directory.Delete(_root, recursive: true);
		}
	}

	[Test]
	[Description("Publishes a verified bundle into an immutable version directory and switches the activation marker last.")]
	public void Publish_ShouldPersistBundleMetadataAndExtractedContent() {
		// Arrange
		byte[] bundle = Bundle(("manifest.json", "{}"), ("resources/example.md", "example"));

		// Act
		KnowledgeInstallationResult result = _store.Publish(
			"Clio.Knowledge", "1.0.0", 10, "https://feed.invalid/v3/index.json", bundle, isUpdate: false,
			expectedActive: null);
		KnowledgeCurrentState? current = _store.ReadCurrent(out string? diagnostic);

		// Assert
		result.Status.Should().Be(KnowledgeInstallationStatus.Installed,
			because: "the first validated candidate should become the active disk installation");
		diagnostic.Should().BeNull(because: "the atomically published marker must be readable");
		current!.Active.PackageVersion.Should().Be("1.0.0",
			because: "the marker must identify the installed immutable package version");
		File.Exists(Path.Combine(_root, "versions", "1.0.0", "bundle.zip")).Should().BeTrue(
			because: "the exact verified archive is retained for runtime revalidation");
		File.ReadAllText(Path.Combine(_root, "versions", "1.0.0", "resources", "example.md"))
			.Should().Be("example", because: "agents need the extracted knowledge content on disk");
		File.Exists(Path.Combine(_root, "versions", "1.0.0", "install.json")).Should().BeTrue(
			because: "installation provenance must be inspectable without opening the bundle");
	}

	[Test]
	[Description("Keeps one previous version while an update atomically advances the active version and sequence.")]
	public void Publish_ShouldKeepPreviousPointer_WhenUpdateAdvances() {
		// Arrange
		_store.Publish("Clio.Knowledge", "1.0.0", 10, "feed", Bundle(("a.txt", "one")), isUpdate: false,
			expectedActive: null);
		KnowledgeVersionPointer expected = _store.ReadCurrent(out _)!.Active;

		// Act
		KnowledgeInstallationResult result = _store.Publish(
			"Clio.Knowledge", "1.1.0", 20, "feed", Bundle(("a.txt", "two")), isUpdate: true, expected);
		KnowledgeCurrentState? current = _store.ReadCurrent(out string? diagnostic);

		// Assert
		result.Status.Should().Be(KnowledgeInstallationStatus.Updated,
			because: "a strictly newer version and sequence should be publishable");
		diagnostic.Should().BeNull(because: "the updated activation marker must remain readable");
		current!.Active.PackageVersion.Should().Be("1.1.0",
			because: "the update should become active only after its files are complete");
		current.Previous!.PackageVersion.Should().Be("1.0.0",
			because: "a cold process needs one last-known-good fallback");
		Directory.Exists(Path.Combine(_root, "versions", "1.0.0")).Should().BeTrue(
			because: "the previous immutable installation must be retained for fallback");
	}

	[Test]
	[Description("Rejects an update whose signed sequence does not advance even when its package version is newer.")]
	public void Publish_ShouldRejectUpdate_WhenSequenceDoesNotAdvance() {
		// Arrange
		_store.Publish("Clio.Knowledge", "1.0.0", 10, "feed", Bundle(("a.txt", "one")), isUpdate: false,
			expectedActive: null);
		KnowledgeVersionPointer expected = _store.ReadCurrent(out _)!.Active;

		// Act
		KnowledgeInstallationResult result = _store.Publish(
			"Clio.Knowledge", "1.1.0", 10, "feed", Bundle(("a.txt", "two")), isUpdate: true, expected);

		// Assert
		result.Status.Should().Be(KnowledgeInstallationStatus.Rejected,
			because: "a version update must not bypass the signed monotonic sequence guarantee");
		_store.ReadCurrent(out _).Active.PackageVersion.Should().Be("1.0.0",
			because: "a rejected update must not change the last-known-good marker");
	}

	[Test]
	[Description("Rejects different bytes published under an already installed immutable package version.")]
	public void Publish_ShouldRejectSameVersion_WhenContentDiffers() {
		// Arrange
		_store.Publish("Clio.Knowledge", "1.0.0", 10, "feed", Bundle(("a.txt", "one")), isUpdate: false,
			expectedActive: null);
		KnowledgeVersionPointer expected = _store.ReadCurrent(out _)!.Active;

		// Act
		KnowledgeInstallationResult result = _store.Publish(
			"Clio.Knowledge", "1.0.0", 10, "feed", Bundle(("a.txt", "different")), isUpdate: true, expected);

		// Assert
		result.Status.Should().Be(KnowledgeInstallationStatus.Rejected,
			because: "one NuGet version must never identify two different knowledge archives");
	}

	[Test]
	[Description("Deletes only managed knowledge artifacts after explicit confirmation and preserves unrelated files in the configured root.")]
	public void Delete_ShouldPreserveUnmanagedFiles_AndRequireConfirmation() {
		// Arrange
		_store.Publish("Clio.Knowledge", "1.0.0", 10, "feed", Bundle(("a.txt", "one")), isUpdate: false,
			expectedActive: null);
		string unmanaged = Path.Combine(_root, "owner-note.txt");
		File.WriteAllText(unmanaged, "keep");

		// Act
		KnowledgeInstallationResult refused = _store.Delete(confirmed: false);
		KnowledgeInstallationResult deleted = _store.Delete(confirmed: true);

		// Assert
		refused.Status.Should().Be(KnowledgeInstallationStatus.ConfirmationRequired,
			because: "knowledge removal must fail closed without explicit confirmation");
		deleted.Status.Should().Be(KnowledgeInstallationStatus.Deleted,
			because: "confirmed removal should delete the managed installation");
		File.Exists(unmanaged).Should().BeTrue(
			because: "delete-knowledge must never remove files it does not own");
		File.Exists(Path.Combine(_root, "current.json")).Should().BeFalse(
			because: "the active marker belongs to the managed knowledge installation");
		Directory.Exists(_root).Should().BeTrue(
			because: "the configured root and visible appsettings pointer remain after deletion");
	}

	[Test]
	[Description("Does not delete generic child directories when the configured root has never been claimed by Clio.")]
	public void Delete_ShouldNotMutateUnownedRoot() {
		// Arrange
		string unrelated = Path.Combine(_root, "versions", "user-data.txt");
		Directory.CreateDirectory(Path.GetDirectoryName(unrelated)!);
		File.WriteAllText(unrelated, "keep");

		// Act
		KnowledgeInstallationResult result = _store.Delete(confirmed: true);

		// Assert
		result.Status.Should().Be(KnowledgeInstallationStatus.NotInstalled,
			because: "a missing ownership marker means this directory is not a Clio knowledge store");
		File.ReadAllText(unrelated).Should().Be("keep",
			because: "generic directory names must never authorize recursive deletion");
	}

	[Test]
	[Description("Returns a safe diagnostic rather than throwing when the ownership sentinel is tampered.")]
	public void ReadCurrent_ShouldReturnDiagnostic_WhenOwnershipMarkerIsInvalid() {
		// Arrange
		_store.Publish("Clio.Knowledge", "1.0.0", 10, "feed", Bundle(("a.txt", "one")), isUpdate: false,
			expectedActive: null);
		File.WriteAllText(Path.Combine(_root, ".clio-knowledge-root"), "tampered");

		// Act
		KnowledgeCurrentState? state = _store.ReadCurrent(out string? diagnostic);

		// Assert
		state.Should().BeNull(
			because: "an invalid ownership boundary must fail closed instead of exposing an active pointer");
		diagnostic.Should().Contain("could not be read",
			because: "MCP and info-knowledge need a typed operator-safe failure reason");
	}

	[Test]
	[Description("Returns a safe diagnostic rather than throwing when the configured knowledge root cannot be resolved.")]
	public void ReadCurrent_ShouldReturnDiagnostic_WhenRootResolutionFails() {
		// Arrange
		IKnowledgeRootPathProvider rootProvider = Substitute.For<IKnowledgeRootPathProvider>();
		rootProvider.GetOrCreateRoot().Returns(_ => throw new InvalidOperationException("invalid configured root"));
		using ServiceProvider provider = new ServiceCollection()
			.AddSingleton(rootProvider)
			.AddSingleton<IFileSystem, FileSystem>()
			.AddSingleton(new KnowledgeInstallationStoreOptions(5_000))
			.AddSingleton<IKnowledgeInstallationStore, KnowledgeInstallationStore>()
			.BuildServiceProvider();
		IKnowledgeInstallationStore store = provider.GetRequiredService<IKnowledgeInstallationStore>();

		// Act
		KnowledgeCurrentState? state = store.ReadCurrent(out string? diagnostic);

		// Assert
		state.Should().BeNull(
			because: "an invalid visible root setting must make knowledge unavailable without crashing MCP");
		diagnostic.Should().Contain("invalid configured root",
			because: "operators need a typed diagnostic that identifies the invalid root configuration");
	}

	[Test]
	[Description("Completes deletion after a prior process moved managed content into a validated quarantine directory.")]
	public void Delete_ShouldRemoveAbandonedManagedQuarantine_WhenRetryingInterruptedDelete() {
		// Arrange
		_store.Publish("Clio.Knowledge", "1.0.0", 10, "feed", Bundle(("a.txt", "one")), isUpdate: false,
			expectedActive: null);
		string marker = Path.Combine(_root, "current.json");
		string deletingMarker = Path.Combine(_root, ".current.deleting.json");
		File.Move(marker, deletingMarker);
		string versions = Path.Combine(_root, "versions");
		string quarantine = Path.Combine(_root, $".versions.delete-{Guid.NewGuid():N}");
		Directory.Move(versions, quarantine);
		string unrelated = Path.Combine(_root, ".versions.delete-not-owned");
		Directory.CreateDirectory(unrelated);
		File.WriteAllText(Path.Combine(unrelated, "keep.txt"), "keep");

		// Act
		KnowledgeInstallationResult result = _store.Delete(confirmed: true);

		// Assert
		result.Status.Should().Be(KnowledgeInstallationStatus.Deleted,
			because: "a retry must finish an interrupted deletion before withdrawing its deleting marker");
		Directory.Exists(quarantine).Should().BeFalse(
			because: "validated Clio quarantine directories remain managed content until deletion completes");
		File.Exists(deletingMarker).Should().BeFalse(
			because: "the deleting marker can be removed only after every managed quarantine is gone");
		File.ReadAllText(Path.Combine(unrelated, "keep.txt")).Should().Be("keep",
			because: "lookalike directories outside the exact owned naming contract must be preserved");
	}

	[Test]
	[Description("Atomically repairs an interrupted ownership marker when it is the only root entry.")]
	public void Publish_ShouldRepairInterruptedOwnershipMarker_WhenRootHasNoOtherContent() {
		// Arrange
		File.WriteAllBytes(Path.Combine(_root, ".clio-knowledge-root"), []);

		// Act
		KnowledgeInstallationResult result = _store.Publish(
			"Clio.Knowledge", "1.0.0", 10, "feed", Bundle(("a.txt", "one")), isUpdate: false,
			expectedActive: null);

		// Assert
		result.Status.Should().Be(KnowledgeInstallationStatus.Installed,
			because: "a crash during first ownership publication must not permanently wedge an otherwise empty store");
		File.ReadAllText(Path.Combine(_root, ".clio-knowledge-root")).Should().Be("clio-knowledge-store-v1\n",
			because: "recovery must atomically restore the canonical ownership marker");
	}

	[Test]
	[Description("Rejects publication when the active marker changed after an update began.")]
	public void Publish_ShouldRejectUpdate_WhenExpectedActiveWasDeleted() {
		// Arrange
		_store.Publish("Clio.Knowledge", "1.0.0", 10, "feed", Bundle(("a.txt", "one")), isUpdate: false,
			expectedActive: null);
		KnowledgeVersionPointer expected = _store.ReadCurrent(out _)!.Active;
		_store.Delete(confirmed: true);

		// Act
		KnowledgeInstallationResult result = _store.Publish(
			"Clio.Knowledge", "1.1.0", 20, "feed", Bundle(("a.txt", "two")), isUpdate: true, expected);

		// Assert
		result.Status.Should().Be(KnowledgeInstallationStatus.Failed,
			because: "compare-and-swap publication must not resurrect knowledge after deletion");
		_store.ReadCurrent(out _).Should().BeNull(
			because: "the completed deletion remains authoritative over the stale update");
	}

	[Test]
	[Description("Repairs extracted files from the verified archive when the active immutable directory is damaged.")]
	public void Publish_ShouldRepairSameVersion_WhenExtractedContentIsDamaged() {
		// Arrange
		byte[] bundle = Bundle(("resources/example.md", "verified"));
		_store.Publish("Clio.Knowledge", "1.0.0", 10, "feed", bundle, isUpdate: false, expectedActive: null);
		KnowledgeVersionPointer expected = _store.ReadCurrent(out _)!.Active;
		string extractedFile = Path.Combine(_root, "versions", "1.0.0", "resources", "example.md");
		File.WriteAllText(extractedFile, "tampered");

		// Act
		KnowledgeInstallationResult result = _store.Publish(
			"Clio.Knowledge", "1.0.0", 10, "feed", bundle, isUpdate: true, expected);

		// Assert
		result.Status.Should().Be(KnowledgeInstallationStatus.Updated,
			because: "a verified same-version package should repair a damaged local materialization");
		File.ReadAllText(extractedFile).Should().Be("verified",
			because: "repair must rebuild extracted content solely from the verified archive");
	}

	[Test]
	[Description("Repairs a missing active archive from the same verified immutable package version.")]
	public void Publish_ShouldRepairSameVersion_WhenActiveArchiveIsMissing() {
		// Arrange
		byte[] bundle = Bundle(("resources/example.md", "verified"));
		_store.Publish("Clio.Knowledge", "1.0.0", 10, "feed", bundle, isUpdate: false, expectedActive: null);
		KnowledgeVersionPointer expected = _store.ReadCurrent(out _)!.Active;
		string bundlePath = Path.Combine(_root, "versions", "1.0.0", "bundle.zip");
		File.Delete(bundlePath);

		// Act
		KnowledgeInstallationResult result = _store.Publish(
			"Clio.Knowledge", "1.0.0", 10, "feed", bundle, isUpdate: true, expected);

		// Assert
		result.Status.Should().Be(KnowledgeInstallationStatus.Updated,
			because: "a missing local archive must not make a valid immutable package permanently unrepairable");
		File.ReadAllBytes(bundlePath).Should().Equal(bundle,
			because: "repair must restore the exact verified archive bytes");
	}

	[Test]
	[Description("Rejects a managed directory redirected through a symbolic link before writing or recursive cleanup.")]
	public void Publish_ShouldRejectManagedDirectory_WhenItIsSymbolicLink() {
		// Arrange
		_store.Publish("Clio.Knowledge", "1.0.0", 10, "feed", Bundle(("a.txt", "one")), isUpdate: false,
			expectedActive: null);
		KnowledgeVersionPointer expected = _store.ReadCurrent(out _)!.Active;
		string staging = Path.Combine(_root, "staging");
		string external = Path.Combine(Path.GetTempPath(), $"clio-knowledge-link-target-{Guid.NewGuid():N}");
		Directory.Delete(staging, recursive: true);
		Directory.CreateDirectory(external);
		string externalFile = Path.Combine(external, "outside.txt");
		File.WriteAllText(externalFile, "keep");
		try {
			try {
				Directory.CreateSymbolicLink(staging, external);
			} catch (Exception exception) when (exception is UnauthorizedAccessException or IOException
					or PlatformNotSupportedException) {
				Assert.Ignore($"Symbolic links are unavailable on this test host: {exception.Message}");
			}

			// Act
			Action act = () => _store.Publish(
				"Clio.Knowledge", "1.1.0", 20, "feed", Bundle(("a.txt", "two")), isUpdate: true, expected);

			// Assert
			act.Should().Throw<InvalidOperationException>(
				because: "managed paths redirected outside the owned root must fail closed");
			File.ReadAllText(externalFile).Should().Be("keep",
				because: "rejecting the link must not mutate its external target");
		} finally {
			if (Directory.Exists(staging)) {
				Directory.Delete(staging);
			}
			if (Directory.Exists(external)) {
				Directory.Delete(external, recursive: true);
			}
		}
	}

	[Test]
	[Description("Rejects a configured knowledge root whose existing ancestor is a symbolic link or junction.")]
	public void Publish_ShouldRejectRoot_WhenAncestorIsSymbolicLink() {
		// Arrange
		string baseDirectory = Path.Combine(Path.GetTempPath(), $"clio-knowledge-link-base-{Guid.NewGuid():N}");
		string targetDirectory = Path.Combine(Path.GetTempPath(), $"clio-knowledge-link-target-{Guid.NewGuid():N}");
		string linkedRoot = Path.Combine(baseDirectory, "knowledge");
		Directory.CreateDirectory(targetDirectory);
		Directory.CreateDirectory(Path.Combine(targetDirectory, "knowledge"));
		try {
			try {
				Directory.CreateSymbolicLink(baseDirectory, targetDirectory);
			} catch (Exception exception) when (exception is UnauthorizedAccessException or IOException
					or PlatformNotSupportedException) {
				Assert.Ignore($"Symbolic links are unavailable on this test host: {exception.Message}");
			}
			IKnowledgeRootPathProvider rootProvider = Substitute.For<IKnowledgeRootPathProvider>();
			rootProvider.GetOrCreateRoot().Returns(linkedRoot);
			using ServiceProvider provider = new ServiceCollection()
				.AddSingleton(rootProvider)
				.AddSingleton<IFileSystem, FileSystem>()
				.AddSingleton(new KnowledgeInstallationStoreOptions(5_000))
				.AddSingleton<IKnowledgeInstallationStore, KnowledgeInstallationStore>()
				.BuildServiceProvider();
			IKnowledgeInstallationStore store = provider.GetRequiredService<IKnowledgeInstallationStore>();

			// Act
			Action act = () => store.Publish(
				"Clio.Knowledge", "1.0.0", 10, "feed", Bundle(("a.txt", "one")), isUpdate: false,
				expectedActive: null);

			// Assert
			act.Should().Throw<InvalidOperationException>(
				because: "logical containment through a redirected ancestor is not physical containment");
		} finally {
			if (Directory.Exists(baseDirectory)) {
				Directory.Delete(baseDirectory);
			}
			if (Directory.Exists(targetDirectory)) {
				Directory.Delete(targetDirectory, recursive: true);
			}
		}
	}

	[Test]
	[Description("Rejects an archive entry that attempts to escape the configured knowledge root.")]
	public void Publish_ShouldRejectArchive_WhenEntryEscapesRoot() {
		// Arrange
		byte[] bundle = Bundle(("../escaped.txt", "unsafe"));

		// Act
		Action act = () => _store.Publish(
			"Clio.Knowledge", "1.0.0", 10, "feed", bundle, isUpdate: false, expectedActive: null);

		// Assert
		act.Should().Throw<InvalidDataException>(
			because: "package-controlled paths must remain under the staging directory");
		File.Exists(Path.Combine(_root, "escaped.txt")).Should().BeFalse(
			because: "a rejected archive must not write outside its immutable version directory");
	}

	private static byte[] Bundle(params (string Path, string Text)[] entries) {
		using MemoryStream output = new();
		using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true)) {
			foreach ((string path, string text) in entries) {
				ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
				using Stream stream = entry.Open();
				byte[] bytes = Encoding.UTF8.GetBytes(text);
				stream.Write(bytes);
			}
		}
		return output.ToArray();
	}
}
