using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command.ProcessModel;
using Clio.Common;
using FluentAssertions;
using Json.Schema;
using NUnit.Framework;

namespace Clio.Tests.Command.ProcessModel;

/// <summary>
/// Pins <c>process-builder-write-keys.schema.json</c> to the CrtProcessBuilder contract sources inside the archive
/// clio bundles (ENG-95244, <c>spec/adr/adr-descriptor-strict-keys.md</c>).
/// </summary>
/// <remarks>
/// Kept in the <c>Unit</c> lane despite reading from disk, for the reason <c>BundledProcessBuilderPackageTests</c>
/// gives: the pre-commit gate is <c>Category=Unit&amp;Module=X</c>, and a guard that only ran in the integration
/// lane would not guard the rebundle that breaks it. The archive is a file this test project's own build output
/// always carries.
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "ProcessModel")]
public sealed class ProcessBuilderWriteKeySchemaTests {

	/// <summary>Set to <c>1</c> to rewrite the checked-in schema from the bundled archive instead of comparing.</summary>
	private const string RegenerateVariable = "CLIO_REGENERATE_PROCESS_BUILDER_KEY_SCHEMA";

	private const string ContractsFolder = "Files/src/cs/Contracts/";

	private const string SchemaRelativePath = "clio/Command/ProcessModel/Schemas/process-builder-write-keys.schema.json";

	[Test]
	[Description("The write-key schema clio ships equals the one regenerated from Files/src/cs/Contracts/*.cs in the bundled CrtProcessBuilder archive. A rebundle that adds, renames or removes a [DataMember] fails here until the schema is regenerated in the same change - otherwise clio would refuse a key the server accepts, or pass one it drops.")]
	public void Schema_ShouldEqualTheOneGeneratedFromTheBundledContracts_WhenTheArchiveIsRebundled() {
		// Arrange
		string expected = ProcessBuilderWriteKeySchemaGenerator.Serialize(
			ProcessBuilderWriteKeySchemaGenerator.Generate(
				ProcessBuilderWriteKeySchemaGenerator.ReadContracts(ReadBundledContractSources())));
		if (Environment.GetEnvironmentVariable(RegenerateVariable) == "1") {
			File.WriteAllText(Path.Combine(FindRepositoryRoot(), SchemaRelativePath), expected, new UTF8Encoding(false));
			Assert.Inconclusive($"Regenerated {SchemaRelativePath}; rebuild and run again without {RegenerateVariable}.");
		}

		// Act
		string shipped = ProcessDescriptorKeyValidator.ReadSchemaText().Replace("\r\n", "\n");

		// Assert
		shipped.Should().Be(expected,
			because: $"the keys clio enforces must be the keys the bundled package declares. Regenerate with "
				+ $"{RegenerateVariable}=1 dotnet test --filter FullyQualifiedName~ProcessBuilderWriteKeySchemaTests, "
				+ "then review the diff: every added or removed key is a change to what clio refuses");
	}

