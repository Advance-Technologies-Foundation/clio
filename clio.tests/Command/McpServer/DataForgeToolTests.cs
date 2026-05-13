using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using Clio.Common.DataForge;
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
	[Description("GetTableColumns delegates to the read client and returns columns unchanged.")]
	public void GetTableColumns_Should_Delegate_To_ReadClient() {
		// Arrange
		IDataForgeReadClient readClient = Substitute.For<IDataForgeReadClient>();
		readClient.GetTableColumnsDetails("Contact").Returns([
			new DataForgeColumnResult("Name", "Full name", null, "Text", true, null),
			new DataForgeColumnResult("Account", "Account", null, "Lookup", false, "Account")
		]);
		DataForgeTool tool = CreateTool(readClient);

		// Act
		DataForgeColumnsResponse result = tool.GetTableColumns(new DataForgeGetTableColumnsArgs(TableName: "Contact"));

		// Assert
		result.Success.Should().BeTrue(
			because: "valid read client results should produce a successful response");
		result.Columns.Should().HaveCount(2,
			because: "all columns from the read client should be returned");
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
			RelationPairs: [new DataForgeRelationPairArgs("Contact", "Account")]));

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
	[Description("Marks read tools as read-only and maintenance tools as mutating in MCP metadata.")]
	[TestCase(nameof(DataForgeTool.GetStatus), true, false)]
	[TestCase(nameof(DataForgeTool.FindTables), true, false)]
	[TestCase(nameof(DataForgeTool.FindLookups), true, false)]
	[TestCase(nameof(DataForgeTool.GetRelations), true, false)]
	[TestCase(nameof(DataForgeTool.GetTableColumns), true, false)]
	[TestCase(nameof(DataForgeTool.GetContext), true, false)]
	[TestCase(nameof(DataForgeTool.Initialize), false, true)]
	[TestCase(nameof(DataForgeTool.Update), false, true)]
	public void DataForgeTools_Should_Advertise_Safety_Metadata(string methodName, bool readOnly, bool destructive) {
		// Arrange
		System.Reflection.MethodInfo method = typeof(DataForgeTool).GetMethod(methodName)!;
		McpServerToolAttribute attribute = method
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Cast<McpServerToolAttribute>()
			.Single();

		// Assert
		attribute.ReadOnly.Should().Be(readOnly,
			because: "MCP metadata must advertise the correct read-only flag for each Data Forge tool");
		attribute.Destructive.Should().Be(destructive,
			because: "MCP metadata must advertise the correct destructive flag for each Data Forge tool");
	}

	private static DataForgeTool CreateTool(
		IDataForgeReadClient? readClient = null,
		IDataForgeMaintenanceClient? maintenanceClient = null,
		IDataForgeContextService? contextService = null,
		IToolCommandResolver? commandResolver = null) {
		return new(
			readClient ?? Substitute.For<IDataForgeReadClient>(),
			maintenanceClient ?? Substitute.For<IDataForgeMaintenanceClient>(),
			contextService ?? Substitute.For<IDataForgeContextService>(),
			commandResolver ?? Substitute.For<IToolCommandResolver>());
	}
}
