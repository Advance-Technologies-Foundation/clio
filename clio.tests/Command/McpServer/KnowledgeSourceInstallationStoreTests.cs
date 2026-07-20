using System;
using System.IO;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using Clio.Command.McpServer.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class KnowledgeSourceInstallationStoreTests {
	private string _root = null!;
	private ServiceProvider _container = null!;
	private IKnowledgeSourceInstallationStore _store = null!;

	[SetUp]
	public void SetUp() {
		_root = Path.Combine(Path.GetTempPath(), $"clio-knowledge-sources-{Guid.NewGuid():N}");
		IKnowledgeRootPathProvider rootProvider = Substitute.For<IKnowledgeRootPathProvider>();
		rootProvider.GetOrCreateRoot().Returns(_root);
		ServiceCollection services = new();
		services.AddSingleton(rootProvider);
		services.AddSingleton<IFileSystem, FileSystem>();
		services.AddSingleton(new KnowledgeInstallationStoreOptions(LockTimeoutMilliseconds: 5_000));
		services.AddSingleton<IKnowledgeSourceInstallationStore, KnowledgeSourceInstallationStore>();
		_container = services.BuildServiceProvider();
		_store = _container.GetRequiredService<IKnowledgeSourceInstallationStore>();
	}

	[TearDown]
	public void TearDown() {
		_container.Dispose();
		if (Directory.Exists(_root)) {
			Directory.Delete(_root, recursive: true);
		}
	}

	[Test]
	[Description("A non-blocking source read lease reports contention instead of waiting behind a mutation.")]
	public void TryExecuteWithSourceMutationLock_ShouldReturnFalse_WhenSourceIsAlreadyLocked() {
		// Arrange
		bool acquired = true;

		// Act
		_store.ExecuteWithSourceMutationLock("partner", () => {
			acquired = _store.TryExecuteWithSourceMutationLock("partner", () => { });
			return true;
		});

		// Assert
		acquired.Should().BeFalse(
			because: "guidance reads must keep serving last-known-good content instead of waiting behind an update");
	}

	[Test]
	[Description("Git mutation locking revalidates an existing source root before returning its repository path.")]
	public void ExecuteWithSourceMutationLock_ShouldRejectExistingSourceRoot_WhenOwnershipMarkerChanges() {
		// Arrange
		string repositoryPath = _store.GetGitRepositoryPath("partner", createSourceRoot: true);
		string sourceRoot = Path.GetDirectoryName(repositoryPath)!;
		File.WriteAllText(Path.Combine(sourceRoot, ".clio-knowledge-source"), "different-alias\n");

		// Act
		Action act = () => _store.ExecuteWithSourceMutationLock("partner", () => true);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			because: "an existing source root must retain its ownership marker before any Git mutation can run");
	}

	[Test]
	[Description("Git repository migration preserves an installed checkout when a stable library adopts a canonical source alias.")]
	public void TryMigrateGitRepository_ShouldMoveCheckout_ToCanonicalAlias() {
		// Arrange
		string previousRepository = _store.GetGitRepositoryPath("creatio-poc", createSourceRoot: true);
		Directory.CreateDirectory(Path.Combine(previousRepository, ".git"));
		File.WriteAllText(Path.Combine(previousRepository, "bundle-source.json"), "{}");

		// Act
		bool migrated = _store.TryMigrateGitRepository("creatio-poc", "creatio-curated");
		string canonicalRepository = _store.GetGitRepositoryPath("creatio-curated", createSourceRoot: false);

		// Assert
		migrated.Should().BeTrue(
			because: "an already-installed checkout should be reused instead of requiring another network clone");
		Directory.Exists(Path.Combine(canonicalRepository, ".git")).Should().BeTrue(
			because: "the canonical source must own the migrated Git checkout");
		File.Exists(Path.Combine(canonicalRepository, "bundle-source.json")).Should().BeTrue(
			because: "all repository content must move with the checkout");
		Directory.Exists(previousRepository).Should().BeFalse(
			because: "the previous alias must not retain a duplicate repository checkout");
	}

	[Test]
	[Description("Startup alias migration reports lock contention without waiting behind another source operation.")]
	public void TryMigrateGitRepository_ShouldReturnFalse_WhenLegacySourceIsLocked() {
		// Arrange
		string previousRepository = _store.GetGitRepositoryPath("creatio-poc", createSourceRoot: true);
		Directory.CreateDirectory(Path.Combine(previousRepository, ".git"));
		bool migrated = true;

		// Act
		_store.ExecuteWithSourceMutationLock("creatio-poc", () => {
			migrated = _store.TryMigrateGitRepository("creatio-poc", "creatio-curated");
			return true;
		});

		// Assert
		migrated.Should().BeFalse(
			because: "MCP preparation must never delay its protocol handshake behind an existing cache mutation");
		Directory.Exists(previousRepository).Should().BeTrue(
			because: "the background phase must be able to retry the untouched legacy checkout");
	}

	[Test]
	[Description("Each source publishes and reads an independent active generation without affecting another source.")]
	public void Publish_ShouldKeepIndependentCandidates_WhenSourcesDiffer() {
		// Arrange
		byte[] alphaBundle = Bundle(("resources/alpha.md", "alpha"));
		byte[] betaBundle = Bundle(("resources/beta.md", "beta"));

		// Act
		KnowledgeInstallationResult alphaResult = Publish("alpha", "com.example.alpha", 1, alphaBundle);
		KnowledgeInstallationResult betaResult = Publish("beta", "com.example.beta", 7, betaBundle);
		KnowledgeSourceCurrentState? alphaState = _store.ReadCurrent("alpha", out string? alphaDiagnostic);
		KnowledgeSourceCurrentState? betaState = _store.ReadCurrent("beta", out string? betaDiagnostic);
		bool alphaRead = _store.TryReadCandidate(
			"alpha", alphaState!.Active, out InstalledKnowledgeSourceCandidate? alpha, out string? alphaReadDiagnostic);
		bool betaRead = _store.TryReadCandidate(
			"beta", betaState!.Active, out InstalledKnowledgeSourceCandidate? beta, out string? betaReadDiagnostic);

		// Assert
		alphaResult.Status.Should().Be(KnowledgeInstallationStatus.Installed,
			because: "the first source must publish independently");
		betaResult.Status.Should().Be(KnowledgeInstallationStatus.Installed,
			because: "a second source must not collide with the first source's store");
		alphaDiagnostic.Should().BeNull(because: "alpha's marker must remain readable after beta publishes");
		betaDiagnostic.Should().BeNull(because: "beta's marker must remain readable after alpha publishes");
		alphaRead.Should().BeTrue(because: alphaReadDiagnostic ?? "alpha's digest-bound candidate must be readable");
		betaRead.Should().BeTrue(because: betaReadDiagnostic ?? "beta's digest-bound candidate must be readable");
		alpha!.BundleBytes.Should().Equal(alphaBundle,
			because: "alpha must retain only its own immutable archive");
		beta!.BundleBytes.Should().Equal(betaBundle,
			because: "beta must retain only its own immutable archive");
		alpha.ContentRoot.Should().NotBe(beta.ContentRoot,
			because: "each source needs a separate activation and generation directory");
	}

	[Test]
	[Description("A source rejects lower sequences and same-sequence content changes without changing its active digest.")]
	public void Publish_ShouldPreserveActiveDigest_WhenSequenceDoesNotAdvanceMonotonically() {
		// Arrange
		byte[] activeBundle = Bundle(("article.md", "active"));
		byte[] changedBundle = Bundle(("article.md", "changed"));
		Publish("alpha", "com.example.alpha", 10, activeBundle);
		KnowledgeSourceGenerationPointer expected = _store.ReadCurrent("alpha", out _)!.Active;

		// Act
		KnowledgeInstallationResult rollback = _store.Publish(
			"alpha", "com.example.alpha", "1.0.0", 9, "nuget", "https://feed.invalid/v3/index.json",
			"0.9.0", changedBundle, isUpdate: true, expected);
		KnowledgeInstallationResult digestRewrite = _store.Publish(
			"alpha", "com.example.alpha", "1.0.1", 10, "nuget", "https://feed.invalid/v3/index.json",
			"1.0.1", changedBundle, isUpdate: true, expected);
		KnowledgeSourceCurrentState? current = _store.ReadCurrent("alpha", out string? diagnostic);

		// Assert
		rollback.Status.Should().Be(KnowledgeInstallationStatus.Rejected,
			because: "a source generation sequence must never move backward");
		digestRewrite.Status.Should().Be(KnowledgeInstallationStatus.Rejected,
			because: "the same source sequence cannot be rebound to different bytes");
		diagnostic.Should().BeNull(because: "rejected updates must leave the activation marker intact");
		current!.Active.Should().Be(expected,
			because: "neither a rollback nor a same-sequence digest change may replace active content");
		current.Previous.Should().BeNull(
			because: "rejected candidates must not become rollback generations");
	}

	[Test]
	[Description("Retains the library sequence high-water mark after cache deletion and alias changes so signed rollbacks cannot be reinstalled as fresh content.")]
	public void Publish_ShouldRejectLibraryRollback_WhenSourceCacheWasDeletedAndAliasChanged() {
		// Arrange
		byte[] acceptedBundle = Bundle(("article.md", "accepted"));
		byte[] rollbackBundle = Bundle(("article.md", "rollback"));
		KnowledgeInstallationResult accepted = Publish(
			"original", "com.example.shared-library", 20, acceptedBundle);
		KnowledgeInstallationResult deleted = _store.Delete("original", confirmed: true);

		// Act
		KnowledgeInstallationResult rollback = Publish(
			"replacement", "com.example.shared-library", 10, rollbackBundle);
		KnowledgeSourceCurrentState? replacement = _store.ReadCurrent("replacement", out string? diagnostic);

		// Assert
		accepted.Status.Should().Be(KnowledgeInstallationStatus.Installed,
			because: "the trusted library's initial high-water sequence must be established");
		deleted.Status.Should().Be(KnowledgeInstallationStatus.Deleted,
			because: "cache deletion should remove content without erasing replay protection");
		rollback.Status.Should().Be(KnowledgeInstallationStatus.Rejected,
			because: "monotonic signed sequence is scoped to library identity rather than a deletable alias cache");
		replacement.Should().BeNull(
			because: "a rejected rollback must not publish an activation marker under a new alias");
		diagnostic.Should().BeNull(
			because: "absence after a rejected fresh install is not storage corruption");
		File.Exists(Path.Combine(_root, "sources", ".history",
			GetLibraryHistoryFileName("com.example.shared-library"))).Should().BeTrue(
			because: "the non-deletable library replay marker must survive source-cache deletion");
	}

	[Test]
	[Description("Deleting one confirmed source removes only its owned subtree and retains other source and root content.")]
	public void Delete_ShouldRemainContainedToSelectedSource_WhenOtherContentExists() {
		// Arrange
		Publish("alpha", "com.example.alpha", 1, Bundle(("alpha.md", "alpha")));
		Publish("beta", "com.example.beta", 1, Bundle(("beta.md", "beta")));
		string sentinel = Path.Combine(_root, "operator-note.txt");
		File.WriteAllText(sentinel, "retain");

		// Act
		KnowledgeInstallationResult result = _store.Delete("alpha", confirmed: true);
		KnowledgeSourceCurrentState? alpha = _store.ReadCurrent("alpha", out string? alphaDiagnostic);
		KnowledgeSourceCurrentState? beta = _store.ReadCurrent("beta", out string? betaDiagnostic);

		// Assert
		result.Status.Should().Be(KnowledgeInstallationStatus.Deleted,
			because: "explicit confirmation authorizes deletion of exactly the selected source cache");
		alpha.Should().BeNull(because: "the selected source activation marker must be removed");
		alphaDiagnostic.Should().BeNull(
			because: "an absent selected source is a clean not-installed state rather than corruption");
		beta.Should().NotBeNull(because: "deleting alpha must not remove beta's activation marker");
		betaDiagnostic.Should().BeNull(because: "the retained source must remain healthy");
		File.ReadAllText(sentinel).Should().Be("retain",
			because: "source deletion must never recursively delete unrelated root content");
		File.Exists(Path.Combine(_root, ".clio-knowledge-root")).Should().BeTrue(
			because: "the shared owned root must survive a single-source deletion");
	}

	[Test]
	[Description("An explicit repair publishes equal-sequence equal-digest bytes into a new immutable generation and replaces a damaged active generation.")]
	public void Publish_ShouldCreateNewGeneration_WhenExplicitRepairMatchesSequenceAndDigest() {
		// Arrange
		byte[] bundle = Bundle(("article.md", "stable"));
		KnowledgeInstallationResult initial = Publish("alpha", "com.example.alpha", 10, bundle);
		KnowledgeSourceCurrentState before = _store.ReadCurrent("alpha", out string? beforeDiagnostic)!;
		string activeBundlePath = Directory.EnumerateFiles(_root, "bundle.zip", SearchOption.AllDirectories).Single();
		File.WriteAllBytes(activeBundlePath, [0x00]);

		// Act
		KnowledgeInstallationResult repaired = _store.Publish(
			"alpha", "com.example.alpha", "1.0.0", 10, "nuget", "https://feed.invalid/v3/index.json",
			"1.0.10", bundle, isUpdate: true, before.Active, allowRepair: true);
		KnowledgeSourceCurrentState after = _store.ReadCurrent("alpha", out string? afterDiagnostic)!;
		bool readable = _store.TryReadCandidate(
			"alpha", after.Active, out InstalledKnowledgeSourceCandidate? candidate, out string? readDiagnostic);

		// Assert
		initial.Status.Should().Be(KnowledgeInstallationStatus.Installed,
			because: "repair requires an existing accepted generation");
		beforeDiagnostic.Should().BeNull(because: "the initial activation marker must be valid");
		repaired.Status.Should().Be(KnowledgeInstallationStatus.Updated,
			because: "explicit same-content repair is an allowed immutable generation replacement");
		afterDiagnostic.Should().BeNull(because: "repair must publish a valid activation marker");
		after.Active.RelativePath.Should().NotBe(before.Active.RelativePath,
			because: "repair must never rewrite the existing immutable generation in place");
		after.Active.Sequence.Should().Be(before.Active.Sequence,
			because: "repair preserves the signed sequence identity");
		after.Active.BundleDigest.Should().Be(before.Active.BundleDigest,
			because: "repair is permitted only for identical signed content");
		readable.Should().BeTrue(because: readDiagnostic ?? "the new repaired generation must be readable");
		candidate!.BundleBytes.Should().Equal(bundle,
			because: "the repaired active generation must contain the verified original bytes");
	}

	[Test]
	[Description("Recovers an exact immutable generation left after a crash between its final move and activation-marker publication.")]
	public void Publish_ShouldRecoverExactOrphan_WhenCrashPrecedesActivationMarker() {
		// Arrange
		byte[] bundle = Bundle(("article.md", "stable"));
		KnowledgeInstallationResult initial = Publish("alpha", "com.example.alpha", 10, bundle);
		string currentMarker = Directory.EnumerateFiles(_root, "current.json", SearchOption.AllDirectories).Single();
		File.Delete(currentMarker);

		// Act
		KnowledgeInstallationResult recovered = Publish("alpha", "com.example.alpha", 10, bundle);
		KnowledgeSourceCurrentState? current = _store.ReadCurrent("alpha", out string? currentDiagnostic);
		bool readable = _store.TryReadCandidate(
			"alpha", current!.Active, out InstalledKnowledgeSourceCandidate? candidate, out string? readDiagnostic);

		// Assert
		initial.Status.Should().Be(KnowledgeInstallationStatus.Installed,
			because: "the equivalent pre-crash operation must have moved one complete immutable generation");
		recovered.Status.Should().Be(KnowledgeInstallationStatus.Installed,
			because: "an exact unreferenced generation must be rebuilt and activated instead of stranding the source");
		currentDiagnostic.Should().BeNull(
			because: "crash recovery must republish a valid activation marker");
		current.Active.Sequence.Should().Be(10,
			because: "recovery must preserve the accepted signed sequence");
		readable.Should().BeTrue(
			because: readDiagnostic ?? "the recovered immutable generation must remain digest-bound and readable");
		candidate!.BundleBytes.Should().Equal(bundle,
			because: "recovery must never substitute different bytes for the accepted sequence");
		Directory.EnumerateDirectories(
			Path.GetDirectoryName(candidate.ContentRoot)!, "*", SearchOption.TopDirectoryOnly).Should().ContainSingle(
			because: "the exact orphan must be removed before rebuilding rather than duplicated");
	}

	[Test]
	[Description("Recovers an interrupted update when the exact newer high-water generation was moved before activation publication.")]
	public void ReadCurrent_ShouldRecoverExactNewerGeneration_WhenUpdateWasInterruptedBeforeActivation() {
		// Arrange
		byte[] previousBundle = Bundle(("article.md", "previous"));
		byte[] currentBundle = Bundle(("article.md", "current"));
		Publish("alpha", "com.example.alpha", 9, previousBundle);
		KnowledgeSourceCurrentState previous = _store.ReadCurrent("alpha", out string? previousDiagnostic)!;
		KnowledgeInstallationResult advanced = _store.Publish(
			"alpha", "com.example.alpha", "1.0.0", 10, "nuget", "https://feed.invalid/v3/index.json",
			"1.0.10", currentBundle, isUpdate: true, previous.Active);
		KnowledgeSourceCurrentState accepted = _store.ReadCurrent("alpha", out string? acceptedDiagnostic)!;
		File.WriteAllBytes(
			GetCurrentMarkerPath(),
			JsonSerializer.SerializeToUtf8Bytes(
				previous,
				KnowledgeSourceInstallationJsonContext.Default.KnowledgeSourceCurrentState));

		// Act
		KnowledgeSourceCurrentState? recovered = _store.ReadCurrent("alpha", out string? recoveryDiagnostic);
		bool readable = _store.TryReadCandidate(
			"alpha", recovered!.Active, out InstalledKnowledgeSourceCandidate? candidate, out string? readDiagnostic);
		KnowledgeSourceCurrentState? retried = _store.ReadCurrent("alpha", out string? retryDiagnostic);

		// Assert
		previousDiagnostic.Should().BeNull(
			because: "the initial accepted marker must be readable before the high-water sequence advances");
		advanced.Status.Should().Be(KnowledgeInstallationStatus.Updated,
			because: "the interrupted state must contain a complete newer accepted generation");
		acceptedDiagnostic.Should().BeNull(
			because: "an exact marker at the accepted high-water identity must remain readable");
		recovered.Should().NotBeNull(
			because: "an exact immutable generation must allow activation publication to resume safely");
		recoveryDiagnostic.Should().BeNull(
			because: "successful interrupted-publication recovery is a valid current-state read");
		recovered!.Active.Should().BeEquivalentTo(accepted.Active, options => options
			.Excluding(pointer => pointer.ActivatedAtUtc),
			because: "recovery must activate the exact generation accepted by the replay ledger");
		recovered.Previous.Should().Be(previous.Active,
			because: "recovery must preserve the formerly active generation as the rollback candidate");
		readable.Should().BeTrue(
			because: readDiagnostic ?? "the recovered active generation must remain digest-bound and readable");
		candidate!.BundleBytes.Should().Equal(currentBundle,
			because: "recovery must serve the exact newer accepted bundle");
		retried.Should().Be(recovered,
			because: "reading the reconciled marker again must be idempotent");
		retryDiagnostic.Should().BeNull(
			because: "the reconciled marker must satisfy the replay ledger on subsequent reads");
	}

	[TestCase(false, "missing")]
	[TestCase(true, "unexpected content")]
	[Description("Fails closed when interrupted-update recovery cannot validate the exact newer high-water generation.")]
	public void ReadCurrent_ShouldFailClosed_WhenNewerAcceptedGenerationCannotBeValidated(
		bool corruptBundle,
		string expectedDiagnostic) {
		// Arrange
		byte[] previousBundle = Bundle(("article.md", "previous"));
		byte[] currentBundle = Bundle(("article.md", "current"));
		Publish("alpha", "com.example.alpha", 9, previousBundle);
		KnowledgeSourceCurrentState previous = _store.ReadCurrent("alpha", out string? previousDiagnostic)!;
		KnowledgeInstallationResult advanced = _store.Publish(
			"alpha", "com.example.alpha", "1.0.0", 10, "nuget", "https://feed.invalid/v3/index.json",
			"1.0.10", currentBundle, isUpdate: true, previous.Active);
		KnowledgeSourceCurrentState accepted = _store.ReadCurrent("alpha", out string? acceptedDiagnostic)!;
		bool candidateRead = _store.TryReadCandidate(
			"alpha", accepted.Active, out InstalledKnowledgeSourceCandidate? candidate, out string? candidateDiagnostic);
		File.WriteAllBytes(
			GetCurrentMarkerPath(),
			JsonSerializer.SerializeToUtf8Bytes(
				previous,
				KnowledgeSourceInstallationJsonContext.Default.KnowledgeSourceCurrentState));
		if (corruptBundle) {
			File.WriteAllBytes(Path.Combine(candidate!.ContentRoot, "bundle.zip"), [0x00]);
		} else {
			Directory.Delete(candidate!.ContentRoot, recursive: true);
		}

		// Act
		KnowledgeSourceCurrentState? result = _store.ReadCurrent("alpha", out string? diagnostic);
		KnowledgeSourceCurrentState? persisted = JsonSerializer.Deserialize(
			File.ReadAllBytes(GetCurrentMarkerPath()),
			KnowledgeSourceInstallationJsonContext.Default.KnowledgeSourceCurrentState);

		// Assert
		previousDiagnostic.Should().BeNull(
			because: "the initial marker must be valid before simulating interruption");
		advanced.Status.Should().Be(KnowledgeInstallationStatus.Updated,
			because: "the replay ledger must advance to the candidate under test");
		acceptedDiagnostic.Should().BeNull(
			because: "the newer generation must initially be accepted and readable");
		candidateRead.Should().BeTrue(
			because: candidateDiagnostic ?? "the test must modify a complete accepted generation");
		result.Should().BeNull(
			because: "recovery must never activate missing or digest-mismatched content");
		diagnostic.Should().Contain(expectedDiagnostic,
			because: "the failure must explain why the accepted generation was not activated");
		persisted.Should().Be(previous,
			because: "failed validation must leave the activation marker unchanged");
	}

	[Test]
	[Description("Rejects an equal-sequence activation marker whose digest conflicts with the library high-water identity.")]
	public void ReadCurrent_ShouldRejectEqualSequenceMarker_WhenDigestConflictsWithLibraryHighWater() {
		// Arrange
		byte[] bundle = Bundle(("article.md", "current"));
		Publish("alpha", "com.example.alpha", 10, bundle);
		KnowledgeSourceCurrentState accepted = _store.ReadCurrent("alpha", out string? acceptedDiagnostic)!;
		KnowledgeSourceCurrentState conflicting = accepted with {
			Active = accepted.Active with { BundleDigest = new string('a', 64) }
		};
		File.WriteAllBytes(
			GetCurrentMarkerPath(),
			JsonSerializer.SerializeToUtf8Bytes(
				conflicting,
				KnowledgeSourceInstallationJsonContext.Default.KnowledgeSourceCurrentState));

		// Act
		KnowledgeSourceCurrentState? result = _store.ReadCurrent("alpha", out string? diagnostic);

		// Assert
		acceptedDiagnostic.Should().BeNull(
			because: "the exact accepted marker must be readable before simulating a conflict");
		result.Should().BeNull(
			because: "equal-sequence identity conflicts cannot be interpreted as interrupted publication");
		diagnostic.Should().Contain("cannot be recovered automatically",
			because: "the refusal must not imply that conflicting content was activated");
	}

	[Test]
	[Description("Accepts an otherwise valid activation marker when no library high-water file exists for backward compatibility.")]
	public void ReadCurrent_ShouldAcceptMarker_WhenHighWaterIsMissing() {
		// Arrange
		byte[] bundle = Bundle(("article.md", "stable"));
		KnowledgeInstallationResult installed = Publish("alpha", "com.example.alpha", 10, bundle);
		File.Delete(Path.Combine(
			_root,
			"sources",
			".history",
			GetLibraryHistoryFileName("com.example.alpha")));

		// Act
		KnowledgeSourceCurrentState? current = _store.ReadCurrent("alpha", out string? diagnostic);

		// Assert
		installed.Status.Should().Be(KnowledgeInstallationStatus.Installed,
			because: "the compatibility scenario starts with one valid persisted generation");
		current.Should().NotBeNull(
			because: "stores created before replay-ledger persistence must remain readable");
		current!.Active.Sequence.Should().Be(10,
			because: "missing optional replay metadata must not alter the activation marker");
		diagnostic.Should().BeNull(
			because: "absence of backward-compatible replay metadata is not storage corruption");
	}

	private KnowledgeInstallationResult Publish(
		string alias,
		string libraryId,
		ulong sequence,
		byte[] bundle) => _store.Publish(
		alias,
		libraryId,
		"1.0.0",
		sequence,
		"nuget",
		"https://feed.invalid/v3/index.json",
		$"1.0.{sequence}",
		bundle,
		isUpdate: false,
		expectedActive: null);

	private static byte[] Bundle(params (string Path, string Text)[] entries) {
		using MemoryStream output = new();
		using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true)) {
			foreach ((string path, string text) in entries) {
				ZipArchiveEntry entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
				using Stream stream = entry.Open();
				stream.Write(Encoding.UTF8.GetBytes(text));
			}
		}
		return output.ToArray();
	}

	private static string GetLibraryHistoryFileName(string libraryId) {
		byte[] digest = System.Security.Cryptography.SHA256.HashData(
			Encoding.UTF8.GetBytes(libraryId.ToLowerInvariant()));
		return $"{Convert.ToHexString(digest).ToLowerInvariant()[..24]}.json";
	}

	private string GetCurrentMarkerPath() =>
		Directory.EnumerateFiles(_root, "current.json", SearchOption.AllDirectories).Single();
}
