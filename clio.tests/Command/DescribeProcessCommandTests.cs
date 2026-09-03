using System;
using System.Text.Json.Nodes;
using Clio.Command;
using Clio.Command.ProcessModel;
using Clio.Common;
using ErrorOr;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class DescribeProcessCommandTests {
	private IProcessDescriber _describer;
	private ILogger _logger;
	private DescribeProcessCommand _command;

	[SetUp]
	public void Setup() {
		_describer = Substitute.For<IProcessDescriber>();
		_logger = Substitute.For<ILogger>();
		_command = new DescribeProcessCommand(_describer, _logger);
	}

	[TearDown]
	public void TearDown() {
		_describer.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	[Test]
	[Category("Unit")]
	[Description("Describes the process via the server and writes the structured graph JSON, returning zero, when one identity is given.")]
	public void Execute_ShouldWriteStructuredGraphAndReturnZero_WhenProcessFound() {
		// Arrange
		_describer.Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>())
			.Returns(new DescribeProcessResult {
				Name = "UsrProcess_493d4c9",
				Caption = "AI PoC Read Contact",
				SchemaUId = "uid",
				Elements = [],
				Flows = [],
				Parameters = []
			});
		DescribeProcessOptions options = new() { Environment = "dev", ProcessName ="UsrProcess_493d4c9" };

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a found process is described successfully");
		_describer.Received(1).Describe(
			Arg.Is<ProcessIdentity>(identity => identity.Code == "UsrProcess_493d4c9"), Arg.Any<string>());
		_logger.Received(1).WriteInfo(Arg.Is<string>(json => json.Contains("UsrProcess_493d4c9")));
		_logger.DidNotReceive().WriteError(Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Forwards the requested culture to the server describer.")]
	public void Execute_ShouldForwardCulture_WhenProvided() {
		// Arrange
		_describer.Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>())
			.Returns(new DescribeProcessResult { Name = "UsrProcess_493d4c9" });
		DescribeProcessOptions options = new() {
			Environment = "dev", ProcessName ="UsrProcess_493d4c9", Culture = "uk-UA"
		};

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a found process is described successfully");
		_describer.Received(1).Describe(Arg.Any<ProcessIdentity>(), "uk-UA");
	}

	[Test]
	[Category("Unit")]
	[Description("Prints a user-friendly Error and returns non-zero when the process cannot be resolved.")]
	public void Execute_ShouldPrintErrorAndReturnNonZero_WhenProcessNotFound() {
		// Arrange
		_describer.Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>())
			.Returns(Error.Failure("ResolveId", "process not found (code 'missing')"));
		DescribeProcessOptions options = new() { Environment = "dev", ProcessName ="missing" };

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "a missing process is a hard error");
		_logger.Received(1).WriteError(Arg.Is<string>(value =>
			value.StartsWith("Error:", StringComparison.Ordinal) && value.Contains("not found")));
	}

	[Test]
	[Category("Unit")]
	[Description("Requires exactly one identity: with none provided it errors before contacting the server.")]
	public void Execute_ShouldErrorWithoutReading_WhenNoIdentityProvided() {
		// Arrange
		DescribeProcessOptions options = new() { Environment = "dev" };

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "exactly one process identity is required");
		_logger.Received(1).WriteError(Arg.Is<string>(value => value.StartsWith("Error:", StringComparison.Ordinal)));
		_describer.DidNotReceive().Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>());
	}

	[Test]
	[Category("Unit")]
	[Description("Writes each parameter's data value type and a lookup's referenceSchema into the graph JSON (regression: the clio DescribedParameter DTO previously dropped these server fields on re-serialization).")]
	public void Execute_ShouldWriteParameterTypeAndReferenceSchema_WhenPresent() {
		// Arrange
		_describer.Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>())
			.Returns(new DescribeProcessResult {
				Name = "UsrTaskProcess",
				SchemaUId = "uid",
				Elements = [],
				Flows = [],
				Parameters = [
					new DescribedParameter {
						Name = "PCity", UId = "u1", Type = "Lookup", ReferenceSchema = "City", Source = "None"
					}
				]
			});
		DescribeProcessOptions options = new() { Environment = "dev", ProcessName ="UsrTaskProcess" };
		string written = null;
		_logger.WriteInfo(Arg.Do<string>(value => written = value));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a found process is described successfully");
		written.Should().Contain("Lookup",
			because: "the parameter's resolved data value type must survive the clio DTO re-serialization");
		written.Should().Contain("\"referenceSchema\"",
			because: "the lookup parameter's referenceSchema field must not be dropped by the clio DTO");
		written.Should().Contain("City",
			because: "the lookup's referenced object name must be carried through to the command output");
	}

	[Test]
	[Category("Unit")]
	[Description("Writes each element parameter's direction and isResult into the graph JSON (regression: the clio DescribedParameter DTO previously dropped these server fields on re-serialization, so callers could not tell an element's outputs — mappable as a source — from its plain inputs).")]
	public void Execute_ShouldWriteParameterDirectionAndIsResult_WhenPresent() {
		// Arrange — a user-task element exposing an output (IsResult true while Direction is Variable) and a plain input
		_describer.Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>())
			.Returns(new DescribeProcessResult {
				Name = "UsrTaskProcess",
				SchemaUId = "uid",
				Elements = [
					new DescribedElement {
						Name = "Task1", Uid = "e1", Type = "ProcessSchemaUserTask", BuildType = "usertask",
						Parameters = [
							new DescribedParameter {
								Name = "PResult", UId = "p1", Type = "Guid",
								Direction = "Variable", IsResult = true, Source = "None"
							},
							new DescribedParameter {
								Name = "PInput", UId = "p2", Type = "ShortText",
								Direction = "In", IsResult = false, Source = "None"
							}
						]
					}
				],
				Flows = [],
				Parameters = []
			});
		DescribeProcessOptions options = new() { Environment = "dev", ProcessName ="UsrTaskProcess" };
		string written = null;
		_logger.WriteInfo(Arg.Do<string>(value => written = value));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a found process is described successfully");
		written.Should().Contain("\"direction\": \"Variable\"",
			because: "a parameter's direction must survive the clio DTO re-serialization so callers can classify it");
		written.Should().Contain("\"isResult\": true",
			because: "an element output (IsResult true) marks a parameter usable as a mapping source even when its direction is Variable, and must not be dropped by the clio DTO");
	}

	[Test]
	[Category("Unit")]
	[Description("Requires exactly one identity: with more than one provided it errors before contacting the server.")]
	public void Execute_ShouldErrorWithoutReading_WhenMultipleIdentitiesProvided() {
		// Arrange
		DescribeProcessOptions options = new() { Environment = "dev", ProcessName ="x", ProcessCaption = "y" };

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1, because: "providing two identities is ambiguous");
		_describer.DidNotReceive().Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>());
	}
	[Test]
	[Category("Unit")]
	[Description("Writes every field of each version-family entry into the graph JSON, so a caller can pick a version to describe without a second call.")]
	public void Execute_ShouldWriteEveryFamilyEntryField_WhenTheProcessHasVersions() {
		// Arrange
		_describer.Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>())
			.Returns(new DescribeProcessResult {
				Name = "InvoiceVisaProcess",
				SchemaUId = "332eac25-1443-4e4e-a972-6c0e66cb9243",
				Elements = [], Flows = [], Parameters = [],
				Version = 0,
				IsActiveVersion = false,
				ActiveVersionName = "InvoiceVisaProcessInvoice1",
				ActiveVersionSchemaUId = "b5e5162a-254a-430f-8978-4738c6ebf76b",
				VersionRootSchemaUId = "332eac25-1443-4e4e-a972-6c0e66cb9243",
				ActiveVersionSource = "process-library-view",
				Versions = [
					new DescribedProcessVersion {
						SchemaUId = "b5e5162a-254a-430f-8978-4738c6ebf76b",
						Name = "InvoiceVisaProcessInvoice1",
						Caption = "Invoice approval",
						Version = 1,
						IsActiveVersion = true,
						IsRoot = false,
						PackageUId = "864d1545-a641-46c3-b866-e57bd6d39579",
						Enabled = true
					}
				]
			});
		DescribeProcessOptions options = new() { Environment = "dev", ProcessName = "InvoiceVisaProcess" };
		string written = null;
		_logger.WriteInfo(Arg.Do<string>(value => written = value));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a described versioned process is still a successful describe");
		JsonObject entry = JsonNode.Parse(written)!["versions"]!.AsArray()[0]!.AsObject();
		entry.Should().ContainKeys(new[] {
				"schemaUId", "name", "caption", "version", "isActiveVersion", "isRoot", "packageUId", "enabled"
			}, "a family entry has to be complete enough to choose and address a version from it alone");
		entry["isActiveVersion"]!.GetValue<bool>().Should().BeTrue(
			because: "the entry that runs must be identifiable inside the list, not only at the graph root");
		JsonObject root = JsonNode.Parse(written)!.AsObject();
		root.Should().ContainKeys(new[] {
				"version", "isActiveVersion", "activeVersionSchemaUId", "activeVersionName",
				"versionRootSchemaUId", "activeVersionSource", "versions"
			}, "the command serializes by the static type, so a member declared on the private wire subclass "
			 + "instead of the public result would vanish here without an error");
		root["activeVersionName"]!.GetValue<string>().Should().Be("InvoiceVisaProcessInvoice1",
			because: "the root-level pointer to the running version must survive the command's re-serialization");
	}

	[Test]
	[Category("Unit")]
	[Description("Omits every version key and writes only the warning when the version facts could not be established, so absence is never read as version 0.")]
	public void Execute_ShouldOmitVersionKeysAndWriteOnlyTheWarning_WhenFactsWereNotEstablished() {
		// Arrange
		_describer.Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>())
			.Returns(new DescribeProcessResult {
				Name = "UsrProcess_493d4c9",
				SchemaUId = "uid",
				Elements = [], Flows = [], Parameters = [],
				VersionReadWarning = "reading the process library failed: timeout, so the version facts were not established"
			});
		DescribeProcessOptions options = new() { Environment = "dev", ProcessName = "UsrProcess_493d4c9" };
		string written = null;
		_logger.WriteInfo(Arg.Do<string>(value => written = value));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "an unestablished version read does not fail the describe");
		JsonObject output = JsonNode.Parse(written)!.AsObject();
		// Parsed keys, not substrings: DescribeProcessResult carries a [JsonExtensionData] bag, so a server
		// that already returns a "version" key would defeat a substring scan.
		output.Should().NotContainKeys(new[] {
				"version", "isActiveVersion", "activeVersionName", "activeVersionSchemaUId",
				"versionRootSchemaUId", "activeVersionSource", "versions", "versionsTruncatedAt"
			}, "a null version value is omitted rather than published, so absence cannot be read as zero");
		output.Should().ContainKey("versionReadWarning",
			because: "the caller is told the graph's version standing is unknown instead of being left to assume");
	}

	[Test]
	[Category("Unit")]
	[Description("Writes versionsTruncatedAt into the graph JSON when the family was capped, so a caller can tell a partial history from a complete one.")]
	public void Execute_ShouldWriteWhereTheFamilyWasCut_WhenTheFamilyWasTruncated() {
		// Arrange
		_describer.Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>())
			.Returns(new DescribeProcessResult {
				Name = "UsrProcess_0370312",
				SchemaUId = "332eac25-1443-4e4e-a972-6c0e66cb9243",
				Elements = [], Flows = [], Parameters = [],
				Version = 0,
				IsActiveVersion = false,
				Versions = [
					new DescribedProcessVersion { SchemaUId = "u", Name = "UsrProcess_0370312", Version = 0, IsRoot = true }
				],
				VersionsTruncatedAt = 50
			});
		DescribeProcessOptions options = new() { Environment = "dev", ProcessName = "UsrProcess_0370312" };
		string written = null;
		_logger.WriteInfo(Arg.Do<string>(value => written = value));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a capped family is still a successful describe");
		JsonNode.Parse(written)!["versionsTruncatedAt"]!.GetValue<int>().Should().Be(50,
			because: "a partial version history has to announce itself, or it reads as the whole history");
	}

}
