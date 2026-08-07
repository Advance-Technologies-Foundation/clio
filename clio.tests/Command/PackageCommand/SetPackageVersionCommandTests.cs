using System;
using System.IO.Abstractions.TestingHelpers;
using Clio.Command.PackageCommand;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Tests.Command.PackageCommand;

[TestFixture]
[Property("Module", "Command")]
public class SetPackageVersionCommandTests : BaseCommandTests<SetPackageVersionOptions> {

	#region Constants: Private

	private const string PackagePath = @"C:\pkg\CrtProcessBuilder";

	private const string DescriptorPath = @"C:\pkg\CrtProcessBuilder\descriptor.json";

	/// <summary>A descriptor as <c>clio compress</c> ships one — the real shape, not a stub.</summary>
	private const string Descriptor = """
		{
		  "Descriptor": {
		    "UId": "f100e6d2-3cd0-a1d8-fbc0-41fce76a538d",
		    "PackageVersion": "1.0.0.0",
		    "Name": "CrtProcessBuilder",
		    "Type": 1,
		    "ProjectPath": "Files/CrtProcessBuilder.csproj",
		    "ModifiedOnUtc": "/Date(1786026213000)/",
		    "Maintainer": "Creatio",
		    "InstallBehavior": 1,
		    "DependsOn": []
		  }
		}
		""";

	#endregion

	#region Fields: Private

	private ILogger _logger;
	private SetPackageVersionCommand _command;

	#endregion

	#region Methods: Private

	private string DescriptorOnDisk() => FileSystem.File.ReadAllText(DescriptorPath);

