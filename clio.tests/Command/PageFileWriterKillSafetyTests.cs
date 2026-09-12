using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// ENG-95262 — <c>get-page</c> ships in the stage-6 worker cohort, so it is bounded by the parent
/// KILLING the worker. A kill runs no <c>finally</c>, so whatever is on disk between two filesystem
/// operations is what the user is left with. These tests pin the publication invariant that makes that
/// admissible: <c>.clio-pages/{schema}/</c> is never observable in a partial state.
/// <para>
/// The state that motivates them is not merely "untidy". <c>meta.json</c> is written LAST, so a kill
/// after <c>body.js</c> leaves a directory that reads as a successful get-page while carrying NO
/// conflict baseline — and <see cref="Clio.Command.PageBaselineStore"/> then answers "no baseline",
/// so the next <c>update-page</c> runs with no expected checksum and can silently overwrite an
/// external change. Nothing repairs that and nothing reports it.
/// </para>
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageFileWriterKillSafetyTests {

	private const string SchemaName = "Usr_FormPage";
	// Deliberately a PREFIX of SchemaName: a residue purge that matched on a prefix rather than on a
	// whole path segment would reach into this schema's in-flight staging from the other schema's gate.
	private const string SiblingSchemaName = "Usr_Form";
	private const string OutputDirectory = "/ws";
	private const string PreviousBody = "define(\"Usr_FormPage\", [], function() { return \"previous-generation\"; });";
	private const string PublishedBody = "define(\"Usr_FormPage\", [], function() { return \"published-generation\"; });";
	private const string PreviousChecksum = "checksum-previous";
	private const string PublishedChecksum = "checksum-published";

	private MockFileSystem _fileSystem;
	private InterruptionObservingFileSystem _observing;
	private PageFileWriter _writer;
	private List<IReadOnlyDictionary<string, string>> _snapshots;
	private string _clioPagesDir;
	private string _schemaDir;
	private string _stagingDir;

	[SetUp]
	public void SetUp() {
		_fileSystem = new MockFileSystem();
		_snapshots = [];
		_observing = new InterruptionObservingFileSystem(_fileSystem, CaptureSnapshot);
		_writer = new PageFileWriter(_observing.FileSystem, new RecordingFileGate());
		_clioPagesDir = _fileSystem.Path.Combine(_fileSystem.Path.GetFullPath(OutputDirectory), ".clio-pages");
		_schemaDir = _fileSystem.Path.Combine(_clioPagesDir, SchemaName);
		_stagingDir = _fileSystem.Path.Combine(_clioPagesDir, ".staging");
	}

	[Test]
	[Description("A kill landing between any two filesystem operations of get-page's publication must leave .clio-pages/{schema}/ either absent or complete — never an existing directory with files missing, and never a mixture of the previous and the published generation.")]
	public void WritePageFiles_ShouldLeaveTheSchemaDirectoryAbsentOrComplete_WhenInterruptedAtAnyPoint() {
		// Arrange
		SeedPreviousGeneration();
		PageGetResponse response = CreateResponse(PublishedBody, PublishedChecksum);

		// Act — a snapshot before the first operation and after the last one bracket the samples the
		// observing file system takes between every pair of operations.
		CaptureSnapshot();
		PageGetResponse written = _writer.WritePageFiles(response, SchemaName, "dev", null, OutputDirectory);
		CaptureSnapshot();

		// Assert
		written.Success.Should().BeTrue(because: "the publication itself must still succeed; kill-safety is not bought by failing");
		_snapshots.Count.Should().BeGreaterThan(3,
			because: "the publication performs several observable filesystem operations, so anything fewer means the interruption points were not sampled at all");
		foreach (IReadOnlyDictionary<string, string> snapshot in _snapshots) {
			if (snapshot is null) {
				continue; // an absent directory is the honest 'never fetched' state and self-heals on retry
			}
			snapshot.Keys.Should().BeEquivalentTo(InterruptionObservingFileSystem.PublishedPageFiles,
				because: "an existing .clio-pages/{schema} that is missing files reads to the user and to update-page as a successful get-page — with meta.json written last, that state silently disarms conflict detection for good");
			bool bodyIsPublished = snapshot["body.js"] == PublishedBody;
			bool metaIsPublished = snapshot["meta.json"].Contains(PublishedChecksum, StringComparison.Ordinal);
			bool bundleIsPublished = snapshot["bundle.json"].Contains("published-generation", StringComparison.Ordinal);
			metaIsPublished.Should().Be(bodyIsPublished,
				because: "a baseline captured against one generation of body.js while the other generation is on disk is exactly the false-or-missing conflict signal the baseline exists to prevent");
			bundleIsPublished.Should().Be(bodyIsPublished,
				because: "the merged hierarchy view and the editable body must always describe the same fetch");
		}
	}

	[Test]
	[Description("get-page must write no file content inside the published .clio-pages/{schema}/ directory, so a kill landing mid-write of any single file cannot truncate a published file.")]
	public void WritePageFiles_ShouldWriteNoContentInsideThePublishedDirectory_WhenPublishing() {
		// Arrange
		SeedPreviousGeneration();
		PageGetResponse response = CreateResponse(PublishedBody, PublishedChecksum);

		// Act
		PageGetResponse written = _writer.WritePageFiles(response, SchemaName, "dev", null, OutputDirectory);

		// Assert
		written.Success.Should().BeTrue(because: "the publication must succeed for its write targets to be meaningful");
		_observing.ContentWriteTargets.Should().NotBeEmpty(because: "the page files must actually be written somewhere");
		string publishedPrefix = _fileSystem.Path.GetFullPath(_schemaDir) + _fileSystem.Path.DirectorySeparatorChar;
		foreach (string target in _observing.ContentWriteTargets) {
			_fileSystem.Path.GetFullPath(target).Should().NotStartWith(publishedPrefix,
				because: "content written straight into the published directory can be observed half-written by a kill, which no snapshot between operations can catch — the files must be completed elsewhere and moved into place");
		}
	}

	[Test]
	[Description("A completed get-page must publish the new generation and drop files left by the previous one, so the schema directory is a replacement rather than a merge.")]
	public void WritePageFiles_ShouldReplaceThePreviousGenerationWholesale_WhenTheWriteSucceeds() {
		// Arrange
		SeedPreviousGeneration();
		_fileSystem.AddFile(_fileSystem.Path.Combine(_schemaDir, "stale.js"), new MockFileData("left by an older fetch"));
		PageGetResponse response = CreateResponse(PublishedBody, PublishedChecksum);

		// Act
		PageGetResponse written = _writer.WritePageFiles(response, SchemaName, "dev", null, OutputDirectory);

		// Assert
		written.Success.Should().BeTrue(because: "publishing over a previous generation must succeed");
		_fileSystem.File.ReadAllText(_fileSystem.Path.Combine(_schemaDir, "body.js")).Should().Be(PublishedBody,
			because: "the published body must be the one just fetched, not the previous generation");
		_fileSystem.File.Exists(_fileSystem.Path.Combine(_schemaDir, "stale.js")).Should().BeFalse(
			because: "get-page replaces the schema directory wholesale, so a file from an older fetch must not survive the publication");
	}

	[Test]
	[Description("A completed get-page must leave no staging or retired directory behind, so repeated fetches do not accumulate copies of the page tree.")]
	public void WritePageFiles_ShouldLeaveNoStagingResidue_WhenTheWriteSucceeds() {
		// Arrange
		SeedPreviousGeneration();
		PageGetResponse response = CreateResponse(PublishedBody, PublishedChecksum);

		// Act
		PageGetResponse written = _writer.WritePageFiles(response, SchemaName, "dev", null, OutputDirectory);

		// Assert
		written.Success.Should().BeTrue(because: "the publication must succeed for its residue to be meaningful");
		string stagingPrefix = _fileSystem.Path.GetFullPath(_stagingDir) + _fileSystem.Path.DirectorySeparatorChar;
		_fileSystem.AllFiles
			.Where(path => _fileSystem.Path.GetFullPath(path).StartsWith(stagingPrefix, StringComparison.Ordinal))
			.Should().BeEmpty(because: "a successful publication cleans up after itself; only a kill may leave staging behind");
	}

	[Test]
	[Description("get-page must clear staging residue left by an interrupted run of the SAME schema, and must not touch residue belonging to another schema whose name it merely starts with.")]
	public void WritePageFiles_ShouldPurgeOnlyItsOwnSchemaResidue_WhenAPreviousRunWasInterrupted() {
		// Arrange — residue of this schema's own interrupted run, and of a sibling whose name is a prefix of it.
		string ownResidue = _fileSystem.Path.Combine(_stagingDir, SchemaName, "interrupted", "body.js");
		string siblingResidue = _fileSystem.Path.Combine(_stagingDir, SiblingSchemaName, "in-flight", "body.js");
		_fileSystem.AddFile(ownResidue, new MockFileData("half-published by a killed worker"));
		_fileSystem.AddFile(siblingResidue, new MockFileData("another schema's publication, in flight right now"));
		PageGetResponse response = CreateResponse(PublishedBody, PublishedChecksum);

		// Act
		PageGetResponse written = _writer.WritePageFiles(response, SchemaName, "dev", null, OutputDirectory);

		// Assert
		written.Success.Should().BeTrue(because: "residue from an earlier kill must not block the retry that repairs it");
		_fileSystem.File.Exists(ownResidue).Should().BeFalse(
			because: "residue of this schema's own interrupted publication is covered by the gate this call holds, so the retry is the natural place to clear it");
		_fileSystem.File.Exists(siblingResidue).Should().BeTrue(
			because: "another schema's staging is guarded by another schema's gate — reaching into it would delete a publication that is in flight right now");
	}

	// -------------------------------------------------------------------------------------------

	private void SeedPreviousGeneration() {
		_fileSystem.AddFile(_fileSystem.Path.Combine(_schemaDir, "body.js"), new MockFileData(PreviousBody));
		_fileSystem.AddFile(_fileSystem.Path.Combine(_schemaDir, "bundle.json"),
			new MockFileData("{\"name\":\"previous-generation\"}"));
		_fileSystem.AddFile(_fileSystem.Path.Combine(_schemaDir, "meta.json"),
			new MockFileData(JsonSerializer.Serialize(new PageMetaFileModel {
				FetchedAt = "2026-08-01T00:00:00.0000000Z",
				Page = new PageMetadataInfo { SchemaName = SchemaName },
				Baseline = new PageBaselineInfo {
					SchemaName = SchemaName,
					EnvironmentName = "dev",
					EditableSchemaExists = true,
					Checksum = PreviousChecksum,
					CapturedAt = "2026-08-01T00:00:00.0000000Z"
				}
			})));
	}

	private void CaptureSnapshot() {
		if (!_fileSystem.Directory.Exists(_schemaDir)) {
			_snapshots.Add(null);
			return;
		}
		Dictionary<string, string> files = new(StringComparer.Ordinal);
		foreach (string path in _fileSystem.Directory.GetFiles(_schemaDir)) {
			files[_fileSystem.Path.GetFileName(path)] = _fileSystem.File.ReadAllText(path);
		}
		_snapshots.Add(files);
	}

	private static PageGetResponse CreateResponse(string body, string checksum) =>
		new() {
			Success = true,
			Page = new PageMetadataInfo { SchemaName = SchemaName },
			Bundle = new PageBundleInfo { Name = "published-generation" },
			Raw = new PageRawInfo { Body = body },
			Editable = new PageEditableSchemaInfo {
				EditableSchemaExists = true,
				EditableSchemaUId = "11111111-2222-3333-4444-555555555555",
				Checksum = checksum,
				ModifiedOn = "2026-08-18T09:00:00"
			}
		};
}
