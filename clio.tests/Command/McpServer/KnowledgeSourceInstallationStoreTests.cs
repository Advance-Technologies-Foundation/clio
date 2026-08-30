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
		services.AddSingleton<IKnowledgeManagedTreeDeleter, KnowledgeManagedTreeDeleter>();
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
		KnowledgeInstallationResult rollback = _store.Publish(Update("1.0.0", 9, "0.9.0", changedBundle, expected));
		KnowledgeInstallationResult digestRewrite = _store.Publish(Update("1.0.1", 10, "1.0.1", changedBundle, expected));
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
	[Description("Repairing the active generation keeps the generation behind it as the rollback target instead of the generation being replaced.")]
	public void Publish_ShouldKeepPriorGenerationAsRollback_WhenRepairingActiveGeneration() {
		// Arrange
		byte[] firstBundle = Bundle(("article.md", "first"));
		byte[] secondBundle = Bundle(("article.md", "second"));
		Publish("alpha", "com.example.alpha", 1, firstBundle);
		KnowledgeSourceGenerationPointer first = _store.ReadCurrent("alpha", out _)!.Active;
		KnowledgeInstallationResult advanced = _store.Publish(Update("1.0.0", 2, "1.0.2", secondBundle, first));
		KnowledgeSourceGenerationPointer second = _store.ReadCurrent("alpha", out _)!.Active;

		// Act
		KnowledgeInstallationResult repaired = _store.Publish(Update("1.0.0", 2, "1.0.2", secondBundle, second, allowRepair: true));
		KnowledgeSourceCurrentState? current = _store.ReadCurrent("alpha", out string? diagnostic);

		// Assert
		advanced.Status.Should().Be(KnowledgeInstallationStatus.Updated,
			because: "the repair must act on a second generation, not on the freshly installed first one");
		second.RelativePath.Should().NotBe(first.RelativePath,
			because: "the arranged history needs two distinct generations before a repair can be meaningful");
		repaired.Status.Should().Be(KnowledgeInstallationStatus.Updated,
			because: "an explicitly allowed repair must replace the damaged active generation");
		diagnostic.Should().BeNull(because: "a completed repair must leave a readable activation marker");
		current!.Active.RelativePath.Should().Contain("-repair-",
			because: "the scenario must reach the in-place repair branch rather than an ordinary forward update");
		current.Active.RelativePath.Should().NotBe(second.RelativePath,
			because: "the repair must materialize fresh content rather than reuse the generation it replaces");
		current.Previous.Should().Be(first,
			because: "a repair replaces the active generation in place, so the rollback target must stay the one behind it");
		Directory.Exists(GenerationPath("alpha", first)).Should().BeTrue(
			because: "the retained rollback generation must survive pruning after a repair");
		Directory.Exists(GenerationPath("alpha", second)).Should().BeFalse(
			because: "the replaced generation is what the repair distrusts and must never become the rollback target");
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
		KnowledgeInstallationResult repaired = _store.Publish(Update("1.0.0", 10, "1.0.10", bundle, before.Active, allowRepair: true));
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
		KnowledgeInstallationResult advanced = _store.Publish(Update("1.0.0", 10, "1.0.10", currentBundle, previous.Active));
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
		KnowledgeInstallationResult advanced = _store.Publish(Update("1.0.0", 10, "1.0.10", currentBundle, previous.Active));
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

	[Test]
	[Description("The startup-safe generation probe returns the recorded active pointer for a published generation.")]
	public void TryReadStartupState_ShouldReturnActivePointer_WhenAGenerationIsPublished() {
		// Arrange
		byte[] bundle = Bundle(("guidance/alpha.md", "# alpha"));
		Publish("alpha", "com.example.alpha", 1, bundle);

		// Act
		KnowledgeSourceStartupState? state = _store.TryReadStartupState("alpha");

		// Assert
		state.Should().NotBeNull(
			because: "a warm start must be able to learn which generation it would serve without any reconciliation");
		KnowledgeSourceGenerationPointer active = state!.Active;
		active.LibraryVersion.Should().Be("1.0.0",
			because: "the served library version is what a staleness report and an agent session need to name");
		active.Sequence.Should().Be(1,
			because: "the recorded sequence identifies the generation the marker points at");
		active.ActivatedAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5),
			because: "cache age is computed from this timestamp, so it must reflect the publication just made");
	}

	[Test]
	[Description("The startup-safe generation probe reports nothing when the source has no activation marker.")]
	public void TryReadStartupState_ShouldReturnNull_WhenNothingIsInstalled() {
		// Arrange
		const string sourceAlias = "alpha";

		// Act
		KnowledgeSourceStartupState? state = _store.TryReadStartupState(sourceAlias);

		// Assert
		state.Should().BeNull(
			because: "an absent marker means the age of the cache is unknown, not that a stale cache exists");
	}

	[Test]
	[Description("A successful publisher check renews only the generation that was active when the check began.")]
	public void TryRecordPublisherCheck_ShouldRenewFreshness_WhenExpectedGenerationIsStillActive() {
		// Arrange
		byte[] bundle = Bundle(("guidance/alpha.md", "# alpha"));
		Publish("alpha", "com.example.alpha", 1, bundle);
		KnowledgeSourceCurrentState published = _store.ReadCurrent("alpha", out string? publishedDiagnostic)!;
		KnowledgeSourceCurrentState before = published with {
			Active = published.Active with { ActivatedAtUtc = DateTimeOffset.UtcNow - TimeSpan.FromDays(10) }
		};
		File.WriteAllBytes(
			GetCurrentMarkerPath(),
			JsonSerializer.SerializeToUtf8Bytes(
				before,
				KnowledgeSourceInstallationJsonContext.Default.KnowledgeSourceCurrentState));

		// Act
		bool recorded = _store.TryRecordPublisherCheck("alpha", before.Active);
		KnowledgeSourceCurrentState? after = _store.ReadCurrent("alpha", out string? afterDiagnostic);
		KnowledgeSourceStartupState? startup = _store.TryReadStartupState("alpha");
		string currentMarker = File.ReadAllText(GetCurrentMarkerPath());

		// Assert
		publishedDiagnostic.Should().BeNull(
			because: "the arranged generation must be readable before its publisher freshness is renewed");
		recorded.Should().BeTrue(
			because: "the publisher confirmed that the still-active generation is current");
		afterDiagnostic.Should().BeNull(
			because: "renewing freshness must preserve a valid activation marker");
		after.Should().NotBeNull(
			because: "recording publisher freshness must not deactivate the generation");
		startup!.LastPublisherCheckAtUtc.Should().BeAfter(before.Active.ActivatedAtUtc,
			because: "the warm-start warning must age from the latest successful publisher confirmation");
		after!.Active.Should().BeEquivalentTo(before.Active,
			because: "a freshness acknowledgement must not change bundle identity or provenance");
		currentMarker.Should().NotContain("lastPublisherCheckAtUtc",
			because: "released Clio readers reject unknown current-marker members, so freshness must stay in a sidecar");
	}

	[Test]
	[Description("Publisher freshness recording is best-effort and never waits behind an active source mutation.")]
	public void TryRecordPublisherCheck_ShouldReturnFalse_WhenSourceMutationLockIsBusy() {
		// Arrange
		byte[] bundle = Bundle(("guidance/alpha.md", "# alpha"));
		Publish("alpha", "com.example.alpha", 1, bundle);
		KnowledgeSourceCurrentState current = _store.ReadCurrent("alpha", out string? diagnostic)!;
		bool? recorded = null;

		// Act
		_store.ExecuteWithSourceMutationLock("alpha", () => {
			recorded = _store.TryRecordPublisherCheck("alpha", current.Active);
			return true;
		});

		// Assert
		diagnostic.Should().BeNull(
			because: "the arranged active generation must be readable before simulating lock contention");
		recorded.Should().BeFalse(
			because: "an advisory freshness write must not extend a completed publisher check past its deadline");
	}

	[Test]
	[Description("Recording publisher freshness does not invalidate publication compare-and-swap identity.")]
	public void Publish_ShouldAcceptExpectedGeneration_AfterPublisherFreshnessWasRecorded() {
		// Arrange
		byte[] initialBundle = Bundle(("guidance/alpha.md", "# alpha"));
		Publish("alpha", "com.example.alpha", 1, initialBundle);
		KnowledgeSourceCurrentState current = _store.ReadCurrent("alpha", out string? diagnostic)!;
		_store.TryRecordPublisherCheck("alpha", current.Active).Should().BeTrue(
			because: "the interleaving requires a successful freshness acknowledgement");

		// Act
		KnowledgeInstallationResult updated = _store.Publish(new KnowledgeGenerationPublication {
			SourceAlias = "alpha",
			LibraryId = "com.example.alpha",
			LibraryVersion = "2.0.0",
			Sequence = 2,
			TransportType = KnowledgeSourceTypeNames.NuGet,
			Location = "https://example.invalid/v3/index.json",
			ResolvedRevision = "2.0.0",
			BundleBytes = Bundle(("guidance/alpha.md", "# alpha 2")),
			IsUpdate = true,
			ExpectedActive = current.Active,
			AllowRepair = false
		});
		KnowledgeSourceStartupState? startup = _store.TryReadStartupState("alpha");

		// Assert
		diagnostic.Should().BeNull(
			because: "the initial generation must be valid before exercising the compare-and-swap");
		updated.Status.Should().Be(KnowledgeInstallationStatus.Updated,
			because: "freshness metadata is not generation identity and cannot reject a concurrently downloaded release");
		startup!.Active.Sequence.Should().Be(2,
			because: "startup must select the newly published generation after the interleaving");
		startup.LastPublisherCheckAtUtc.Should().BeNull(
			because: "a freshness sidecar bound to generation one cannot mark generation two current");
	}

	[Test]
	[Description("A delayed publisher check cannot renew a different generation that became active while it was running.")]
	public void TryRecordPublisherCheck_ShouldNotRenewFreshness_WhenActiveGenerationChanged() {
		// Arrange
		byte[] bundle = Bundle(("guidance/alpha.md", "# alpha"));
		Publish("alpha", "com.example.alpha", 1, bundle);
		KnowledgeSourceCurrentState current = _store.ReadCurrent("alpha", out string? beforeDiagnostic)!;
		KnowledgeSourceGenerationPointer staleExpectation = current.Active with { ResolvedRevision = "older-check" };

		// Act
		bool recorded = _store.TryRecordPublisherCheck("alpha", staleExpectation);
		KnowledgeSourceCurrentState? after = _store.ReadCurrent("alpha", out string? afterDiagnostic);

		// Assert
		beforeDiagnostic.Should().BeNull(
			because: "the arranged active generation must be valid before simulating a concurrent change");
		recorded.Should().BeFalse(
			because: "a check for a different generation cannot establish freshness for the active one");
		afterDiagnostic.Should().BeNull(
			because: "rejecting a stale acknowledgement must leave the activation marker valid");
		after.Should().BeEquivalentTo(current,
			because: "a failed compare-and-swap must not mutate bundle identity or freshness");
	}

	[Test]
	[Description("The startup-safe generation probe swallows a corrupt activation marker instead of faulting startup.")]
	public void TryReadStartupState_ShouldReturnNull_WhenActivationMarkerIsCorrupt() {
		// Arrange
		byte[] bundle = Bundle(("guidance/alpha.md", "# alpha"));
		Publish("alpha", "com.example.alpha", 1, bundle);
		string sourceRoot = Path.GetDirectoryName(_store.GetGitRepositoryPath("alpha", createSourceRoot: false))!;
		File.WriteAllText(Path.Combine(sourceRoot, "current.json"), "{ not json");

		// Act
		KnowledgeSourceStartupState? state = _store.TryReadStartupState("alpha");

		// Assert
		state.Should().BeNull(
			because: "a probe on the bounded startup path must never surface a parse failure to its caller");
	}

	private KnowledgeInstallationResult Publish(
		string alias,
		string libraryId,
		ulong sequence,
		byte[] bundle) => _store.Publish(new KnowledgeGenerationPublication {
			SourceAlias = alias,
			LibraryId = libraryId,
			LibraryVersion = "1.0.0",
			Sequence = sequence,
			TransportType = "nuget",
			Location = "https://feed.invalid/v3/index.json",
			ResolvedRevision = $"1.0.{sequence}",
			BundleBytes = bundle,
			IsUpdate = false
		});

	// Every update in this fixture targets the same alias, library, transport and location; only the
	// generation, revision, payload and concurrency expectation differ.
	private static KnowledgeGenerationPublication Update(
		string libraryVersion,
		ulong sequence,
		string revision,
		byte[] bundle,
		KnowledgeSourceGenerationPointer? expectedActive,
		bool allowRepair = false) => new() {
			SourceAlias = "alpha",
			LibraryId = "com.example.alpha",
			LibraryVersion = libraryVersion,
			Sequence = sequence,
			TransportType = "nuget",
			Location = "https://feed.invalid/v3/index.json",
			ResolvedRevision = revision,
			BundleBytes = bundle,
			IsUpdate = true,
			ExpectedActive = expectedActive,
			AllowRepair = allowRepair
		};

	private string GenerationPath(string alias, KnowledgeSourceGenerationPointer pointer) {
		string sourceRoot = Path.GetDirectoryName(_store.GetGitRepositoryPath(alias, createSourceRoot: false))!;
		return Path.Combine(sourceRoot, pointer.RelativePath.Replace('/', Path.DirectorySeparatorChar));
	}

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
