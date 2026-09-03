using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public class PageBaselineStoreTests {

	private const string SchemaName = "UsrCase_FormPage";
	private const string MetaPath = "/ws/.clio-pages/UsrCase_FormPage/meta.json";

	private static string MetaJsonWithBaseline(
		string environmentName = "local",
		string environmentUri = null,
		string checksum = "abc",
		string editableSchemaUId = "11111111-2222-3333-4444-555555555555") =>
		JsonSerializer.Serialize(new PageMetaFileModel {
			FetchedAt = "2026-06-12T10:00:00Z",
			Page = new PageMetadataInfo { SchemaName = SchemaName },
			Baseline = new PageBaselineInfo {
				SchemaName = SchemaName,
				EnvironmentName = environmentName,
				EnvironmentUri = environmentUri,
				EditableSchemaExists = true,
				EditableSchemaUId = editableSchemaUId,
				Checksum = checksum,
				ModifiedOn = "raw-modified-on",
				CapturedAt = "2026-06-12T10:00:00Z"
			}
		});

	[Test]
	[Description("TryReadBaseline must return the baseline block when meta.json contains one.")]
	public void TryReadBaseline_ShouldReturnBaseline_WhenMetaJsonContainsBaseline() {
		// Arrange
		MockFileSystem fs = new();
		fs.AddFile(MetaPath, new MockFileData(MetaJsonWithBaseline()));

		// Act
		PageBaselineInfo baseline = PageBaselineStore.TryReadBaseline(fs, gate: null, MetaPath, out _);

		// Assert
		baseline.Should().NotBeNull(because: "the meta.json on disk carries a baseline block");
		baseline.Checksum.Should().Be("abc", because: "the persisted checksum must round-trip unchanged");
		baseline.EditableSchemaExists.Should().BeTrue(because: "the persisted existence flag must round-trip unchanged");
	}

	[Test]
	[Description("TryReadBaseline must return null for a legacy meta.json without a baseline block so the conflict check is skipped.")]
	public void TryReadBaseline_ShouldReturnNull_WhenMetaJsonIsLegacyFormat() {
		// Arrange
		MockFileSystem fs = new();
		fs.AddFile(MetaPath, new MockFileData("""{"fetchedAt":"2026-06-12T10:00:00Z","page":{"schemaName":"UsrCase_FormPage"}}"""));

		// Act
		PageBaselineInfo baseline = PageBaselineStore.TryReadBaseline(fs, gate: null, MetaPath, out _);

		// Assert
		baseline.Should().BeNull(because: "legacy meta.json files predate the baseline contract and must skip the check");
	}

	[Test]
	[Description("TryReadBaseline must return null when meta.json is missing or unparseable instead of throwing.")]
	public void TryReadBaseline_ShouldReturnNull_WhenMetaJsonMissingOrCorrupt() {
		// Arrange
		MockFileSystem fs = new();
		fs.AddFile("/ws/.clio-pages/Other/meta.json", new MockFileData("not-json{{{"));

		// Act
		PageBaselineInfo missing = PageBaselineStore.TryReadBaseline(fs, gate: null, MetaPath, out _);
		PageBaselineInfo corrupt = PageBaselineStore.TryReadBaseline(fs, gate: null, "/ws/.clio-pages/Other/meta.json", out _);

		// Assert
		missing.Should().BeNull(because: "a missing meta.json means no baseline was ever captured");
		corrupt.Should().BeNull(because: "an unparseable meta.json must fail toward no-check, never throw");
	}

	[Test]
	[Description("ResolveMetaFilePath must prefer the sibling meta.json when body-file resides inside .clio-pages/{schema}/.")]
	public void ResolveMetaFilePath_ShouldUseBodyFileSibling_WhenBodyFileInsideClioPages() {
		// Arrange
		MockFileSystem fs = new();
		string bodyFile = fs.Path.Combine("/custom/anchor", ".clio-pages", SchemaName, "body.js");

		// Act
		string metaPath = PageBaselineStore.ResolveMetaFilePath(
			fs, "/elsewhere", "/home/user", "/home/user/.clio", null, bodyFile, SchemaName, out _);

		// Assert — normalize both sides so the comparison is OS-agnostic (Windows adds a drive
		// prefix and uses backslashes; macOS/Linux do not). The code resolves the sibling via
		// GetFullPath, so the expectation must pass through the same normalization.
		string expectedPath = fs.Path.Combine(
			fs.Path.GetDirectoryName(fs.Path.GetFullPath(bodyFile)), "meta.json");
		fs.Path.GetFullPath(metaPath).Should().Be(fs.Path.GetFullPath(expectedPath),
			because: "a body-file inside .clio-pages pins the baseline to its sibling meta.json regardless of the anchor");
	}

	[Test]
	[Description("ResolveMetaFilePath must fall back to anchor resolution when body-file is outside .clio-pages.")]
	public void ResolveMetaFilePath_ShouldUseAnchor_WhenBodyFileOutsideClioPages() {
		// Arrange
		MockFileSystem fs = new();
		fs.AddFile("/ws/.clio/workspaceSettings.json", new MockFileData("{}"));
		fs.AddDirectory("/ws/src");

		// Act
		string metaPath = PageBaselineStore.ResolveMetaFilePath(
			fs, "/ws/src", "/home/user", "/home/user/.clio", null, "/tmp/body.js", SchemaName, out _);

		// Assert — normalize both sides (see sibling test): the anchor is resolved via GetFullPath,
		// so the expectation must be normalized identically to stay OS-agnostic on the Windows CI.
		string expectedPath = fs.Path.Combine("/ws", ".clio-pages", SchemaName, "meta.json");
		fs.Path.GetFullPath(metaPath).Should().Be(fs.Path.GetFullPath(expectedPath),
			because: "a body-file outside .clio-pages must not override the workspace-root anchor resolution");
	}

	[Test]
	[Description("MatchesEnvironment must match registered environment names ordinally ignoring case.")]
	public void MatchesEnvironment_ShouldMatch_WhenEnvironmentNamesEqualIgnoreCase() {
		// Arrange
		PageBaselineInfo baseline = new() { EnvironmentName = "Local" };

		// Act
		bool matches = PageBaselineStore.MatchesEnvironment(baseline, "local", null);

		// Assert
		matches.Should().BeTrue(because: "environment names identify the same registration regardless of casing");
	}

	[Test]
	[Description("MatchesEnvironment must match direct URIs normalized for trailing slash and case.")]
	public void MatchesEnvironment_ShouldMatch_WhenUrisDifferOnlyByTrailingSlash() {
		// Arrange
		PageBaselineInfo baseline = new() { EnvironmentUri = "https://Site.creatio.com/" };

		// Act
		bool matches = PageBaselineStore.MatchesEnvironment(baseline, null, "https://site.creatio.com");

		// Assert
		matches.Should().BeTrue(because: "a trailing slash or casing difference does not change the target environment");
	}

	[Test]
	[Description("MatchesEnvironment must NOT match cross-mode combinations (name vs uri) or differing identities so the check is skipped.")]
	public void MatchesEnvironment_ShouldNotMatch_WhenIdentityModesDifferOrMismatch() {
		// Arrange
		PageBaselineInfo namedBaseline = new() { EnvironmentName = "local" };
		PageBaselineInfo uriBaseline = new() { EnvironmentUri = "https://a.creatio.com" };

		// Act
		bool crossMode = PageBaselineStore.MatchesEnvironment(namedBaseline, null, "https://a.creatio.com");
		bool nameMismatch = PageBaselineStore.MatchesEnvironment(namedBaseline, "prod", null);
		bool uriMismatch = PageBaselineStore.MatchesEnvironment(uriBaseline, null, "https://b.creatio.com");
		bool nullBaseline = PageBaselineStore.MatchesEnvironment(null, "local", null);

		// Assert
		crossMode.Should().BeFalse(because: "a name-captured baseline cannot be proven to target the same host as a raw uri");
		nameMismatch.Should().BeFalse(because: "different environment names are different targets");
		uriMismatch.Should().BeFalse(because: "different hosts are different targets");
		nullBaseline.Should().BeFalse(because: "no baseline means there is nothing to match");
	}

	[Test]
	[Description("RefreshExistingBaseline must rewrite only the baseline block, preserving fetchedAt and page.")]
	public void RefreshExistingBaseline_ShouldPreserveFetchedAtAndPage_WhenRefreshing() {
		// Arrange
		MockFileSystem fs = new();
		fs.AddFile(MetaPath, new MockFileData(MetaJsonWithBaseline(checksum: "old")));

		// Act
		PageBaselineStore.RefreshExistingBaseline(
			fs,
			gate: null,
			MetaPath,
			new PageBaselineInfo {
				SchemaName = SchemaName,
				EnvironmentName = "local",
				EditableSchemaExists = true,
				EditableSchemaUId = "99999999-8888-7777-6666-555555555555",
				Checksum = "new-checksum",
				ModifiedOn = "new-modified",
				CapturedAt = "2026-06-12T11:00:00Z"
			});

		// Assert
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(fs.GetFile(MetaPath).TextContents);
		meta.FetchedAt.Should().Be("2026-06-12T10:00:00Z", because: "fetchedAt belongs to the get-page snapshot and must survive the refresh");
		meta.Page.SchemaName.Should().Be(SchemaName, because: "page metadata belongs to the get-page snapshot and must survive the refresh");
		meta.Baseline.Checksum.Should().Be("new-checksum", because: "the refresh must persist the post-save checksum");
		meta.Baseline.EditableSchemaUId.Should().Be("99999999-8888-7777-6666-555555555555",
			because: "the refresh must persist the schema UId the save actually wrote to");
		meta.Baseline.EditableSchemaExists.Should().BeTrue(because: "a successful save guarantees the editable schema now exists");
		meta.Baseline.CapturedAt.Should().Be("2026-06-12T11:00:00Z", because: "the refresh timestamp must reflect the save moment");
	}

	[Test]
	[Description("RefreshExistingBaseline must carry forward a prior EnvironmentUri when the post-save baseline omits it, so a name-mode sync refresh does not disarm URI-mode conflict detection.")]
	public void RefreshExistingBaseline_ShouldPreservePriorEnvironmentUri_WhenRefreshOmitsIt() {
		// Arrange — a prior get-page captured both the name and the direct URI identity.
		MockFileSystem fs = new();
		fs.AddFile(MetaPath, new MockFileData(MetaJsonWithBaseline(
			environmentName: "local", environmentUri: "https://site.creatio.com", checksum: "old")));

		// Act — a sync-pages (name-only) refresh persists the post-save checksum without a URI.
		PageBaselineStore.RefreshExistingBaseline(
			fs,
			gate: null,
			MetaPath,
			new PageBaselineInfo {
				SchemaName = SchemaName,
				EnvironmentName = "local",
				EditableSchemaExists = true,
				EditableSchemaUId = "99999999-8888-7777-6666-555555555555",
				Checksum = "new-checksum",
				ModifiedOn = "new-modified",
				CapturedAt = "2026-06-12T11:00:00Z"
			});

		// Assert
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(fs.GetFile(MetaPath).TextContents);
		meta.Baseline.Checksum.Should().Be("new-checksum", because: "the refresh must still persist the post-save checksum");
		meta.Baseline.EnvironmentName.Should().Be("local", because: "the name identity must round-trip unchanged");
		meta.Baseline.EnvironmentUri.Should().Be("https://site.creatio.com",
			because: "a name-only refresh must not strip the URI identity a prior capture stored, or URI-mode conflict detection silently disarms");
	}

	[Test]
	[Description("MergeEnvironmentIdentity must keep an explicitly supplied identity field instead of overwriting it with the prior value.")]
	public void MergeEnvironmentIdentity_ShouldKeepIncomingValue_WhenRefreshSuppliesIt() {
		// Arrange
		PageBaselineInfo previous = new() { EnvironmentName = "old", EnvironmentUri = "https://old.creatio.com" };
		PageBaselineInfo refreshed = new() {
			SchemaName = SchemaName, EnvironmentName = "new", EnvironmentUri = "https://new.creatio.com", Checksum = "c"
		};

		// Act
		PageBaselineInfo merged = PageBaselineStore.MergeEnvironmentIdentity(refreshed, previous);

		// Assert
		merged.EnvironmentName.Should().Be("new", because: "an explicit incoming name must win over the prior value");
		merged.EnvironmentUri.Should().Be("https://new.creatio.com", because: "an explicit incoming URI must win over the prior value");
	}

	[Test]
	[Description("RefreshExistingBaseline must no-op when meta.json does not exist — the store never creates .clio-pages trees.")]
	public void RefreshExistingBaseline_ShouldNoOp_WhenMetaJsonMissing() {
		// Arrange
		MockFileSystem fs = new();

		// Act
		PageBaselineStore.RefreshExistingBaseline(
			fs,
			gate: null,
			MetaPath,
			new PageBaselineInfo {
				SchemaName = SchemaName,
				EnvironmentName = "local",
				EditableSchemaExists = true,
				EditableSchemaUId = "uid",
				Checksum = "checksum",
				ModifiedOn = "modified",
				CapturedAt = "2026-06-12T11:00:00Z"
			});

		// Assert
		fs.FileExists(MetaPath).Should().BeFalse(because: "the refresh path must never materialize .clio-pages directories");
	}

	[Test]
	[Description("DeleteBaseline must remove the baseline block while keeping fetchedAt and page intact.")]
	public void DeleteBaseline_ShouldRemoveBaselineOnly_WhenMetaJsonHasBaseline() {
		// Arrange
		MockFileSystem fs = new();
		fs.AddFile(MetaPath, new MockFileData(MetaJsonWithBaseline()));

		// Act
		PageBaselineStore.DeleteBaseline(fs, gate: null, MetaPath);

		// Assert
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(fs.GetFile(MetaPath).TextContents);
		meta.Baseline.Should().BeNull(because: "a stale baseline must be removed so the next write skips the check instead of false-conflicting");
		meta.FetchedAt.Should().Be("2026-06-12T10:00:00Z", because: "legacy fields must survive baseline removal");
		meta.Page.SchemaName.Should().Be(SchemaName, because: "legacy fields must survive baseline removal");
	}

	// ---------------------------------------------------------------------------------------------
	// TC-U-901 (ENG-95262 AC-02): the baseline path used to swallow every I/O failure, so a lost
	// refresh and a healthy save were indistinguishable on the wire. Each failure now reports a
	// diagnostic the caller surfaces as a response WARNING — never an exception, because by the time
	// these run the Creatio save has already landed and failing the response would misreport it.
	// The discrimination is the point: a MISSING meta.json is the legitimate "no baseline" state and
	// must stay silent, or every first-ever save would carry a spurious warning.
	// ---------------------------------------------------------------------------------------------

	// A file system whose meta.json reads fine but whose writes always fail — the shape of a full disk,
	// a read-only checkout, or a permission change under the workspace.
	private static IFileSystem CreateWriteFailingFileSystem(string metaContent, string failureMessage) {
		MockFileSystem pathSource = new();
		IFileSystem fileSystem = Substitute.For<IFileSystem>();
		fileSystem.Path.Returns(pathSource.Path);
		IFile file = Substitute.For<IFile>();
		fileSystem.File.Returns(file);
		file.Exists(MetaPath).Returns(true);
		file.ReadAllText(MetaPath).Returns(metaContent);
		file.When(f => f.WriteAllText(Arg.Any<string>(), Arg.Any<string>()))
			.Do(_ => throw new IOException(failureMessage));
		return fileSystem;
	}

	[Test]
	[Description("TC-U-901: RefreshExistingBaseline must return a surfaced warning naming the lost refresh when the meta.json write fails, instead of swallowing the failure.")]
	public void RefreshExistingBaseline_ShouldReturnWarning_WhenWriteFails() {
		// Arrange
		IFileSystem fileSystem = CreateWriteFailingFileSystem(MetaJsonWithBaseline(checksum: "old"), "no space left on device");

		// Act
		string warning = PageBaselineStore.RefreshExistingBaseline(
			fileSystem,
			gate: null,
			MetaPath,
			new PageBaselineInfo {
				SchemaName = SchemaName,
				EnvironmentName = "local",
				EditableSchemaExists = true,
				EditableSchemaUId = "uid",
				Checksum = "new-checksum",
				ModifiedOn = "new-modified",
				CapturedAt = "2026-06-12T11:00:00Z"
			});

		// Assert
		warning.Should().NotBeNull(
			because: "a refresh that could not be written leaves the stored checksum behind the server, and the caller must be told rather than shown a clean success");
		warning.Should().Contain("no space left on device",
			because: "the underlying I/O reason must reach the caller so the failure is actionable, not merely announced");
		warning.Should().Contain(MetaPath,
			because: "the caller has to know WHICH baseline was lost to be able to recapture it");
	}

	[Test]
	[Description("TC-U-901: DeleteBaseline must return a surfaced warning when the meta.json write fails, so a stale baseline that could not be removed is not left invisible.")]
	public void DeleteBaseline_ShouldReturnWarning_WhenWriteFails() {
		// Arrange
		IFileSystem fileSystem = CreateWriteFailingFileSystem(MetaJsonWithBaseline(), "access to the path is denied");

		// Act
		string warning = PageBaselineStore.DeleteBaseline(fileSystem, gate: null, MetaPath);

		// Assert
		warning.Should().NotBeNull(
			because: "a stale baseline that survived its removal will compare the next save against a superseded checksum, which the caller must be warned about");
		warning.Should().Contain("access to the path is denied",
			because: "the underlying I/O reason must reach the caller");
	}

	[Test]
	[Description("TC-U-901: a refresh whose write fails must NOT throw — the Creatio save has already landed, so failing the call would report a successful write as a failure.")]
	public void RefreshExistingBaseline_ShouldNotThrow_WhenWriteFails() {
		// Arrange
		IFileSystem fileSystem = CreateWriteFailingFileSystem(MetaJsonWithBaseline(), "device error");

		// Act
		Action refresh = () => PageBaselineStore.RefreshExistingBaseline(
			fileSystem, gate: null, MetaPath, new PageBaselineInfo { SchemaName = SchemaName, Checksum = "c" });

		// Assert
		refresh.Should().NotThrow(
			because: "a failed refresh must never fail a save that already succeeded on the server — the warning is the whole mechanism");
	}

	[Test]
	[Description("TC-U-901: TryReadBaseline must warn that conflict detection is disarmed when an EXISTING meta.json cannot be parsed.")]
	public void TryReadBaseline_ShouldWarnDisarmed_WhenExistingMetaIsCorrupt() {
		// Arrange
		MockFileSystem fs = new();
		fs.AddFile(MetaPath, new MockFileData("not-json{{{"));

		// Act
		PageBaselineInfo baseline = PageBaselineStore.TryReadBaseline(fs, gate: null, MetaPath, out string warning);

		// Assert
		baseline.Should().BeNull(because: "an unparseable baseline must still fail toward no-check rather than blocking the write");
		warning.Should().NotBeNull(
			because: "silently skipping the external-modification check is exactly the invisible failure AC-02 removes");
		warning.Should().Contain("DISARMED",
			because: "the caller must understand that the write is proceeding WITHOUT conflict detection, not that all is well");
	}

	[Test]
	[Description("TC-U-901: TryReadBaseline must stay SILENT when meta.json is simply missing — that is the legitimate no-baseline state, not a failure.")]
	public void TryReadBaseline_ShouldNotWarn_WhenMetaIsMissing() {
		// Arrange
		MockFileSystem fs = new();

		// Act
		PageBaselineInfo baseline = PageBaselineStore.TryReadBaseline(fs, gate: null, MetaPath, out string warning);

		// Assert
		baseline.Should().BeNull(because: "no baseline was ever captured for this page");
		warning.Should().BeNull(
			because: "a page that was never fetched with get-page has no baseline by design; warning about it would make the first save of every page noisy and train callers to ignore the channel");
	}

	[Test]
	[Description("TC-U-901: TryReadBaseline must stay SILENT for a legacy meta.json that carries no baseline block — a known, supported format, not an I/O failure.")]
	public void TryReadBaseline_ShouldNotWarn_WhenMetaIsLegacyFormat() {
		// Arrange
		MockFileSystem fs = new();
		fs.AddFile(MetaPath, new MockFileData("""{"fetchedAt":"2026-06-12T10:00:00Z","page":{"schemaName":"UsrCase_FormPage"}}"""));

		// Act
		PageBaselineInfo baseline = PageBaselineStore.TryReadBaseline(fs, gate: null, MetaPath, out string warning);

		// Assert
		baseline.Should().BeNull(because: "a legacy meta.json predates the baseline contract");
		warning.Should().BeNull(because: "a supported older format parsed correctly; there is nothing to report");
	}

	[Test]
	[Description("Every meta.json write must be atomic (temp file + replace) so a concurrent reader cannot observe a truncated prefix; the temp file must not be left behind.")]
	public void RefreshExistingBaseline_ShouldWriteAtomicallyAndLeaveNoTempFile_WhenRefreshing() {
		// Arrange
		MockFileSystem fs = new();
		fs.AddFile(MetaPath, new MockFileData(MetaJsonWithBaseline(checksum: "old")));

		// Act
		string warning = PageBaselineStore.RefreshExistingBaseline(
			fs, gate: null, MetaPath, new PageBaselineInfo {
				SchemaName = SchemaName, EnvironmentName = "local", EditableSchemaExists = true,
				EditableSchemaUId = "uid", Checksum = "new-checksum", ModifiedOn = "m", CapturedAt = "2026-06-12T11:00:00Z"
			});

		// Assert
		warning.Should().BeNull(because: "a successful refresh reports nothing");
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(fs.GetFile(MetaPath).TextContents);
		meta.Baseline.Checksum.Should().Be("new-checksum", because: "the atomic replacement must publish the new content");
		fs.AllFiles.Where(path => path.Contains(".tmp", StringComparison.Ordinal)).Should().BeEmpty(
			because: "the sibling temp file used for the atomic replace must never be left behind in the workspace");
	}

	[Test]
	[Description("The schema's lock sentinel must live in a sibling .locks directory, NOT inside .clio-pages/{schema}/ which get-page deletes recursively.")]
	public void ResolveSchemaLockFilePath_ShouldPlaceSentinelOutsideTheDeletedSchemaDirectory_WhenDerivedFromMetaPath() {
		// Arrange
		MockFileSystem fs = new();

		// Act
		string lockPath = PageBaselineStore.ResolveSchemaLockFilePath(fs, MetaPath);

		// Assert
		lockPath.Should().NotBeNull(because: "a well-formed meta.json path must yield a sentinel path");
		fs.Path.GetFileName(lockPath).Should().Be($"{SchemaName}.lock",
			because: "the sentinel is per schema, so two schemas never serialise against each other");
		fs.Path.GetFileName(fs.Path.GetDirectoryName(lockPath)).Should().Be(".locks",
			because: "the sentinel must sit in a sibling .locks directory");
		string schemaDir = fs.Path.GetDirectoryName(MetaPath);
		fs.Path.GetFullPath(lockPath).Should().NotStartWith(fs.Path.GetFullPath(schemaDir),
			because: "get-page deletes .clio-pages/{schema}/ recursively — a sentinel inside it would be unlinked from under its holder on Unix and would make the delete fail against the open exclusive handle on Windows");
	}

	[Test]
	[Description("A meta.json missing while a concurrent get-page rewrites the schema directory must NOT be read as 'no baseline': the gate has to be taken, or update-page runs with no expected checksum and silently overwrites an external change.")]
	public void TryReadBaseline_ShouldTakeTheGate_WhenTheFileIsMissingButThePagesTreeExists() {
		// Arrange — the transient state PageFileWriter creates: it deletes the whole schema directory while
		// holding the gate, so the schema's meta.json is gone but the .clio-pages ROOT is not.
		MockFileSystem fs = new();
		string pagesRoot = fs.Path.GetDirectoryName(fs.Path.GetDirectoryName(MetaPath));
		fs.AddDirectory(pagesRoot);
        IInterprocessFileGate gate = Substitute.For<IInterprocessFileGate>();

		// Act
		PageBaselineStore.TryReadBaseline(fs, gate, MetaPath, out _);

		// Assert
		gate.ReceivedCalls().Should().NotBeEmpty(
			because: "an absence inside an existing .clio-pages tree may be a get-page mid-rewrite, and answering it without the gate is how a stale-baseline write slips past conflict detection");
	}

	[Test]
	[Description("With no .clio-pages tree at all the absence is definitive, so the gate must NOT be taken — acquiring it would create a .locks directory in a workspace that never captured a baseline.")]
	public void TryReadBaseline_ShouldNotTakeTheGate_WhenThereIsNoPagesTreeAtAll() {
		// Arrange — nothing on disk.
		MockFileSystem fs = new();
		IInterprocessFileGate gate = Substitute.For<IInterprocessFileGate>();

		// Act
		PageBaselineInfo baseline = PageBaselineStore.TryReadBaseline(fs, gate, MetaPath, out _);

		// Assert
		baseline.Should().BeNull(because: "no tree means no baseline was ever captured here");
		gate.ReceivedCalls().Should().BeEmpty(
			because: "the store promises never to materialise a .clio-pages tree as a side effect of looking for a baseline that does not exist");
	}
}
