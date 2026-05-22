using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using Clio.Common.DataForge;
using Clio.Common.EntitySchema;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class DataForgeToolTests {

	[Test]
	[Category("Unit")]
	[Description("Advertises stable MCP tool names for the DataForge tool family so clients can bind to production identifiers.")]
	public void DataForgeTools_Should_Advertise_Stable_Tool_Names() {
		// Arrange

		// Act
		string[] toolNames = [
			DataForgeTool.DataForgeStatusToolName,
			DataForgeTool.DataForgeFindTablesToolName,
			DataForgeTool.DataForgeFindLookupsToolName,
			DataForgeTool.DataForgeGetRelationsToolName,
			DataForgeTool.DataForgeGetTableColumnsToolName,
			DataForgeTool.DataForgeContextToolName,
			DataForgeTool.DataForgeInitializeToolName,
			DataForgeTool.DataForgeUpdateToolName
		];

		// Assert
		toolNames.Should().Equal([
			"dataforge-status",
			"dataforge-find-tables",
			"dataforge-find-lookups",
			"dataforge-get-relations",
			"dataforge-get-table-columns",
			"dataforge-context",
			"dataforge-initialize",
			"dataforge-update"
		], because: "MCP clients must be able to bind to stable Data Forge tool identifiers");
	}

	[Test]
	[Category("Unit")]
	[Description("GetTableColumns delegates to the runtime schema reader and returns columns mapped to logical names.")]
	public void GetTableColumns_Should_Delegate_To_RuntimeSchemaReader() {
		// Arrange
		IRuntimeEntitySchemaReader runtimeReader = Substitute.For<IRuntimeEntitySchemaReader>();
		runtimeReader.GetByName("Contact").Returns(new RuntimeEntitySchemaResult(
			Guid.NewGuid(), "Contact", Guid.NewGuid(), null, null,
			[
				new RuntimeEntitySchemaColumnResult(Guid.NewGuid(), "Name", "Full name", null, 1, true, false, null),
				new RuntimeEntitySchemaColumnResult(Guid.NewGuid(), "Account", "Account", null, 10, false, false, "Account")
			]));
		DataForgeTool tool = CreateTool(runtimeEntitySchemaReader: runtimeReader);

		// Act
		DataForgeColumnsResponse result = tool.GetTableColumns(new DataForgeGetTableColumnsArgs(TableName: "Contact") {
			EnvironmentName = "sandbox"
		});

		// Assert
		result.Success.Should().BeTrue(
			because: "valid runtime schema results should produce a successful response");
		result.Columns.Should().HaveCount(2,
			because: "all non-inherited columns from the runtime schema should be returned");
		runtimeReader.Received(1).GetByName("Contact");
	}

	[Test]
	[Category("Unit")]
	[Description("GetContext delegates to the context service and returns its result unchanged.")]
	public void GetContext_Should_Delegate_To_Context_Service() {
		// Arrange
		IDataForgeContextService contextService = Substitute.For<IDataForgeContextService>();
		contextService.GetContext(Arg.Any<DataForgeContextRequest>())
			.Returns(new DataForgeContextAggregationResult(
				"corr-1",
				["columns:Account:read failed"],
				new DataForgeHealthResult(true, true, true, true, "corr-1"),
				new DataForgeMaintenanceStatusResult(true, "Ready", null),
				[new SimilarTableResult("Contact", "Contact", "Primary contact")],
				[new SimilarLookupResult("lookup-id", "Industry", "Manufacturing", 0.92m)],
				new Dictionary<string, IReadOnlyList<string>> {
					["Contact->Account"] = ["(Contact)-[:Account]->(Account)"]
				},
				new Dictionary<string, IReadOnlyList<DataForgeColumnResult>> {
					["Contact"] = [new DataForgeColumnResult("Name", "Full name", null, "Text", true, null)]
				},
				new DataForgeCoverage(true, true, true, true, false)));
		DataForgeTool tool = CreateTool(contextService: contextService);

		// Act
		DataForgeContextResponse result = tool.GetContext(new DataForgeContextArgs(
			RequirementSummary: "customer request",
			CandidateTerms: ["customer request"],
			LookupHints: ["industry"],
			RelationPairs: [new DataForgeRelationPairArgs("Contact", "Account")]) {
			EnvironmentName = "sandbox"
		});

		// Assert
		result.Success.Should().BeTrue(
			because: "successful context-service aggregation should surface as a successful response");
		result.Warnings.Should().ContainSingle(
			because: "warnings from the context service should be preserved");
		result.Coverage.Columns.Should().BeFalse(
			because: "coverage from the context service should be preserved");
	}

	[Test]
	[Category("Unit")]
	[Description("Every DataForge tool call should reject missing environment-name before resolving target-scoped services.")]
	public void GetTableColumns_Should_Reject_Missing_Environment_Name() {
		// Arrange
		IDataForgeReadClient readClient = Substitute.For<IDataForgeReadClient>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		DataForgeTool tool = CreateTool(readClient, commandResolver: commandResolver);

		// Act
		DataForgeColumnsResponse result = tool.GetTableColumns(new DataForgeGetTableColumnsArgs(TableName: "Contact"));

		// Assert
		result.Success.Should().BeFalse(because: "environment-name is required by the DataForge tool contract");
		result.Error!.Message.Should().Contain("environment-name is required",
			because: "callers need a clear contract error for missing environment-name");
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<IDataForgeReadClient>(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("DataForge tool descriptions should advertise the Creatio platform version requirement rather than an external CrtDataForge package version.")]
	public void DataForgeTools_Should_Advertise_Creatio_Platform_Version_Requirement() {
		// Arrange
		System.Reflection.MethodInfo method = typeof(DataForgeTool).GetMethod(nameof(DataForgeTool.GetStatus))!;
		System.ComponentModel.DescriptionAttribute attribute = method
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Cast<System.ComponentModel.DescriptionAttribute>()
			.Single();

		// Assert
		attribute.Description.Should().Contain("Creatio platform version 10.0.0 or later",
			because: "CrtDataForge is bundled into supported Creatio platform versions");
	}

	[Test]
	[Category("Unit")]
	[Description("GetTableColumns description should describe the caller-facing result.")]
	public void GetTableColumns_Should_Advertise_Outcome() {
		// Arrange
		System.Reflection.MethodInfo method = typeof(DataForgeTool).GetMethod(nameof(DataForgeTool.GetTableColumns))!;
		System.ComponentModel.DescriptionAttribute attribute = method
			.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
			.Cast<System.ComponentModel.DescriptionAttribute>()
			.Single();

		// Act
		string description = attribute.Description;

		// Assert
		description.Should().Contain("logical columns of a Creatio table",
			because: "tool discovery should describe the user-facing result");
		description.Should().Contain("lookup targets",
			because: "callers need to know the returned columns include reference metadata");
	}

	[Test]
	[Category("Unit")]
	[Description("Serializes the DataForge context response using stable kebab-case payload field names and a coverage object.")]
	public void DataForgeContextResponse_Should_Serialize_Using_Stable_Field_Names() {
		// Arrange
		DataForgeContextResponse response = new(
			Success: true,
			Source: "clio+dataforge-service",
			CorrelationId: "corr-1",
			Warnings: ["partial columns"],
			Error: null,
			Health: new DataForgeHealthResult(true, true, true, true, "corr-health"),
			Status: new DataForgeMaintenanceStatusResult(true, "Ready", null),
			SimilarTables: [new SimilarTableResult("Contact", "Contact", "Primary contact")],
			SimilarLookups: [new SimilarLookupResult("lookup-id", "Industry", "Manufacturing", 0.92m)],
			Relations: new Dictionary<string, IReadOnlyList<string>> {
				["Contact->Account"] = ["(Contact)-[:Account]->(Account)"]
			},
			Columns: new Dictionary<string, IReadOnlyList<DataForgeColumnResult>> {
				["Contact"] = [new DataForgeColumnResult("Name", "Full name", null, "Text", true, null)]
			},
			Coverage: new DataForgeCoverage(true, true, true, true, true));

		// Act
		string json = JsonSerializer.Serialize(response);

		// Assert
		json.Should().Contain("\"correlation-id\":\"corr-1\"",
			because: "the response envelope should preserve the stable kebab-case correlation id field");
		json.Should().Contain("\"similar-tables\"",
			because: "the response envelope should preserve the stable similar-tables field name");
		json.Should().Contain("\"similar-lookups\"",
			because: "the response envelope should preserve the stable similar-lookups field name");
		json.Should().Contain("\"coverage\"",
			because: "the response envelope should preserve the coverage object");
		json.Should().Contain("\"table-columns\":true",
			because: "the coverage object should preserve the stable table-columns field name");
	}

	[Test]
	[Category("Unit")]
	[Description("Marks read tools as read-only and maintenance tools as mutating in MCP metadata. FindTables/FindLookups are folded into the consolidated `Find` entry point.")]
	[TestCase(nameof(DataForgeTool.GetStatus), true, false)]
	[TestCase(nameof(DataForgeTool.Find), true, false)]
	[TestCase(nameof(DataForgeTool.GetRelations), true, false)]
	[TestCase(nameof(DataForgeTool.GetTableColumns), true, false)]
	[TestCase(nameof(DataForgeTool.GetContext), true, false)]
	[TestCase(nameof(DataForgeTool.Initialize), false, true)]
	[TestCase(nameof(DataForgeTool.Update), false, true)]
	public void DataForgeTools_Should_Advertise_Safety_Metadata(string methodName, bool readOnly, bool destructive) {
		System.Reflection.MethodInfo method = typeof(DataForgeTool).GetMethod(methodName)!;
		McpServerToolAttribute attribute = method
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Cast<McpServerToolAttribute>()
			.Single();
		attribute.ReadOnly.Should().Be(readOnly,
			because: "MCP metadata must advertise the correct read-only flag for each Data Forge tool");
		attribute.Destructive.Should().Be(destructive,
			because: "MCP metadata must advertise the correct destructive flag for each Data Forge tool");
	}

	private static DataForgeTool CreateTool(
		IDataForgeReadClient? readClient = null,
		IDataForgeMaintenanceClient? maintenanceClient = null,
		IDataForgeContextService? contextService = null,
		IRuntimeEntitySchemaReader? runtimeEntitySchemaReader = null,
		IToolCommandResolver? commandResolver = null) {
		readClient ??= Substitute.For<IDataForgeReadClient>();
		maintenanceClient ??= Substitute.For<IDataForgeMaintenanceClient>();
		contextService ??= Substitute.For<IDataForgeContextService>();
		runtimeEntitySchemaReader ??= Substitute.For<IRuntimeEntitySchemaReader>();
		commandResolver ??= Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IDataForgeReadClient>(Arg.Any<DataForgeTargetOptions>()).Returns(readClient);
		commandResolver.Resolve<IDataForgeMaintenanceClient>(Arg.Any<DataForgeTargetOptions>()).Returns(maintenanceClient);
		commandResolver.Resolve<IDataForgeContextService>(Arg.Any<DataForgeTargetOptions>()).Returns(contextService);
		commandResolver.Resolve<IRuntimeEntitySchemaReader>(Arg.Any<DataForgeTargetOptions>()).Returns(runtimeEntitySchemaReader);
		return new(
			readClient,
			maintenanceClient,
			contextService,
			runtimeEntitySchemaReader,
			commandResolver);
	}
}
