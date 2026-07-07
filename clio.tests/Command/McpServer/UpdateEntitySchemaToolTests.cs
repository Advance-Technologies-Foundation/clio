using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Clio;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class UpdateEntitySchemaToolTests {

	[Test]
	[Description("Advertises a stable MCP tool name for remote entity schema batch updates.")]
	[Category("Unit")]
	public void UpdateEntitySchemaTool_Should_Advertise_Stable_Tool_Name() {
		// Arrange

		// Act
		string toolName = UpdateEntitySchemaTool.UpdateEntitySchemaToolName;

		// Assert
		toolName.Should().Be("update-entity-schema",
			because: "tests and MCP callers should use the shared production constant");
	}

	[Test]
	[Description("Returns a DataForge enrichment section when the enrichment service is provided and succeeds.")]
	[Category("Unit")]
	public async Task UpdateEntitySchema_Should_Return_DataForge_Section_When_Enrichment_Succeeds() {
		// Arrange
		FakeUpdateEntitySchemaCommand command = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(command);
		ApplicationDataForgeResult expectedDataForge = new(
			Used: true,
			Health: null,
			Status: null,
			Coverage: null,
			Warnings: [],
			ContextSummary: null);
		ISchemaEnrichmentService enrichmentService = Substitute.For<ISchemaEnrichmentService>();
		enrichmentService.Enrich(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
				Arg.Any<IReadOnlyList<string>>())
			.Returns(expectedDataForge);
		UpdateEntitySchemaTool tool = new(command, ConsoleLogger.Instance, commandResolver, enrichmentService);
		UpdateEntitySchemaArgs args = BuildArgs("UsrVehicle", new[] {
			new UpdateEntitySchemaOperationArgs("add", "UsrColor", Type: "ShortText",
				TitleLocalizations: Title("Color")),
			new UpdateEntitySchemaOperationArgs("add", "UsrOwner", Type: "Lookup",
				ReferenceSchemaName: "Contact", TitleLocalizations: Title("Owner"))
		});

		// Act
		CommandExecutionResult result = await tool.UpdateEntitySchema(args);

		// Assert
		result.ExitCode.Should().Be(0, because: "a valid batch update with enrichment should succeed");
		result.DataForge.Should().Be(expectedDataForge,
			because: "the DataForge result from the enrichment service should be attached to the response");
	}

	[Test]
	[Description("Succeeds without a DataForge section when no enrichment service is provided (test or degraded context).")]
	[Category("Unit")]
	public async Task UpdateEntitySchema_Should_Succeed_Without_DataForge_When_No_Service() {
		// Arrange
		FakeUpdateEntitySchemaCommand command = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(command);
		UpdateEntitySchemaTool tool = new(command, ConsoleLogger.Instance, commandResolver);
		UpdateEntitySchemaArgs args = BuildArgs("UsrOrder", new[] {
			new UpdateEntitySchemaOperationArgs("add", "UsrStatus", Type: "ShortText",
				TitleLocalizations: Title("Status"))
		});

		// Act
		CommandExecutionResult result = await tool.UpdateEntitySchema(args);

		// Assert
		result.ExitCode.Should().Be(0, because: "schema mutation should succeed without an enrichment service");
		result.DataForge.Should().BeNull(
			because: "no enrichment service means no DataForge section should be attached");
	}

	[Test]
	[Description("Mutation still succeeds and returns null DataForge when the enrichment service throws.")]
	[Category("Unit")]
	public async Task UpdateEntitySchema_Should_Succeed_When_Enrichment_Service_Throws() {
		// Arrange
		FakeUpdateEntitySchemaCommand command = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(command);
		ISchemaEnrichmentService enrichmentService = Substitute.For<ISchemaEnrichmentService>();
		enrichmentService.Enrich(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
				Arg.Any<IReadOnlyList<string>>())
			.Throws(new System.Exception("DataForge unavailable"));
		UpdateEntitySchemaTool tool = new(command, ConsoleLogger.Instance, commandResolver, enrichmentService);
		UpdateEntitySchemaArgs args = BuildArgs("UsrVehicle", new[] {
			new UpdateEntitySchemaOperationArgs("add", "UsrPlate", Type: "ShortText",
				TitleLocalizations: Title("Plate"))
		});

		// Act
		CommandExecutionResult result = await tool.UpdateEntitySchema(args);

		// Assert
		result.ExitCode.Should().Be(0,
			because: "enrichment failure must not block the schema mutation");
		result.DataForge.Should().BeNull(
			because: "when enrichment throws, DataForge should be null (best-effort, never blocks mutation)");
	}

	[Test]
	[Description("Passes only add-operation column names and the schema name as candidate terms to the enrichment service.")]
	[Category("Unit")]
	public async Task UpdateEntitySchema_Should_Pass_Add_Operation_ColumnNames_As_CandidateTerms() {
		// Arrange
		FakeUpdateEntitySchemaCommand command = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(command);
		ISchemaEnrichmentService enrichmentService = Substitute.For<ISchemaEnrichmentService>();
		enrichmentService.Enrich(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
				Arg.Any<IReadOnlyList<string>>())
			.Returns(new ApplicationDataForgeResult(Used: true, Health: null, Status: null, Coverage: null, Warnings: [], ContextSummary: null));
		UpdateEntitySchemaTool tool = new(command, ConsoleLogger.Instance, commandResolver, enrichmentService);
		UpdateEntitySchemaArgs args = BuildArgs("UsrFleet", new[] {
			new UpdateEntitySchemaOperationArgs("add", "UsrMake", Type: "ShortText",
				TitleLocalizations: Title("Make")),
			new UpdateEntitySchemaOperationArgs("modify", "UsrModel", NewName: "UsrModelName"),
			new UpdateEntitySchemaOperationArgs("remove", "UsrObsolete")
		});

		// Act
		await tool.UpdateEntitySchema(args);

		// Assert
		enrichmentService.Received(1).Enrich(
			Arg.Any<string>(),
			Arg.Is<IReadOnlyList<string>>(terms =>
				terms.Contains("UsrFleet")
				&& terms.Contains("UsrMake")
				&& !terms.Contains("UsrModel")
				&& !terms.Contains("UsrObsolete")),
			Arg.Any<IReadOnlyList<string>>());
	}

	[Test]
	[Description("Passes only add-operation reference schema names as lookup hints to the enrichment service.")]
	[Category("Unit")]
	public async Task UpdateEntitySchema_Should_Pass_Add_Operation_ReferenceSchemas_As_LookupHints() {
		// Arrange
		FakeUpdateEntitySchemaCommand command = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(command);
		ISchemaEnrichmentService enrichmentService = Substitute.For<ISchemaEnrichmentService>();
		enrichmentService.Enrich(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
				Arg.Any<IReadOnlyList<string>>())
			.Returns(new ApplicationDataForgeResult(Used: true, Health: null, Status: null, Coverage: null, Warnings: [], ContextSummary: null));
		UpdateEntitySchemaTool tool = new(command, ConsoleLogger.Instance, commandResolver, enrichmentService);
		UpdateEntitySchemaArgs args = BuildArgs("UsrFleet", new[] {
			new UpdateEntitySchemaOperationArgs("add", "UsrDriver", Type: "Lookup",
				ReferenceSchemaName: "Contact", TitleLocalizations: Title("Driver")),
			new UpdateEntitySchemaOperationArgs("add", "UsrDepartment", Type: "Lookup",
				ReferenceSchemaName: "Department", TitleLocalizations: Title("Department")),
			new UpdateEntitySchemaOperationArgs("modify", "UsrOwner", ReferenceSchemaName: "Account")
		});

		// Act
		await tool.UpdateEntitySchema(args);

		// Assert
		enrichmentService.Received(1).Enrich(
			Arg.Any<string>(),
			Arg.Any<IReadOnlyList<string>>(),
			Arg.Is<IReadOnlyList<string>>(hints =>
				hints.Contains("Contact")
				&& hints.Contains("Department")
				&& !hints.Contains("Account")));
	}

	[Test]
	[Description("Emits the deterministic 'compile-creatio not required' note on a successful update so agents do not run a needless compile.")]
	[Category("Unit")]
	public async Task UpdateEntitySchema_Should_EmitCompileNotRequiredNote_WhenUpdateSucceeds() {
		// Arrange
		FakeUpdateEntitySchemaCommand command = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(command);
		UpdateEntitySchemaTool tool = new(command, ConsoleLogger.Instance, commandResolver);
		UpdateEntitySchemaArgs args = BuildArgs("UsrOrder", new[] {
			new UpdateEntitySchemaOperationArgs("add", "UsrStatus", Type: "ShortText",
				TitleLocalizations: Title("Status"))
		});

		// Act
		CommandExecutionResult result = await tool.UpdateEntitySchema(args);

		// Assert
		result.ExitCode.Should().Be(0,
			because: "the fake command returns a successful exit code");
		result.Note.Should().Be(CommandExecutionResult.CompileNotRequiredNote,
			because: "update-entity-schema applies DDL and refreshes the runtime schema itself, so the deterministic note must steer agents away from a needless compile-creatio");
	}

	[Test]
	[Description("Suppresses the deterministic 'compile-creatio not required' note when the update fails, so agents are not misled into skipping a compile that a failed mutation may still require.")]
	[Category("Unit")]
	public async Task UpdateEntitySchema_Should_NotEmitCompileNotRequiredNote_WhenUpdateFails() {
		// Arrange
		FakeUpdateEntitySchemaCommand command = new(exitCode: 1);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<UpdateEntitySchemaCommand>(Arg.Any<EnvironmentOptions>())
			.Returns(command);
		UpdateEntitySchemaTool tool = new(command, ConsoleLogger.Instance, commandResolver);
		UpdateEntitySchemaArgs args = BuildArgs("UsrOrder", new[] {
			new UpdateEntitySchemaOperationArgs("add", "UsrStatus", Type: "ShortText",
				TitleLocalizations: Title("Status"))
		});

		// Act
		CommandExecutionResult result = await tool.UpdateEntitySchema(args);

		// Assert
		result.ExitCode.Should().NotBe(0,
			because: "the fake command returns a non-zero exit code");
		result.Note.Should().BeNull(
			because: "the deterministic note must only ride a successful update; emitting it on a failed mutation would steer agents into skipping a compile the failure may still require");
	}

	private static UpdateEntitySchemaArgs BuildArgs(string schemaName, IEnumerable<UpdateEntitySchemaOperationArgs> operations) {
		return new UpdateEntitySchemaArgs(
			EnvironmentName: "test-env",
			PackageName: "MyPackage",
			SchemaName: schemaName,
			Operations: operations);
	}

	private static Dictionary<string, string> Title(string enUs) =>
		new() { ["en-US"] = enUs };

	private sealed class FakeUpdateEntitySchemaCommand : UpdateEntitySchemaCommand {
		private readonly int _exitCode;

		public UpdateEntitySchemaOptions? CapturedOptions { get; private set; }

		public FakeUpdateEntitySchemaCommand(int exitCode = 0)
			: base(
				Substitute.For<Clio.Command.EntitySchemaDesigner.IRemoteEntitySchemaColumnManager>(),
				Substitute.For<ILogger>()) {
			_exitCode = exitCode;
		}

		public override int Execute(UpdateEntitySchemaOptions options) {
			CapturedOptions = options;
			return _exitCode;
		}
	}
}
