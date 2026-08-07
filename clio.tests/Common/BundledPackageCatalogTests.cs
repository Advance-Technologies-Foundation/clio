using System;
using System.IO;
using System.Text;
using Clio.Common;
using Clio.Project.NuGet;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Covers the reader that answers what the running clio distribution carries for a bundled package.
/// </summary>
/// <remarks>
/// The failure branches carry as much weight here as the happy path. This type replaced a compile-time
/// constant, and the whole argument for doing so was that the constant could not be wrong in a way anyone
/// noticed — so every way this reader can fail has to produce a message that says the distribution is
/// broken, never a silent default and never a bare exception.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public class BundledPackageCatalogTests {

	#region Constants: Private

	private const string ExecutingDirectory = "/clio";
	private const string UnbundledPackage = "SomePackageClioDoesNotShip";

	#endregion

	#region Fields: Private

	private IWorkingDirectoriesProvider _workingDirectoriesProvider;
	private IFileSystem _fileSystem;
	private ICompressionUtilities _compressionUtilities;
	private IBundledPackageCatalog _sut;

	#endregion

	#region Methods: Private

	private static byte[] Descriptor(string version) => Encoding.UTF8.GetBytes(
		$"{{\"Descriptor\": {{\"Name\": \"CrtProcessBuilder\", \"PackageVersion\": \"{version}\"}}}}");

	private static byte[] WithBom(byte[] content) {
		byte[] withBom = new byte[content.Length + 3];
		withBom[0] = 0xEF;
		withBom[1] = 0xBB;
		withBom[2] = 0xBF;
		Array.Copy(content, 0, withBom, 3, content.Length);
		return withBom;
	}

	private string ExpectedArchivePath => Path.Combine(
		ExecutingDirectory,
		BundledPackages.ProcessBuilderPackageName,
		BundledPackages.ProcessBuilderArchiveFileName);

	// A null descriptor arranges the ABSENT case: the reader answers false and hands back nothing, which is
	// a different condition from the corrupt archive arranged by the throwing setup further down.
	private void ArrangeArchive(byte[] descriptor) {
		_fileSystem.ExistsFile(ExpectedArchivePath).Returns(true);
		_compressionUtilities
			.TryReadFileFromGZip(ExpectedArchivePath, "descriptor.json", out Arg.Any<byte[]>())
			.Returns(call => { call[2] = descriptor; return descriptor is not null; });
	}

	#endregion

	#region Methods: Public

	[SetUp]
	public void SetUp() {
		_workingDirectoriesProvider = Substitute.For<IWorkingDirectoriesProvider>();
		_workingDirectoriesProvider.ExecutingDirectory.Returns(ExecutingDirectory);
		_fileSystem = Substitute.For<IFileSystem>();
		_compressionUtilities = Substitute.For<ICompressionUtilities>();
		_sut = new BundledPackageCatalog(_workingDirectoriesProvider, _fileSystem, _compressionUtilities);
	}

	[Test]
	[Description("The catalog resolves the archive from the EXECUTING directory, because that is the copy an install actually ships — the repository copy can and did differ from it.")]
	public void GetArchivePath_ShouldResolveUnderTheExecutingDirectory_WhenPackageIsBundled() {
		// Arrange & Act
		string path = _sut.GetArchivePath(BundledPackages.ProcessBuilderPackageName);

		// Assert
		path.Should().Be(ExpectedArchivePath,
			because: "the install command resolves the same path through this catalog, so a version reported "
				+ "from anywhere else would describe bytes that will not be installed");
	}

	[Test]
	[Description("Asking for the archive of a package clio does not ship is a programming error, so it throws rather than returning a path that can never exist.")]
	public void GetArchivePath_ShouldThrow_WhenPackageIsNotBundled() {
		// Arrange & Act
		Action act = () => _sut.GetArchivePath(UnbundledPackage);

		// Assert
		act.Should().Throw<ArgumentException>(
			because: "returning a plausible-looking path for a package clio does not carry would turn a coding "
				+ "mistake into a 'file not found' that reads like a broken distribution");
	}

	[Test]
	[Description("IsBundled is the predicate that decides whether a package is subject to convergence, so it must answer false for anything clio does not ship — including null and blank.")]
	[TestCase(UnbundledPackage, false)]
	[TestCase("", false)]
	[TestCase(null, false)]
	[TestCase("CrtProcessBuilder", true)]
	[TestCase("crtprocessbuilder", true)]
	public void IsBundled_ShouldAnswerWhetherClioShipsThePackage(string packageName, bool expected) {
		// Arrange & Act
		bool isBundled = _sut.IsBundled(packageName);

		// Assert
		isBundled.Should().Be(expected,
			because: "convergence is applied to exactly the packages clio ships, and the name arrives from a "
				+ "[RequiresPackage] literal whose casing nobody guarantees");
	}

	[Test]
	[Description("The shipped descriptor carries a UTF-8 BOM, which System.Text.Json rejects outright — so the reader must strip it or the version is unreadable on the real archive.")]
	public void TryGetVersion_ShouldReadTheVersion_WhenDescriptorCarriesAByteOrderMark() {
		// Arrange
		ArrangeArchive(WithBom(Descriptor("1.2.3.4")));

		// Act
		bool read = _sut.TryGetVersion(
			BundledPackages.ProcessBuilderPackageName, out PackageVersion version, out string diagnosis);

		// Assert
		read.Should().BeTrue(because: $"the descriptor is well-formed; diagnosis was '{diagnosis}'");
		version.ToString().Should().Be("1.2.3.4",
			because: "this is the value clio info prints and the convergence rule compares against");
	}

	[Test]
	[Description("A second read must not decompress the archive again: the file cannot change under a running process, and on the MCP path this sits on the hot path of every gated tool call.")]
	public void TryGetVersion_ShouldReadTheArchiveOnce_WhenAskedRepeatedly() {
		// Arrange
		ArrangeArchive(Descriptor("1.2.3.4"));

		// Act
		_sut.TryGetVersion(BundledPackages.ProcessBuilderPackageName, out _, out _);
		_sut.TryGetVersion(BundledPackages.ProcessBuilderPackageName, out PackageVersion version, out _);

		// Assert
		version.ToString().Should().Be("1.2.3.4",
			because: "the cached answer must be the one that was read, not a stale default");
		_compressionUtilities.Received(1).TryReadFileFromGZip(
			ExpectedArchivePath, "descriptor.json", out Arg.Any<byte[]>());
	}

	[Test]
	[Description("A failed read must NOT be cached: an archive still being written, or a distribution repaired mid-session, must not stay broken for the life of a long-running MCP server.")]
	public void TryGetVersion_ShouldRetry_WhenThePreviousReadFailed() {
		// Arrange
		_fileSystem.ExistsFile(ExpectedArchivePath).Returns(false);
		_sut.TryGetVersion(BundledPackages.ProcessBuilderPackageName, out _, out _);
		ArrangeArchive(Descriptor("1.2.3.4"));

		// Act
		bool read = _sut.TryGetVersion(
			BundledPackages.ProcessBuilderPackageName, out PackageVersion version, out _);

		// Assert
		read.Should().BeTrue(
			because: "caching the failure would make a transient condition permanent for the whole process");
		version.ToString().Should().Be("1.2.3.4",
			because: "the retry must return the version that is now readable");
	}

	[Test]
	[Description("A missing archive is a broken distribution, so the diagnosis must name the path and say reinstalling clio is the remedy — not suggest anything the user can do to their environment.")]
	public void TryGetVersion_ShouldDiagnoseTheDistribution_WhenArchiveIsMissing() {
		// Arrange
		_fileSystem.ExistsFile(ExpectedArchivePath).Returns(false);

		// Act
		bool read = _sut.TryGetVersion(
			BundledPackages.ProcessBuilderPackageName, out PackageVersion version, out string diagnosis);

		// Assert
		read.Should().BeFalse(because: "there is no archive to read a version from");
		version.Should().BeNull(because: "no version may be invented when none could be read");
		diagnosis.Should().Contain(ExpectedArchivePath,
			because: "the reader has to be told WHICH file is missing to act on it");
		diagnosis.Should().Contain("clio itself",
			because: "the remedy is reinstalling clio; nothing the user does to their environment can help");
	}

	[Test]
	[Description("An archive that carries no descriptor is reported as a broken distribution rather than as a missing package, because the two have different remedies.")]
	public void TryGetVersion_ShouldDiagnose_WhenArchiveHasNoDescriptor() {
		// Arrange
		ArrangeArchive(null);

		// Act
		bool read = _sut.TryGetVersion(
			BundledPackages.ProcessBuilderPackageName, out PackageVersion version, out string diagnosis);

		// Assert
		read.Should().BeFalse(because: "an archive without a descriptor cannot state a version");
		version.Should().BeNull(because: "no version may be invented when none could be read");
		diagnosis.Should().Contain("descriptor.json",
			because: "naming the missing entry is what distinguishes this from a missing archive");
	}

	[Test]
	[Description("A malformed descriptor must surface as a readable diagnosis, not as a JsonException escaping into a gated command's dispatch path.")]
	public void TryGetVersion_ShouldDiagnose_WhenDescriptorIsNotValidJson() {
		// Arrange
		ArrangeArchive(Encoding.UTF8.GetBytes("{ this is not json"));

		// Act
		bool read = _sut.TryGetVersion(
			BundledPackages.ProcessBuilderPackageName, out PackageVersion version, out string diagnosis);

		// Assert
		read.Should().BeFalse(because: "a malformed descriptor states no version");
		version.Should().BeNull(because: "no version may be invented when none could be read");
		diagnosis.Should().Contain("malformed",
			because: "the caller prints this to a user, so it must read as a sentence rather than as a stack");
	}

	[Test]
	[Description("A descriptor whose PackageVersion is absent or unparseable is diagnosed rather than silently treated as version zero, which would make every environment look converged.")]
	[TestCase("{\"Descriptor\": {\"Name\": \"CrtProcessBuilder\"}}")]
	[TestCase("{\"Descriptor\": {\"PackageVersion\": \"not-a-version\"}}")]
	[TestCase("{\"Descriptor\": {\"PackageVersion\": 1}}")]
	[TestCase("{\"Name\": \"CrtProcessBuilder\", \"PackageVersion\": \"1.2.3.4\"}")]
	// The four below are valid JSON of the WRONG SHAPE, and each is a case where JsonElement.TryGetProperty
	// THROWS InvalidOperationException instead of returning false. That is not a JsonException, so before
	// the ValueKind guards were added it escaped this class entirely: `clio info` threw, and the convergence
	// path turned a merely malformed archive into a hard refusal of every gated command — the exact opposite
	// of what an unreadable distribution is supposed to do. Every earlier case here was an object, which is
	// why they all passed while the bug was live.
	[TestCase("[{\"Descriptor\": {\"PackageVersion\": \"1.2.3.4\"}}]")]
	[TestCase("\"1.2.3.4\"")]
	[TestCase("{\"Descriptor\": null}")]
	[TestCase("{\"Descriptor\": []}")]
	public void TryGetVersion_ShouldDiagnose_WhenDescriptorHasNoUsableVersion(string descriptorJson) {
		// Arrange
		ArrangeArchive(Encoding.UTF8.GetBytes(descriptorJson));

		// Act
		bool read = _sut.TryGetVersion(
			BundledPackages.ProcessBuilderPackageName, out PackageVersion version, out string diagnosis);

		// Assert
		read.Should().BeFalse(
			because: "a version that cannot be read must not degrade into a default: a zero would compare "
				+ "below every installed version and quietly declare every environment converged");
		version.Should().BeNull(because: "no version may be invented when none could be read");
		diagnosis.Should().NotBeNullOrWhiteSpace(because: "silence is the one unacceptable outcome here");
	}

	[Test]
	[Description("Asking for the version of a package clio does not ship throws rather than reporting a diagnosis, because it is a programming error — callers are expected to gate on IsBundled, and the convergence rule does.")]
	public void TryGetVersion_ShouldThrow_WhenPackageIsNotBundled() {
		// Arrange & Act
		Action act = () => _sut.TryGetVersion(UnbundledPackage, out PackageVersion _, out string _);

		// Assert
		act.Should().Throw<ArgumentException>(
			because: "a diagnosis would read as 'this distribution is broken', when in fact the caller asked "
				+ "for a package clio was never supposed to carry");
	}

	[Test]
	[Description("An exception from the archive reader is converted into a diagnosis, because it reaches the user through a gated command's refusal path where a raw stack is unreadable.")]
	public void TryGetVersion_ShouldDiagnose_WhenTheReaderThrows() {
		// Arrange
		_fileSystem.ExistsFile(ExpectedArchivePath).Returns(true);
		_compressionUtilities
			.TryReadFileFromGZip(ExpectedArchivePath, "descriptor.json", out Arg.Any<byte[]>())
			.Returns(_ => throw new InvalidDataException("the gzip member is truncated"));

		// Act
		bool read = _sut.TryGetVersion(
			BundledPackages.ProcessBuilderPackageName, out PackageVersion version, out string diagnosis);

		// Assert
		read.Should().BeFalse(because: "an unreadable archive states no version");
		version.Should().BeNull(because: "no version may be invented when none could be read");
		diagnosis.Should().Contain("truncated",
			because: "the cause is kept so a broken distribution can actually be diagnosed, but wrapped in a "
				+ "sentence rather than thrown");
	}

	#endregion

}
