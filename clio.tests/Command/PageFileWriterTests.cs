using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageFileWriterTests {

	private const string SchemaName = "Usr_FormPage";
	private const string SchemaUId = "11111111-2222-3333-4444-555555555555";
	private const string OutputDirectory = "/ws";

	private MockFileSystem _fileSystem;
	private RecordingFileGate _fileGate;
	private PageFileWriter _writer;
	// Built through the same GetFullPath + Combine normalization the writer uses, so path comparisons
	// stay OS-agnostic (the Windows CI adds a drive prefix and uses backslashes; macOS/Linux do not).
	private string _clioPagesDir;
	private string _schemaDir;

	[SetUp]
	public void SetUp() {
		_fileSystem = new MockFileSystem();
		_fileGate = new RecordingFileGate();
		_writer = new PageFileWriter(_fileSystem, _fileGate);
		_clioPagesDir = _fileSystem.Path.Combine(_fileSystem.Path.GetFullPath(OutputDirectory), ".clio-pages");
		_schemaDir = _fileSystem.Path.Combine(_clioPagesDir, SchemaName);
	}

	private string SchemaFile(string fileName) => _fileSystem.Path.Combine(_schemaDir, fileName);

	private static PageGetResponse CreateResponse(PageEditableSchemaInfo editable = null) =>
		new() {
			Success = true,
			Page = new PageMetadataInfo { SchemaName = SchemaName },
			Bundle = new PageBundleInfo { Name = SchemaName },
			Raw = new PageRawInfo { Body = "define(\"Usr_FormPage\", [], function() { return {}; });" },
			Editable = editable ?? new PageEditableSchemaInfo {
				EditableSchemaExists = true,
				EditableSchemaUId = SchemaUId,
				Checksum = "checksum-1",
				ModifiedOn = "2026-06-16T09:00:00"
			}
		};

	[Test]
	[Description("WritePageFiles must persist body.js, bundle.json and meta.json under .clio-pages/{schema}/ for a successful response.")]
	public void WritePageFiles_ShouldWriteBodyBundleAndMeta_WhenResponseSuccessful() {
		// Arrange
		PageGetResponse response = CreateResponse();

		// Act
		PageGetResponse written = _writer.WritePageFiles(response, SchemaName, "dev", null, OutputDirectory);

		// Assert
		written.Success.Should().BeTrue(because: "writing the page files for a successful read must succeed");
		_fileSystem.FileExists(SchemaFile("body.js")).Should().BeTrue(because: "body.js holds the editable own-body for update-page");
		_fileSystem.FileExists(SchemaFile("bundle.json")).Should().BeTrue(because: "bundle.json holds the merged hierarchy view");
		_fileSystem.FileExists(SchemaFile("meta.json")).Should().BeTrue(because: "meta.json holds the conflict-detection baseline");
		_fileSystem.GetFile(SchemaFile("body.js")).TextContents.Should().Be(response.Raw.Body,
			because: "body.js must contain the raw editable body verbatim");
	}

	[Test]
	[Description("WritePageFiles must capture the editable schema checksum and environment identity into the meta.json baseline.")]
	public void WritePageFiles_ShouldCaptureBaseline_WhenEditableSchemaExists() {
		// Arrange
		PageGetResponse response = CreateResponse();

		// Act
		_writer.WritePageFiles(response, SchemaName, "dev", null, OutputDirectory);

		// Assert
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(_fileSystem.GetFile(SchemaFile("meta.json")).TextContents);
		meta.Baseline.Should().NotBeNull(because: "an existing editable schema must produce a baseline");
		meta.Baseline.Checksum.Should().Be("checksum-1", because: "the baseline checksum is the change signal a later update-page compares against");
		meta.Baseline.EditableSchemaUId.Should().Be(SchemaUId, because: "the editable schema UId is part of the baseline identity");
		meta.Baseline.EnvironmentName.Should().Be("dev", because: "the environment identity guards against cross-environment false positives");
	}

	[Test]
	[Description("WritePageFiles must record the direct URI in the baseline when the read used a URI instead of an environment name.")]
	public void WritePageFiles_ShouldRecordUri_WhenUriProvided() {
		// Arrange
		PageGetResponse response = CreateResponse();

		// Act
		_writer.WritePageFiles(response, SchemaName, null, "https://localhost/", OutputDirectory);

		// Assert
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(_fileSystem.GetFile(SchemaFile("meta.json")).TextContents);
		meta.Baseline.EnvironmentUri.Should().Be("https://localhost/", because: "a URI-mode read must capture the URI for the env-identity guard");
		meta.Baseline.EnvironmentName.Should().BeNull(because: "a URI-mode read has no registered environment name");
	}

	[Test]
	[Description("WritePageFiles must omit the baseline block when the editable schema info is unavailable (FR-10 failed checksum capture).")]
	public void WritePageFiles_ShouldOmitBaseline_WhenEditableInfoUnavailable() {
		// Arrange — Editable is null, mirroring get-page's best-effort checksum capture failing.
		PageGetResponse source = CreateResponse();
		PageGetResponse response = new() {
			Success = true, Page = source.Page, Bundle = source.Bundle, Raw = source.Raw, Editable = null
		};

		// Act
		_writer.WritePageFiles(response, SchemaName, "dev", null, OutputDirectory);

		// Assert
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(_fileSystem.GetFile(SchemaFile("meta.json")).TextContents);
		meta.Baseline.Should().BeNull(
			because: "a failed checksum capture (null editable) must skip the baseline so update-page degrades to no check");
	}

	[Test]
	[Description("WritePageFiles must record an absent-schema baseline when no replacing schema exists yet, so a later update-page can detect external creation.")]
	public void WritePageFiles_ShouldRecordAbsentBaseline_WhenEditableSchemaDoesNotExist() {
		// Arrange — the page has no replacing schema yet (a write would create one).
		PageGetResponse response = CreateResponse(new PageEditableSchemaInfo { EditableSchemaExists = false });

		// Act
		_writer.WritePageFiles(response, SchemaName, "dev", null, OutputDirectory);

		// Assert
		PageMetaFileModel meta = JsonSerializer.Deserialize<PageMetaFileModel>(_fileSystem.GetFile(SchemaFile("meta.json")).TextContents);
		meta.Baseline.Should().NotBeNull(because: "an absent replacing schema is still a baseline state to detect external creation against");
		meta.Baseline.EditableSchemaExists.Should().BeFalse(because: "the baseline must record that no editable schema existed at fetch time");
	}

	[Test]
	[Description("WritePageFiles must create the .clio-pages/.gitignore hygiene file so page artifacts are not committed.")]
	public void WritePageFiles_ShouldCreateGitignore_WhenWritingPageFiles() {
		// Arrange
		PageGetResponse response = CreateResponse();

		// Act
		_writer.WritePageFiles(response, SchemaName, "dev", null, OutputDirectory);

		// Assert
		string gitignorePath = _fileSystem.Path.Combine(_clioPagesDir, ".gitignore");
		_fileSystem.FileExists(gitignorePath).Should().BeTrue(because: "the .clio-pages tree must be git-ignored by default");
		_fileSystem.GetFile(gitignorePath).TextContents.Should().Contain("*",
			because: "the gitignore must exclude all generated page artifacts");
	}

	[Test]
	[Description("WritePageFiles must return the written file paths so the caller can surface them to the user.")]
	public void WritePageFiles_ShouldReturnFilePaths_WhenWritingPageFiles() {
		// Arrange
		PageGetResponse response = CreateResponse();

		// Act
		PageGetResponse written = _writer.WritePageFiles(response, SchemaName, "dev", null, OutputDirectory);

		// Assert
		written.Files.Should().NotBeNull(because: "the response must expose the written file paths");
		_fileSystem.Path.GetFullPath(written.Files.BodyFile).Should().Be(_fileSystem.Path.GetFullPath(SchemaFile("body.js")),
			because: "the body file path must point at the written body.js");
		_fileSystem.Path.GetFullPath(written.Files.MetaFile).Should().Be(_fileSystem.Path.GetFullPath(SchemaFile("meta.json")),
			because: "the meta file path must point at the written meta.json");
	}

	[Test]
	[TestCase("../escape")]
	[TestCase("a/b")]
	[TestCase("a\\b")]
	[TestCase("..")]
	[TestCase("with space")]
	[TestCase("")]
	[Description("WritePageFiles must reject a schema name carrying path separators or out-of-charset characters before performing the recursive directory delete, so the destructive write cannot escape .clio-pages/.")]
	public void WritePageFiles_ShouldReturnFailureAndNotWrite_WhenSchemaNameIsUnsafe(string unsafeName) {
		// Arrange
		PageGetResponse response = CreateResponse();

		// Act
		PageGetResponse written = _writer.WritePageFiles(response, unsafeName, "dev", null, OutputDirectory);

		// Assert
		written.Success.Should().BeFalse(because: "an unsafe schema name must be refused before any filesystem mutation");
		written.Error.Should().Contain("schema name",
			because: "the error must explain that the schema name was rejected");
		_fileSystem.AllFiles.Should().BeEmpty(because: "a refused write must not create any page files on disk");
	}

	// ---------------------------------------------------------------------------------------------
	// ENG-95262 H-1: get-page's destructive prepare-and-write is one unit of work over files another
	// clio process may be reading, so it runs under the schema's interprocess sentinel.
	// ---------------------------------------------------------------------------------------------

	[Test]
	[Description("WritePageFiles must hold the schema's interprocess gate across the recursive delete AND the three file writes, in a single acquisition.")]
	public void WritePageFiles_ShouldHoldTheSchemaGateAcrossPrepareAndWrite_WhenWritingPageFiles() {
		// Arrange
		PageGetResponse response = CreateResponse();

		// Act
		_writer.WritePageFiles(response, SchemaName, "dev", null, OutputDirectory);

		// Assert
		_fileGate.EnteredLockPaths.Should().HaveCount(1,
			because: "the delete and the writes are one indivisible sequence — a second acquisition would let a reader observe the directory after the delete but before the rewrite");
		_fileGate.IsHeld.Should().BeFalse(because: "the gate must be released once the write returns");
	}

	[Test]
	[Description("The get-page sentinel must live outside .clio-pages/{schema}/, because that directory is deleted recursively on every get-page.")]
	public void WritePageFiles_ShouldUseASentinelOutsideTheDeletedDirectory_WhenWritingPageFiles() {
		// Arrange
		PageGetResponse response = CreateResponse();

		// Act
		_writer.WritePageFiles(response, SchemaName, "dev", null, OutputDirectory);

		// Assert
		string lockPath = _fileGate.EnteredLockPaths.Single();
		_fileSystem.Path.GetFileName(lockPath).Should().Be($"{SchemaName}.lock",
			because: "the sentinel is per schema, so a get-page of one page never waits on another");
		_fileSystem.Path.GetFullPath(lockPath).Should().NotStartWith(_fileSystem.Path.GetFullPath(_schemaDir),
			because: "a sentinel inside the deleted subtree would be unlinked from under its holder on Unix, and on Windows would make the recursive delete fail against the open exclusive handle and turn a working get-page into an error");
		_fileSystem.Path.GetFullPath(lockPath).Should().StartWith(_fileSystem.Path.GetFullPath(_clioPagesDir),
			because: "the sentinel still belongs to the workspace's .clio-pages tree, which the generated .gitignore already excludes wholesale");
	}

	[Test]
	[Description("WritePageFiles must report a failure rather than throwing when the schema gate cannot be acquired, so a concurrent operation surfaces as a normal error response.")]
	public void WritePageFiles_ShouldReturnFailure_WhenTheGateTimesOut() {
		// Arrange
		PageFileWriter writer = new(_fileSystem, new TimingOutFileGate());
		PageGetResponse response = CreateResponse();

		// Act
		PageGetResponse written = writer.WritePageFiles(response, SchemaName, "dev", null, OutputDirectory);

		// Assert
		written.Success.Should().BeFalse(because: "a page whose files could not be written must not report success");
		written.Error.Should().Contain("still using",
			because: "the caller needs to understand that another clio operation holds the page directory, not that the read failed");
	}

	// Stands in for a gate whose lock is held by another clio process for longer than the timeout.
	private sealed class TimingOutFileGate : Clio.Command.McpServer.Tools.IInterprocessFileGate {
		public T Enter<T>(string lockFilePath, System.Func<T> action) =>
			throw new System.TimeoutException($"Timed out waiting for the file lock '{lockFilePath}'.");

		public void Enter(string lockFilePath, System.Action action) =>
			throw new System.TimeoutException($"Timed out waiting for the file lock '{lockFilePath}'.");
	}
}
