using System;
using Clio.Command;
using Clio.Command.SchemaTransfer;
using Clio.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.SchemaTransfer;

[TestFixture]
[Property("Module", "Command")]
public class ExportSchemaCommandTests : BaseCommandTests<ExportSchemaOptions> {

	private const string SchemaName = "UsrProbeSchema";
	private const string PackageName = "UsrProbePackage";
	private const string Payload = """{"Name":"UsrProbeSchema"}""";

	private ISchemaTransferClient _schemaTransferClient;
	private ISchemaBundleStore _schemaBundleStore;
	private ExportSchemaCommand _sut;

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_schemaTransferClient = Substitute.For<ISchemaTransferClient>();
		_schemaBundleStore = Substitute.For<ISchemaBundleStore>();
		containerBuilder.AddSingleton(_schemaTransferClient);
		containerBuilder.AddSingleton(_schemaBundleStore);
		// The REAL file system on purpose: the command under test only asks OutputPathConfinement to resolve a
		// path (symlink resolution, temp-root comparison, existence probe) and never writes — the bundle store is
		// substituted. An in-memory file system has no OS temp root, so confinement would reject every path and
		// the confinement test would pass for the wrong reason.
		containerBuilder.AddSingleton<System.IO.Abstractions.IFileSystem>(new System.IO.Abstractions.FileSystem());
	}

	[SetUp]
	public void SetUp() {
		_sut = Container.GetRequiredService<ExportSchemaCommand>();
		_schemaTransferClient
			.Export(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.Returns((BuildLayer(), Payload));
	}

	[TearDown]
	public void TearDown() {
		_schemaTransferClient.ClearReceivedCalls();
		_schemaBundleStore.ClearReceivedCalls();
	}

	[Test]
	[Category("Unit")]
	[Description("Fails without calling the environment when the schema name is blank")]
	public void Execute_Should_Fail_On_Blank_Schema_Name() {
		// Arrange
		ExportSchemaOptions options = new() { SchemaName = "  " };

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(1, because: "an empty name cannot resolve a layer, so the call is pointless");
		_schemaTransferClient.DidNotReceive().Export(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a destination outside the allowed locations BEFORE contacting the environment")]
	public void Execute_Should_Reject_Unconfined_Destination_Before_Any_Remote_Call() {
		// Arrange
		ExportSchemaOptions options = new() {
			SchemaName = SchemaName,
			PackageName = PackageName,
			Destination = System.IO.Path.Combine("/", "etc", "clio-escape")
		};

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "the destination can be supplied by an MCP agent, so an out-of-bounds path must be refused");
		_schemaTransferClient.DidNotReceive().Export(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
		_schemaBundleStore.DidNotReceive().Write(Arg.Any<string>(), Arg.Any<SchemaBundle>());
	}

	[Test]
	[Category("Unit")]
	[Description("Surfaces the ambiguity error the environment reports for a name that spans several packages")]
	public void Execute_Should_Fail_When_Environment_Reports_Ambiguity() {
		// Arrange
		_schemaTransferClient
			.Export(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
			.Returns(_ => throw new InvalidOperationException(
				"Schema 'Contact' exists in 5 packages: Completeness, CrtCoreBase."));
		ExportSchemaOptions options = new() {
			SchemaName = "Contact",
			Destination = System.IO.Path.GetTempPath()
		};

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "picking one layer of an ambiguous name would export something the caller did not ask for");
		_schemaBundleStore.DidNotReceive().Write(Arg.Any<string>(), Arg.Any<SchemaBundle>());
	}

	[Test]
	[Category("Unit")]
	[Description("Writes the bundle with a descriptor carrying the identity the environment reported")]
	public void Execute_Should_Write_Bundle_With_Reported_Identity() {
		// Arrange
		ExportSchemaOptions options = new() {
			SchemaName = SchemaName,
			PackageName = PackageName,
			Destination = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"clio-export-{Guid.NewGuid():N}")
		};

		// Act
		int result = _sut.Execute(options);

		// Assert
		result.Should().Be(0, because: "the export resolved a single layer and the destination is in bounds");
		_schemaBundleStore.Received(1).Write(
			Arg.Any<string>(),
			Arg.Is<SchemaBundle>(bundle =>
				bundle.SchemaData == Payload
				&& bundle.Descriptor.SchemaUId == "8375dacb-4ea5-4103-b07a-d365f8d276f3"
				&& bundle.Descriptor.SourcePackageName == PackageName));
	}

	private static SchemaLayerDto BuildLayer() =>
		new() {
			SchemaName = SchemaName,
			SchemaUId = "8375dacb-4ea5-4103-b07a-d365f8d276f3",
			ManagerName = "SourceCodeSchemaManager",
			PackageName = PackageName
		};
}
