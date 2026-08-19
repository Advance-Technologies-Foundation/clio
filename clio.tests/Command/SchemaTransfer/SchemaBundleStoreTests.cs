using System;
using System.Collections.Generic;
using System.IO;
using Clio.Command.SchemaTransfer;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.SchemaTransfer;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public class SchemaBundleStoreTests {

	private const string BundleRoot = "/tmp/bundles";
	private const string SchemaName = "UsrProbeSchema";

	/// <summary>
	/// The exact folder the store is asked to write into. The caller (<c>export-schema</c>) composes and confines
	/// this path, so the store receives it already resolved.
	/// </summary>
	private static readonly string BundleDirectory = System.IO.Path.Combine(BundleRoot, SchemaName);
	private static readonly string SchemaDataPath =
		System.IO.Path.Combine(BundleDirectory, SchemaBundleStore.SchemaDataFileName);
	private static readonly string DescriptorPath =
		System.IO.Path.Combine(BundleDirectory, SchemaBundleStore.DescriptorFileName);

	/// <summary>A payload shaped like the platform exporter's, small enough to assert on.</summary>
	private const string PlatformPayload = """
		{
		  "Version": "10.1.473.0",
		  "UId": "8375dacb-4ea5-4103-b07a-d365f8d276f3",
		  "ManagerName": "AddonSchemaManager",
		  "Name": "UsrProbeSchema",
		  "Caption": "Probe",
		  "MetaData": "{\"MetaData\":{\"Schema\":{\"A2\":\"UsrProbeSchema\"}}}",
		  "Properties": [{"Name": "AddonName", "Value": "BusinessRule"}],
		  "LocalizableValues": [
		    {"Culture": "en-US", "Key": "Caption", "Value": "Probe"},
		    {"Culture": "ru-RU", "Key": "Caption", "Value": "Proba"}
		  ]
		}
		""";

	private IFileSystem _fileSystem;
	private ILogger _logger;
	private SchemaBundleStore _sut;

	[SetUp]
	public void SetUp() {
		_fileSystem = Substitute.For<IFileSystem>();
		_logger = Substitute.For<ILogger>();
		_sut = new SchemaBundleStore(_fileSystem, _logger);
	}

	[TearDown]
	public void TearDown() => _fileSystem.ClearReceivedCalls();

	[Test]
	[Description("Write stores the platform payload verbatim, because it is the only input import consumes")]
	public void Write_Should_Store_Payload_Verbatim() {
		// Arrange
		_fileSystem.ExistsDirectory(BundleDirectory).Returns(false);

		// Act
		string result = _sut.Write(BundleDirectory, new SchemaBundle(BuildDescriptor(), PlatformPayload));

		// Assert
		result.Should().Be(BundleDirectory,
			because: "the caller needs the folder path it can hand over or point import at");
		_fileSystem.Received(1).WriteAllTextToFile(SchemaDataPath, PlatformPayload);
	}

	[Test]
	[Description("Write completes and warns when a projection cannot be written, because the payload is already on disk")]
	public void Write_Should_Complete_When_A_Projection_Write_Fails() {
		// Arrange
		_fileSystem.ExistsDirectory(BundleDirectory).Returns(false);
		// Only the projections fail; the authoritative payload write succeeds. Letting the I/O error escape
		// would abort an export that already produced its artifact, and the half-written folder would then
		// trip the "already exists" guard on every retry.
		_fileSystem
			.When(fs => fs.WriteAllTextToFile(
				Arg.Is<string>(path => path != SchemaDataPath && path != DescriptorPath), Arg.Any<string>()))
			.Do(_ => throw new IOException("The disk is full."));

		// Act
		Action act = () => _sut.Write(BundleDirectory, new SchemaBundle(BuildDescriptor(), PlatformPayload));

		// Assert
		act.Should().NotThrow(
			because: "projections are documented as best-effort and the authoritative payload is already written");
		_fileSystem.Received(1).WriteAllTextToFile(SchemaDataPath, PlatformPayload);
		_logger.Received(1).WriteWarning(Arg.Is<string>(message => message.Contains("The disk is full.")));
	}

	[Test]
	[Description("Write refuses an existing bundle folder rather than overwriting a previous handover")]
	public void Write_Should_Refuse_When_Bundle_Directory_Exists() {
		// Arrange
		_fileSystem.ExistsDirectory(BundleDirectory).Returns(true);

		// Act
		Action act = () => _sut.Write(BundleDirectory, new SchemaBundle(BuildDescriptor(), PlatformPayload));

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage($"*{BundleDirectory}*already exists*",
				because: "silently overwriting an exported bundle would destroy an artifact prepared for review");
		_fileSystem.DidNotReceive().WriteAllTextToFile(SchemaDataPath, Arg.Any<string>());
	}

	[Test]
	[Description("Write emits the reviewable projections: metadata, properties and one resource file per culture")]
	public void Write_Should_Emit_Projections_Per_Culture() {
		// Arrange
		_fileSystem.ExistsDirectory(BundleDirectory).Returns(false);
		List<string> writtenPaths = [];
		_fileSystem
			.When(fileSystem => fileSystem.WriteAllTextToFile(Arg.Any<string>(), Arg.Any<string>()))
			.Do(call => writtenPaths.Add(call.ArgAt<string>(0)));

		// Act
		_sut.Write(BundleDirectory, new SchemaBundle(BuildDescriptor(), PlatformPayload));

		// Assert
		writtenPaths.Should().Contain(System.IO.Path.Combine(BundleDirectory, "metadata.json"),
			because: "an escaped MetaData blob is unreadable, and the point of the bundle is reviewability");
		writtenPaths.Should().Contain(System.IO.Path.Combine(BundleDirectory, "properties.json"),
			because: "schema properties are part of what the operator is asked to approve");
		writtenPaths.Should().Contain(
			System.IO.Path.Combine(BundleDirectory, "resources", "resource.en-US.json"),
			because: "localization resources are projected one file per culture");
		writtenPaths.Should().Contain(
			System.IO.Path.Combine(BundleDirectory, "resources", "resource.ru-RU.json"),
			because: "every culture present in the payload gets its own file, not just the first");
	}

	[Test]
	[Description("Write still produces the authoritative payload when the payload is not parseable JSON")]
	public void Write_Should_Keep_Payload_When_Projections_Cannot_Be_Built() {
		// Arrange
		_fileSystem.ExistsDirectory(BundleDirectory).Returns(false);
		const string notJson = "this is not json";

		// Act
		Action act = () => _sut.Write(BundleDirectory, new SchemaBundle(BuildDescriptor(), notJson));

		// Assert
		act.Should().NotThrow(
			because: "projections are a convenience; losing them must not cost the operator the export itself");
		_fileSystem.Received(1).WriteAllTextToFile(SchemaDataPath, notJson);
	}

	[Test]
	[Description("Read accepts a bundle folder and returns the payload with its descriptor")]
	public void Read_Should_Accept_Bundle_Directory() {
		// Arrange
		_fileSystem.ExistsDirectory(BundleDirectory).Returns(true);
		_fileSystem.ExistsFile(SchemaDataPath).Returns(true);
		_fileSystem.ReadAllText(SchemaDataPath).Returns(PlatformPayload);
		_fileSystem.ExistsFile(DescriptorPath).Returns(true);
		_fileSystem.ReadAllText(DescriptorPath).Returns(
			"""{"schemaName":"UsrProbeSchema","schemaUId":"8375dacb-4ea5-4103-b07a-d365f8d276f3"}""");

		// Act
		SchemaBundle result = _sut.Read(BundleDirectory);

		// Assert
		result.SchemaData.Should().Be(PlatformPayload,
			because: "import must send exactly the bytes the platform exporter produced");
		result.Descriptor.SchemaName.Should().Be(SchemaName,
			because: "the descriptor names the schema the import plan is resolved for");
	}

	[Test]
	[Description("Read accepts the schema-data.json path directly, not only the bundle folder")]
	public void Read_Should_Accept_Schema_Data_File() {
		// Arrange
		_fileSystem.ExistsDirectory(SchemaDataPath).Returns(false);
		_fileSystem.ExistsFile(SchemaDataPath).Returns(true);
		_fileSystem.ReadAllText(SchemaDataPath).Returns(PlatformPayload);
		_fileSystem.ExistsFile(DescriptorPath).Returns(false);

		// Act
		SchemaBundle result = _sut.Read(SchemaDataPath);

		// Assert
		result.SchemaData.Should().Be(PlatformPayload,
			because: "pointing at the payload file must work as well as pointing at its folder");
	}

	[Test]
	[Description("Read recovers the schema identity from the payload when the descriptor is missing")]
	public void Read_Should_Recover_Identity_Without_Descriptor() {
		// Arrange
		_fileSystem.ExistsDirectory(BundleDirectory).Returns(true);
		_fileSystem.ExistsFile(SchemaDataPath).Returns(true);
		_fileSystem.ReadAllText(SchemaDataPath).Returns(PlatformPayload);
		_fileSystem.ExistsFile(DescriptorPath).Returns(false);

		// Act
		SchemaBundle result = _sut.Read(BundleDirectory);

		// Assert
		result.Descriptor.SchemaName.Should().Be(SchemaName,
			because: "the payload itself names the schema, so a missing descriptor must not block an import");
		result.Descriptor.ManagerName.Should().Be("AddonSchemaManager",
			because: "the manager narrows the layer lookup the import plan is built from");
	}

	[Test]
	[Description("Read fails with an actionable message when the path holds no payload")]
	public void Read_Should_Fail_When_Payload_Missing() {
		// Arrange
		_fileSystem.ExistsDirectory(BundleDirectory).Returns(true);
		_fileSystem.ExistsFile(SchemaDataPath).Returns(false);

		// Act
		Action act = () => _sut.Read(BundleDirectory);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*schema-data.json*",
				because: "the caller must be told which file is missing, not just that something is wrong");
	}

	private static SchemaBundleDescriptor BuildDescriptor() =>
		new() {
			SchemaName = SchemaName,
			SchemaUId = "8375dacb-4ea5-4103-b07a-d365f8d276f3",
			ManagerName = "AddonSchemaManager",
			SourcePackageName = "UsrProbePackage",
			ExportedOnUtc = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc)
		};
}
