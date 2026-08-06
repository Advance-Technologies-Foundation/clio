using Clio.Command.PackageCommand;
using Clio.Common;
using Clio.Package;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Tests.Command.PackageCommand;

[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public class SetPackageVersionCommandTests {

	#region Constants: Private

	private const string PackagePath = "packages/CrtProcessBuilder";

	private const string DescriptorPath = "packages/CrtProcessBuilder/descriptor.json";

	#endregion

	#region Fields: Private

	private IJsonConverter _jsonConverter;
	private IFileSystem _fileSystem;
	private ILogger _logger;
	private SetPackageVersionCommand _command;

	#endregion

	#region Methods: Public

	[SetUp]
	public void Setup() {
		_jsonConverter = Substitute.For<IJsonConverter>();
		_fileSystem = Substitute.For<IFileSystem>();
		_logger = Substitute.For<ILogger>();
		_fileSystem.Path.Combine(PackagePath, CreatioPackage.DescriptorName).Returns(DescriptorPath);
		_jsonConverter
			.DeserializeObjectFromFile<PackageDescriptorDto>(Arg.Any<string>())
			.Returns(new PackageDescriptorDto {
				Descriptor = new PackageDescriptor { Name = "CrtProcessBuilder", PackageVersion = "1.1.0.1" }
			});
		_command = new SetPackageVersionCommand(_jsonConverter, _fileSystem, _logger);
	}

	[TearDown]
	public void TearDown() {
		_jsonConverter.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Description("Writes both the version and a fresh ModifiedOnUtc, because Creatio rewrites the SysPackage row only when the timestamp moves.")]
	public void Execute_ShouldWriteBothVersionAndTimestamp() {
		// Arrange
		PackageDescriptorDto written = null;
		_jsonConverter
			.When(converter => converter.SerializeObjectToFile(Arg.Any<object>(), DescriptorPath))
			// Positional, not Arg<object>(): both parameters are reference types, so a type-based lookup is
			// ambiguous and throws inside the callback.
			.Do(call => written = call[0] as PackageDescriptorDto);

		// Act
		int result = _command.Execute(new SetPackageVersionOptions {
			PackagePath = PackagePath,
			PackageVersion = "1.1.0.2"
		});

		// Assert
		result.Should().Be(0,
			because: "a well-formed request is the success path");
		written.Descriptor.PackageVersion.Should().Be("1.1.0.2",
			because: "the requested version is what the environment must record");
		written.Descriptor.ModifiedOnUtc.Should().NotBe("/Date(0)/").And.EndWith("000)/",
			because: "the timestamp is what actually makes the version take effect — Creatio rewrites the "
				+ "SysPackage row only when ModifiedOnUtc changes — and ConvertToModifiedOnUtc clears the "
				+ "milliseconds, which is the provenance oracle the bundled-package guard relies on to tell a "
				+ "generated descriptor from a hand-edited one");
	}

	[Test]
	[Description("Refuses and touches nothing when no version is supplied, instead of erasing the version while still moving the timestamp.")]
	public void Execute_ShouldRefuseAndWriteNothing_WhenNoVersionIsSupplied() {
		// Act
		int result = _command.Execute(new SetPackageVersionOptions { PackagePath = PackagePath });

		// Assert
		result.Should().Be(1,
			because: "the option is not Required on the parser, so the command itself has to refuse — and it must "
				+ "refuse rather than proceed: assigning a null version and then moving ModifiedOnUtc produced a "
				+ "descriptor announcing a change while carrying no version at all, and returned 0 while doing it");
		// NSubstitute's DidNotReceive() takes no `because`; stated here. The refusal has to happen BEFORE the
		// write, because the damage is the write: the descriptor is the single source of this package's version,
		// and restoring it afterwards means recovering the value from git.
		_jsonConverter.DidNotReceive().SerializeObjectToFile(Arg.Any<object>(), Arg.Any<string>());
		_logger.Received().WriteError(Arg.Is<string>(message => message.Contains("--package-version")));
	}

	[TestCase("")]
	[TestCase("   ")]
	[Description("Treats an empty or whitespace version the same as an absent one, since the main option has no guard of its own.")]
	public void Execute_ShouldRefuse_WhenTheVersionIsBlank(string version) {
		// Act
		int result = _command.Execute(new SetPackageVersionOptions {
			PackagePath = PackagePath,
			PackageVersion = version
		});

		// Assert
		result.Should().Be(1,
			because: "only the hidden --PackageVersion alias guards against an empty value; -v assigns the main "
				+ "property directly, so a blank string would otherwise reach the descriptor");
		_jsonConverter.DidNotReceive().SerializeObjectToFile(Arg.Any<object>(), Arg.Any<string>());
	}

	[TestCase("abc")]
	[TestCase("1.1.0.x")]
	[Description("Refuses a version that is not parseable as a version, because Creatio compares recorded versions as versions.")]
	public void Execute_ShouldRefuse_WhenTheVersionDoesNotParse(string version) {
		// Act
		int result = _command.Execute(new SetPackageVersionOptions {
			PackagePath = PackagePath,
			PackageVersion = version
		});

		// Assert
		result.Should().Be(1,
			because: "an unparseable value satisfies no dependency and no [RequiresPackage] floor, so writing it "
				+ "produces a package that installs and can never be depended upon — a failure that surfaces far "
				+ "from its cause");
		_jsonConverter.DidNotReceive().SerializeObjectToFile(Arg.Any<object>(), Arg.Any<string>());
		_logger.Received().WriteError(Arg.Is<string>(message => message.Contains(version)));
	}

	[Test]
	[Description("Rejects a null logger, so a misconfigured DI graph fails at construction rather than while writing a descriptor.")]
	public void Constructor_ShouldRejectANullLogger() {
		// Arrange, Act & Assert
		Assert.Throws<System.ArgumentNullException>(
			() => new SetPackageVersionCommand(_jsonConverter, _fileSystem, null),
			"without a logger a refusal would be silent, which is worse than the defect the refusal fixes");
	}

	#endregion

}