	[Test]
	[Description("The generator reads the contracts the way the server's deserializer binds them: both write roots are present, FilterDescriptor carries its base type's keys (the one inheritance in the contracts), and a List<contract> member is an array of references while a string array is a plain member.")]
	public void Generate_ShouldModelTheContractTree_WhenReadFromTheBundledSources() {
		// Arrange
		IReadOnlyDictionary<string, IReadOnlyList<ProcessBuilderWriteKeySchemaGenerator.ContractMember>> contracts =
			ProcessBuilderWriteKeySchemaGenerator.ReadContracts(ReadBundledContractSources());

		// Act
		JsonObject definitions = ProcessBuilderWriteKeySchemaGenerator.Generate(contracts)["$defs"]!.AsObject();

		// Assert
		definitions.Should().ContainKey(ProcessBuilderWriteKeySchemaGenerator.CreateDescriptorRoot,
			because: "the create descriptor is BuildProcessRequest");
		definitions.Should().ContainKey(ProcessBuilderWriteKeySchemaGenerator.ModifyOperationRoot,
			because: "every modify operation is a ProcessOperationDescriptor");
		definitions["FilterDescriptor"]!["properties"]!.AsObject().Select(pair => pair.Key).Should()
			.Contain(["object", "logicalOperation", "conditions", "groups"],
				because: "FilterDescriptor inherits FilterGroupDescriptor, and a key the base declares is accepted on it");
		definitions["BuildProcessRequest"]!["properties"]!["elements"]!["items"]!["$ref"]!.GetValue<string>()
			.Should().Be("#/$defs/ProcessElementDescriptor", because: "elements is a List<ProcessElementDescriptor>");
		definitions["ProcessFlowDescriptor"]!["properties"]!["results"]!.AsObject().Should().BeEmpty(
			because: "results is a string[] - its entries are values, not keys, and are not descended into");
		definitions.Should().NotContainKey("DescribeProcessResponse",
			because: "only contracts reachable from the two write roots are write keys");
	}

	[Test]
	[Description("The shipped file is a real JSON Schema, not just a format clio happens to read: a standard validator (JsonSchema.Net) accepts a clean descriptor and rejects the measured typo 'lable' on a flow.")]
	public void Schema_ShouldBeAStandardJsonSchema_WhenEvaluatedByAJsonSchemaValidator() {
		// Arrange
		using JsonDocument schemaDocument = JsonDocument.Parse(ProcessDescriptorKeyValidator.ReadSchemaText());
		JsonSchema schema = JsonSchema.Build(schemaDocument.RootElement.Clone());
		using JsonDocument clean = JsonDocument.Parse("""
			{"name":"UsrP","elements":[{"name":"S","type":"startEvent"},{"name":"E","type":"endEvent"}],
			 "flows":[{"source":"S","target":"E","label":"Go"}]}
			""");
		using JsonDocument typo = JsonDocument.Parse("""
			{"name":"UsrP","elements":[{"name":"S","type":"startEvent"},{"name":"E","type":"endEvent"}],
			 "flows":[{"source":"S","target":"E","lable":"Go"}]}
			""");

		// Act
		EvaluationResults cleanResult = schema.Evaluate(clean.RootElement);
		EvaluationResults typoResult = schema.Evaluate(typo.RootElement);

		// Assert
		cleanResult.IsValid.Should().BeTrue(because: "every key in the clean descriptor is a declared member");
		typoResult.IsValid.Should().BeFalse(because: "additionalProperties:false refuses a key the contract lacks");
	}

	private static IEnumerable<string> ReadBundledContractSources() {
		string archivePath = Path.Combine(AppContext.BaseDirectory, BundledPackages.ProcessBuilderPackageName,
			BundledPackages.ProcessBuilderArchiveFileName);
		IFileSystem fileSystem = new FileSystem(new System.IO.Abstractions.FileSystem());
		ICompressionUtilities compression = new CompressionUtilities(fileSystem, new ZipFileWrapper());
		List<string> contractFiles = compression.ListGZipEntryNames(archivePath)
			.Where(entry => entry.StartsWith(ContractsFolder, StringComparison.OrdinalIgnoreCase)
				&& entry.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
			.ToList();
		contractFiles.Should().NotBeEmpty(because: "the archive ships its contract SOURCES - it is source-only");
		foreach (string entry in contractFiles) {
			compression.TryReadFileFromGZip(archivePath, entry, out byte[] content).Should().BeTrue(
				because: $"'{entry}' was just listed from the same archive");
			yield return Encoding.UTF8.GetString(content);
		}
	}

	private static string FindRepositoryRoot() {
		DirectoryInfo directory = new(AppContext.BaseDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "clio.slnx"))) {
			directory = directory.Parent;
		}
		return directory?.FullName
			?? throw new InvalidOperationException("Run the regeneration from inside a clio checkout.");
	}
}