	private static SetPackageVersionOptions Options(string version) =>
		new() { PackagePath = PackagePath, PackageVersion = version };

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_logger = Substitute.For<ILogger>();
		containerBuilder.AddSingleton(_logger);
	}

	#endregion

	#region Methods: Public

	[SetUp]
	public override void Setup() {
		base.Setup();
		FileSystem.AddFile(DescriptorPath, new MockFileData(Descriptor));
		_command = Container.GetRequiredService<SetPackageVersionCommand>();
	}

	[TearDown]
	public override void TearDown() {
		_logger.ClearReceivedCalls();
		base.TearDown();
	}

	[Test]
	[Description("Writes both the version and a fresh ModifiedOnUtc, because Creatio rewrites the SysPackage row only when the timestamp moves.")]
	public void Execute_ShouldWriteBothVersionAndTimestamp() {
		// Arrange
		string before = DescriptorOnDisk();

		// Act
		int result = _command.Execute(Options("1.0.0.1"));

		// Assert
		string after = DescriptorOnDisk();
		result.Should().Be(0,
			because: "a well-formed request is the success path");
		after.Should().Contain("\"PackageVersion\": \"1.0.0.1\"",
			because: "the requested version is what the environment must record");
		after.Should().NotContain("/Date(1786026213000)/",
			because: "the timestamp is what actually makes the version take effect — Creatio rewrites the "
				+ "SysPackage row only when ModifiedOnUtc CHANGES, so asserting merely that some stamp is "
				+ "present would pass a regression that froze it. Measured on a stand: a descriptor whose "
				+ "version moved while this field did not installed with exit 0 and left the old version "
				+ "recorded");
		after.Should().MatchRegex(@"""ModifiedOnUtc"": ""/Date\(\d+000\)/""",
			because: "ConvertToModifiedOnUtc clears the milliseconds, and that trailing 000 is the provenance "
				+ "oracle the bundled-package guard uses to tell a generated descriptor from a hand-edited one");
		before.Should().NotBe(after,
			because: "the command's whole purpose is to change this file");
	}

	[Test]
	[Description("Accepts the X.Y.Z.W-suffix pre-release form, because every reader of this field parses it through the suffix-aware parser.")]
	public void Execute_ShouldAcceptAPreReleaseSuffix() {
		// Arrange
		// Nothing beyond the descriptor placed by Setup.

		// Act
		int result = _command.Execute(Options("1.0.0.1-rc"));

		// Assert
		result.Should().Be(0,
			because: "descriptor.PackageVersion is READ through PackageVersion.TryParseVersion (PackageInfo, "
				+ "NuGetManager), which models the -suffix form — so validating with System.Version.TryParse "
				+ "made the writer refuse values its own readers accept, breaking pre-release bumps");
		DescriptorOnDisk().Should().Contain("\"PackageVersion\": \"1.0.0.1-rc\"",
			because: "the suffix must survive: NuGetManager compares versions INCLUDING it");
	}

	[TestCase(" 1.0.0.1 ", TestName = "surrounding whitespace")]
	[TestCase("+1.0.0.1", TestName = "leading sign")]
	[TestCase("01.00.00.01", TestName = "leading zeros")]
	[Description("Writes the canonical form rather than the raw argument, so a value that parses cannot reach SysPackage.Version in a shape no string comparison matches.")]
	public void Execute_ShouldWriteTheCanonicalForm(string version) {
		// Arrange
		// Nothing beyond the descriptor placed by Setup.

		// Act
		int result = _command.Execute(Options(version));

		// Assert
		result.Should().Be(0,
			because: "each of these parses — System.Version's components accept surrounding whitespace and a "
				+ "leading sign — so refusing them would be a narrowing with no cause");
		DescriptorOnDisk().Should().Contain("\"PackageVersion\": \"1.0.0.1\"",
			because: "the raw string is what lands in SysPackage.Version, so writing it verbatim would leave a "
				+ "version that compares EQUAL as a Version while failing every string comparison against it — "
				+ "including clio's own archive pin, and a human diffing clio info against list-packages would "
				+ "see two values that look identical. A trailing \\r from a CI variable is the realistic case");
	}

	[Test]
	[Description("Writes a version with fewer than four parts and warns, because clio's own add-package seeds a three-part version and refusing broke existing pipelines.")]
	public void Execute_ShouldWriteAndWarn_WhenTheVersionHasFewerThanFourParts() {
		// Arrange
		// The exact shape `clio add-package` produces, bumped the way a release pipeline would bump it:
		// PackageCreator seeds "0.1.0", so "0.1.1" is what such a pipeline passes.

		// Act
		int result = _command.Execute(Options("0.1.1"));

		// Assert
		result.Should().Be(0,
			because: "an earlier version of this guard REFUSED a short version, which broke a shipped verb for "
				+ "every package clio itself creates: PackageCreator seeds \"0.1.0\" and publish-app writes an "
				+ "app version verbatim, so `add-package` followed by `set-pkg-version -v 0.1.1` returned 1 and "
				+ "left the descriptor untouched. Three parts is normal, not an error");
		DescriptorOnDisk().Should().Contain("\"PackageVersion\": \"0.1.1\"",
			because: "the requested value must be written as asked, not normalised to four parts — padding it "
				+ "would make this command disagree with publish-app, which writes the same value verbatim");
		_logger.Received().WriteWarning(Arg.Is<string>(message =>
			message.Contains("0.1.1") && message.Contains("four")));
		// NSubstitute's Received() takes no `because`; stated here. The hazard is narrow but real — a short
		// version sorts below every four-part version it gets compared against, whether that is a
		// [RequiresPackage] literal or a bundled archive's own version — so it must still be SAID. For the
		// packages clio actually gates on, the four-part invariant is asserted against the shipped archive
		// in BundledProcessBuilderPackageTests, not here.
	}

	[TestCase("", TestName = "empty")]
	[TestCase("   ", TestName = "whitespace only")]
	[TestCase(null, TestName = "absent")]
	[Description("Refuses a missing or blank version and leaves the descriptor byte-identical, instead of erasing the version while still moving the timestamp.")]
	public void Execute_ShouldRefuseAndWriteNothing_WhenNoVersionIsSupplied(string version) {
		// Arrange
		string before = DescriptorOnDisk();

		// Act
		int result = _command.Execute(Options(version));

		// Assert
		result.Should().Be(1,
			because: "the option is not Required on the parser, so the command itself has to refuse — and it "
				+ "must refuse rather than proceed: assigning a null version and then moving ModifiedOnUtc "
				+ "produced a descriptor announcing a change while carrying no version at all, and returned 0 "
				+ "while doing it");
		DescriptorOnDisk().Should().Be(before,
			because: "only the hidden --PackageVersion alias guards against an empty value; -v assigns the main "
				+ "property directly, so without this refusal a blank string would reach the descriptor");
		_logger.Received().WriteError(Arg.Is<string>(message => message.Contains("--package-version")));
	}

	[Test]
	[Description("Refuses a version that does not parse at all, quoting the offending value so the operator can see what was rejected.")]
	public void Execute_ShouldRefuse_WhenTheVersionDoesNotParse() {
		// Arrange
		string before = DescriptorOnDisk();

		// Act
		int result = _command.Execute(Options("abc"));

		// Assert
		result.Should().Be(1,
			because: "an unparseable value satisfies no dependency and no [RequiresPackage] floor, so writing it "
				+ "produces a package that installs and can never be depended upon — a failure that surfaces "
				+ "far from its cause");
		DescriptorOnDisk().Should().Be(before,
			because: "a refusal must not touch the file");
		_logger.Received().WriteError(Arg.Is<string>(message => message.Contains("abc")));
	}

	[Test]
	[Description("Rejects any null collaborator, so a misconfigured DI graph fails at construction rather than while writing a descriptor.")]
	public void Constructor_ShouldRejectNullCollaborators() {
		// Arrange
		IJsonConverter jsonConverter = Container.GetRequiredService<IJsonConverter>();
		IFileSystem fileSystem = Container.GetRequiredService<IFileSystem>();

		// Act & Assert
		Assert.Throws<ArgumentNullException>(
			() => new SetPackageVersionCommand(null, fileSystem, _logger),
			"without a converter the descriptor can be neither read nor written");
		Assert.Throws<ArgumentNullException>(
			() => new SetPackageVersionCommand(jsonConverter, null, _logger),
			"a null file system throws a NullReferenceException at Path.Combine instead — i.e. mid-command, "
			+ "after the caller has been told nothing about which dependency was missing");
		Assert.Throws<ArgumentNullException>(
			() => new SetPackageVersionCommand(jsonConverter, fileSystem, null),
			"without a logger a refusal would be silent, which is worse than the defect the refusal fixes");
	}

	#endregion

}
