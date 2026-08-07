using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Compression;
using System.Text;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;
using IClioFileSystem = Clio.Common.IFileSystem;

namespace Clio.Tests.Common;

/// <summary>
/// Covers <see cref="ICompressionUtilities.ReadFileFromGZip"/> — the walk over clio's own package container
/// format that reads a single entry without unpacking to disk.
/// </summary>
/// <remarks>
/// Every case here is built from raw bytes rather than by packing files, because the interesting half of
/// this method is what it does with a container it CANNOT trust. The format is
/// <c>[int32 nameLength][UTF-16LE path][int32 contentLength][bytes]</c> repeated to the end of the stream,
/// with no terminator and no checksum — so a corrupt length prefix is indistinguishable from data until it
/// is bounds-checked, and that check is the thing under test.
/// <para>
/// This matters beyond the one caller that reads a bundled descriptor: the same private helpers serve
/// <see cref="ICompressionUtilities.UnpackFromGZip"/>, which unpacks archives a user was handed
/// (<c>extract-pkg-zip</c>) or that a remote Creatio instance returned.
/// </para>
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class CompressionUtilitiesReadFileTests {

	#region Constants: Private

	private const string ArchivePath = "/archive.gz";

	#endregion

	#region Fields: Private

	private MockFileSystem _mockFileSystem;
	private ICompressionUtilities _sut;

	#endregion

	#region Methods: Private

	// Writes one entry in the container's own layout.
	private static void WriteEntry(Stream stream, string name, byte[] content) {
		byte[] nameBytes = Encoding.Unicode.GetBytes(name);
		stream.Write(BitConverter.GetBytes(name.Length));
		stream.Write(nameBytes);
		stream.Write(BitConverter.GetBytes(content.Length));
		stream.Write(content);
	}

	private void ArrangeArchive(Action<Stream> writeBody) {
		using MemoryStream raw = new();
		writeBody(raw);
		using MemoryStream compressed = new();
		using (GZipStream gzip = new(compressed, CompressionMode.Compress, leaveOpen: true)) {
			gzip.Write(raw.ToArray());
		}
		_mockFileSystem.AddFile(ArchivePath, new MockFileData(compressed.ToArray()));
	}

	private void ArrangeArchive(params (string Name, string Content)[] entries) =>
		ArrangeArchive(stream => {
			foreach ((string name, string content) in entries) {
				WriteEntry(stream, name, Encoding.UTF8.GetBytes(content));
			}
		});

	#endregion

	#region Methods: Public

	[SetUp]
	public void SetUp() {
		_mockFileSystem = new MockFileSystem();
		IClioFileSystem fileSystem = new Clio.Common.FileSystem(_mockFileSystem);
		_sut = new CompressionUtilities(fileSystem, new ZipFileWrapper());
	}

	[Test]
	[Description("The wanted entry is returned when it is the first in the archive, which is the shape the shipped package happens to have.")]
	public void ReadFileFromGZip_ShouldReturnTheEntry_WhenItIsFirst() {
		// Arrange
		ArrangeArchive(("descriptor.json", "{\"a\":1}"), ("Files/x.cs", "class X {}"));

		// Act
		byte[] content = _sut.ReadFileFromGZip(ArchivePath, "descriptor.json");

		// Assert
		Encoding.UTF8.GetString(content).Should().Be("{\"a\":1}",
			because: "the reader must return the wanted entry's bytes exactly, with no length prefix attached");
	}

	[Test]
	[Description("The wanted entry is found after other entries, exercising the skip path — the shipped archive stores the descriptor first, so no test over the real archive can reach this.")]
	public void ReadFileFromGZip_ShouldReturnTheEntry_WhenItFollowsOtherEntries() {
		// Arrange
		ArrangeArchive(
			("Files/a.cs", "aaaa"),
			("Files/Libs/big.dll", new string('x', 5000)),
			("descriptor.json", "{\"found\":true}"));

		// Act
		byte[] content = _sut.ReadFileFromGZip(ArchivePath, "descriptor.json");

		// Assert
		Encoding.UTF8.GetString(content).Should().Be("{\"found\":true}",
			because: "skipping an entry must advance by exactly its declared content length; an off-by-one "
				+ "there would read every later entry as garbage");
	}

	[Test]
	[Description("An archive read cleanly to its end without the wanted entry returns null, which the caller reads as ABSENT rather than as damage.")]
	public void ReadFileFromGZip_ShouldReturnNull_WhenEntryIsAbsent() {
		// Arrange
		ArrangeArchive(("Files/a.cs", "aaaa"), ("Files/b.cs", "bbbb"));

		// Act
		byte[] content = _sut.ReadFileFromGZip(ArchivePath, "descriptor.json");

		// Assert
		content.Should().BeNull(
			because: "null means absent and nothing else - the caller reports a missing descriptor "
				+ "differently from a corrupt archive, and the two have different remedies");
	}

	[Test]
	[Description("Entry paths are matched independently of separator flavour and case, because the archive stores whatever the packing host used and clio runs on all three platforms.")]
	[TestCase("Files/Libs/ErrorOr.dll", "Files/Libs/ErrorOr.dll")]
	[TestCase("Files\\Libs\\ErrorOr.dll", "Files/Libs/ErrorOr.dll")]
	[TestCase("Files/Libs/ErrorOr.dll", "Files\\Libs\\ErrorOr.dll")]
	[TestCase("descriptor.json", "DESCRIPTOR.JSON")]
	[TestCase("descriptor.json", "/descriptor.json")]
	public void ReadFileFromGZip_ShouldMatchTheEntry_RegardlessOfSeparatorFlavourAndCase(
		string storedName, string requestedName) {
		// Arrange
		ArrangeArchive((storedName, "payload"));

		// Act
		byte[] content = _sut.ReadFileFromGZip(ArchivePath, requestedName);

		// Assert
		Encoding.UTF8.GetString(content).Should().Be("payload",
			because: "an archive packed on Windows and read on Linux (or the reverse) must resolve the same "
				+ "entry, and the caller should not have to know which host produced it");
	}

	[Test]
	[Description("A skipped entry declaring a content length longer than the bytes remaining is corruption, and must throw rather than seek past the end and read every later entry as garbage.")]
	public void ReadFileFromGZip_ShouldThrow_WhenASkippedEntryDeclaresAnImpossibleLength() {
		// Arrange
		ArrangeArchive(stream => {
			byte[] nameBytes = Encoding.Unicode.GetBytes("Files/a.cs");
			stream.Write(BitConverter.GetBytes("Files/a.cs".Length));
			stream.Write(nameBytes);
			stream.Write(BitConverter.GetBytes(int.MaxValue));
			stream.Write(Encoding.UTF8.GetBytes("short"));
		});

		// Act
		Action act = () => _sut.ReadFileFromGZip(ArchivePath, "descriptor.json");

		// Assert
		act.Should().Throw<InvalidDataException>(
			because: "returning null here would tell the caller the archive simply has no descriptor, when in "
				+ "fact it cannot be parsed at all");
	}

	[Test]
	[Description("The wanted entry being truncated is corruption too, and must not be reported as a shorter file.")]
	public void ReadFileFromGZip_ShouldThrow_WhenTheWantedEntryIsTruncated() {
		// Arrange
		ArrangeArchive(stream => {
			byte[] nameBytes = Encoding.Unicode.GetBytes("descriptor.json");
			stream.Write(BitConverter.GetBytes("descriptor.json".Length));
			stream.Write(nameBytes);
			stream.Write(BitConverter.GetBytes(1000));
			stream.Write(Encoding.UTF8.GetBytes("only a few bytes"));
		});

		// Act
		Action act = () => _sut.ReadFileFromGZip(ArchivePath, "descriptor.json");

		// Assert
		act.Should().Throw<InvalidDataException>(
			because: "silently returning the truncated bytes would hand the caller a half-file that parses to "
				+ "something plausible");
	}

	[Test]
	[Description("An entry name length larger than the bytes remaining must be rejected immediately, not driven as a read loop — an unbounded name length is billions of iterations and gigabytes of allocation from four bytes of corruption, on a path that unpacks archives clio did not produce.")]
	public void ReadFileFromGZip_ShouldThrow_WhenAnEntryNameLengthExceedsTheRemainingBytes() {
		// Arrange
		ArrangeArchive(stream => {
			stream.Write(BitConverter.GetBytes(0x2000_0000));
			stream.Write(Encoding.Unicode.GetBytes("short"));
		});

		// Act
		Action act = () => _sut.ReadFileFromGZip(ArchivePath, "descriptor.json");

		// Assert
		// The bound must make this immediate. Without it the loop appends up to 2^29 characters before the
		// stream runs out, so a test that merely asserted the outcome would hang rather than fail.
		act.Should().Throw<InvalidDataException>(
			because: "bytes remain that cannot be parsed, so this is a damaged container, and it must be "
				+ "refused in constant time rather than by exhausting the declared length");
	}

	[Test]
	[Description("A negative declared length is refused like any other incredible one, because new byte[negative] would throw OverflowException out of the reader instead of a readable diagnosis.")]
	public void ReadFileFromGZip_ShouldThrow_WhenAnEntryDeclaresANegativeLength() {
		// Arrange
		ArrangeArchive(stream => {
			stream.Write(BitConverter.GetBytes(-1));
			stream.Write(Encoding.UTF8.GetBytes("trailing"));
		});

		// Act
		Action act = () => _sut.ReadFileFromGZip(ArchivePath, "descriptor.json");

		// Assert
		act.Should().Throw<InvalidDataException>(
			because: "a negative length is corruption, and the caller needs it as a stated condition rather "
				+ "than as an OverflowException from an allocation");
	}

	[Test]
	[Description("Both arguments are validated, so a caller passing an empty path gets an argument error rather than a file-not-found from deep inside the reader.")]
	[TestCase(null, "descriptor.json")]
	[TestCase("", "descriptor.json")]
	[TestCase(ArchivePath, null)]
	[TestCase(ArchivePath, "   ")]
	public void ReadFileFromGZip_ShouldThrow_WhenAnArgumentIsMissing(string archivePath, string entryPath) {
		// Arrange
		ArrangeArchive(("descriptor.json", "{}"));

		// Act
		Action act = () => _sut.ReadFileFromGZip(archivePath, entryPath);

		// Assert
		act.Should().Throw<ArgumentException>(
			because: "an empty argument is a programming error and must be named as one");
	}

	[Test]
	[Description("Packing and reading back agree about the format, so the reader cannot drift from the writer it shares a file with.")]
	public void ReadFileFromGZip_ShouldReadWhatPackToGZipWrote() {
		// Arrange
		_mockFileSystem.AddFile("/src/descriptor.json", new MockFileData("{\"round\":\"trip\"}"));
		_mockFileSystem.AddFile("/src/Files/a.cs", new MockFileData("class A {}"));
		List<string> files = ["/src/descriptor.json", "/src/Files/a.cs"];

		// Act
		_sut.PackToGZip(files, "/src", ArchivePath);
		byte[] content = _sut.ReadFileFromGZip(ArchivePath, "descriptor.json");

		// Assert
		Encoding.UTF8.GetString(content).Should().Be("{\"round\":\"trip\"}",
			because: "the reader and the writer live in one class and must agree; a change to either that "
				+ "broke the other would otherwise only surface against the committed binary archive");
	}

	#endregion

}
