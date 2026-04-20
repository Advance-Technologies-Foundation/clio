using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;
using Clio.Common;
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
	[TearDown]
	public void TearDown() {
		Environment.SetEnvironmentVariable("HTTP_PROXY", null);
		Environment.SetEnvironmentVariable("HTTPS_PROXY", null);
		Environment.SetEnvironmentVariable("ALL_PROXY", null);
		Environment.SetEnvironmentVariable("NO_PROXY", null);
	}

	[Test]
	[Category("Unit")]
	[Description("Advertises stable MCP tool names for the DataForge tool family so clients can bind to production identifiers.")]
	public void DataForgeTools_Should_Advertise_Stable_Tool_Names() {
		// Arrange

		// Act
		string[] toolNames = [
			DataForgeTool.DataForgeHealthToolName,
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
			"dataforge-health",
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
	[Description("Uses the shared runtime schema reader for table-column requests, filters inherited columns, and preserves the existing friendly data-type mapping.")]
	public void GetTableColumns_Should_Map_Shared_Runtime_Schema_For_DataForge() {
		// Arrange
		IDataForgeClient dataForgeClient = Substitute.For<IDataForgeClient>();
		IDataForgeMaintenanceClient maintenanceClient = Substitute.For<IDataForgeMaintenanceClient>();
		IRuntimeEntitySchemaReader runtimeEntitySchemaReader = Substitute.For<IRuntimeEntitySchemaReader>();
		IDataForgeContextService contextService = Substitute.For<IDataForgeContextService>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		runtimeEntitySchemaReader.GetByName("Contact").Returns(
			new RuntimeEntitySchemaResult(
				Guid.NewGuid(),
				"Contact",
				Guid.NewGuid(),
				"Name",
				null,
				[
					new RuntimeEntitySchemaColumnResult(Guid.NewGuid(), "Name", "Full name", "Primary display name", 1, true, false, null),
					new RuntimeEntitySchemaColumnResult(Guid.NewGuid(), "Account", "Account", "Related account", 10, false, false, "Account"),
					new RuntimeEntitySchemaColumnResult(Guid.NewGuid(), "CreatedOn", "Created on", null, 7, false, true, null)
				]));
		DataForgeTool tool = CreateTool(dataForgeClient, maintenanceClient, runtimeEntitySchemaReader, contextService,
			commandResolver);

		// Act
		DataForgeColumnsResponse result = tool.GetTableColumns(new DataForgeGetTableColumnsArgs(TableName: "Contact"));

		// Assert
		result.Success.Should().BeTrue(
			because: "valid runtime schema reads should be projected into a successful Data Forge columns response");
		result.Columns.Should().HaveCount(2,
			because: "the Data Forge projection should filter inherited columns after reading the shared runtime schema");
		result.Columns.Should().Contain(column =>
				column.Name == "Account" && column.DataType == "Lookup" && column.ReferenceSchemaName == "Account",
			because: "lookup runtime columns should preserve their friendly data type and reference schema");
		result.Columns.Should().Contain(column => column.Name == "Name" && column.DataType == "Text" && column.Required,
			because: "non-inherited columns should preserve their required flag and friendly data type");
	}

	[Test]
	[Category("Unit")]
	[Description("Delegates aggregated context reads to the dedicated Data Forge context service and returns its coverage and warnings unchanged.")]
	public async Task GetContext_Should_Delegate_To_Context_Service() {
		// Arrange
		IDataForgeClient dataForgeClient = Substitute.For<IDataForgeClient>();
		IDataForgeMaintenanceClient maintenanceClient = Substitute.For<IDataForgeMaintenanceClient>();
		IRuntimeEntitySchemaReader runtimeEntitySchemaReader = Substitute.For<IRuntimeEntitySchemaReader>();
		IDataForgeContextService contextService = Substitute.For<IDataForgeContextService>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		contextService.GetContextAsync(Arg.Any<DataForgeContextRequest>(), Arg.Any<DataForgeConfigRequest>(), default)
			.Returns(new DataForgeContextAggregationResult(
				"corr-health",
				["columns:Account:runtime schema failed"],
				new DataForgeHealthResult(true, true, true, true, "corr-health"),
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
		DataForgeTool tool = CreateTool(dataForgeClient, maintenanceClient, runtimeEntitySchemaReader, contextService,
			commandResolver);

		// Act
		DataForgeContextResponse result = await tool.GetContext(new DataForgeContextArgs(
			RequirementSummary: "customer request",
			CandidateTerms: ["customer request"],
			LookupHints: ["industry"],
			RelationPairs: [new DataForgeRelationPairArgs("Contact", "Account")]));

		// Assert
		result.Success.Should().BeTrue(
			because: "successful context-service aggregation should surface as a successful Data Forge context response");
		result.Warnings.Should().ContainSingle(
			because: "tool-level response mapping should preserve warnings returned by the context service");
		result.Coverage.Columns.Should().BeFalse(
			because: "tool-level response mapping should preserve coverage returned by the context service");
		result.Relations.Should().ContainKey("Contact->Account",
			because: "tool-level response mapping should preserve relation keys returned by the context service");
	}

	[Test]
	[Category("Unit")]
	[Description("Health requests should enable syssettings auth fallback and the DataForge OAuth scope by default so MCP calls work on environments that keep OAuth credentials only in Creatio syssettings.")]
	public async Task DataForgeHealth_Should_Request_Default_DataForge_Auth_Settings() {
		// Arrange
		DataForgeConfigRequest? capturedRequest = null;
		IDataForgeClient dataForgeClient = Substitute.For<IDataForgeClient>();
		dataForgeClient.CheckHealthAsync(Arg.Any<DataForgeConfigRequest>(), default)
			.Returns(callInfo => {
				capturedRequest = callInfo.Arg<DataForgeConfigRequest>();
				return new DataForgeHealthResult(true, true, true, true, "corr-health");
			});
		IDataForgeMaintenanceClient maintenanceClient = Substitute.For<IDataForgeMaintenanceClient>();
		IRuntimeEntitySchemaReader runtimeEntitySchemaReader = Substitute.For<IRuntimeEntitySchemaReader>();
		IDataForgeContextService contextService = Substitute.For<IDataForgeContextService>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		DataForgeTool tool = CreateTool(dataForgeClient, maintenanceClient, runtimeEntitySchemaReader, contextService,
			commandResolver);

		// Act
		DataForgeHealthResponse response = await tool.GetHealth(new DataForgeHealthArgs());

		// Assert
		response.Success.Should().BeTrue(because: "health requests should succeed when the underlying client succeeds");
		capturedRequest.Should().NotBeNull(
			because: "the tool should forward a Data Forge config request to the client");
		capturedRequest!.AllowSysSettingsAuthFallback.Should().BeTrue(
			because: "MCP Data Forge calls default to syssettings auth fallback for environments that only store OAuth credentials in Creatio");
		capturedRequest.Scope.Should().Be("use_enrichment",
			because: "MCP Data Forge calls should default to the service scope expected by the current environments");
	}

	[Test]
	[Category("Unit")]
	[Description("Explicit-target Data Forge MCP calls should clear poisoned proxy env vars and still reach the service URL through the direct SysSettings fallback when cliogate is unavailable.")]
	public async Task DataForgeHealth_Should_Clear_Poisoned_Proxy_And_Fall_Back_To_Direct_Service_Url_Read() {
		// Arrange
		Environment.SetEnvironmentVariable("HTTP_PROXY", "http://127.0.0.1:9");
		Environment.SetEnvironmentVariable("HTTPS_PROXY", "http://127.0.0.1:9");
		Environment.SetEnvironmentVariable("ALL_PROXY", "http://127.0.0.1:9");

		ISysSettingsManager sysSettingsManager = Substitute.For<ISysSettingsManager>();
		sysSettingsManager.GetSysSettingValueByCode("DataForgeServiceUrl")
			.Returns(_ => throw new InvalidOperationException("cliogate endpoint is unavailable"));
		ConfigureDefaultNumericSysSettings(sysSettingsManager);
		IDataForgeSysSettingDirectReader directReader = Substitute.For<IDataForgeSysSettingDirectReader>();
		directReader.ReadTextValue("DataForgeServiceUrl")
			.Returns(new DataForgeSysSettingReadResult(true, "https://data-forge-stage.bpmonline.com/", null));
		ObservingHttpMessageHandler handler = new();
		IDataForgeClient resolvedClient = new DataForgeClient(
			new HttpClient(handler),
			new DataForgeConfigResolver(new EnvironmentSettings(), sysSettingsManager, directReader),
			Substitute.For<ILogger>());
		IDataForgeClient fallbackClient = Substitute.For<IDataForgeClient>();
		IDataForgeMaintenanceClient maintenanceClient = Substitute.For<IDataForgeMaintenanceClient>();
		IRuntimeEntitySchemaReader runtimeEntitySchemaReader = Substitute.For<IRuntimeEntitySchemaReader>();
		IDataForgeContextService contextService = Substitute.For<IDataForgeContextService>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IDataForgeClient>(Arg.Any<EnvironmentOptions>()).Returns(resolvedClient);
		DataForgeTool tool = CreateTool(fallbackClient, maintenanceClient, runtimeEntitySchemaReader, contextService,
			commandResolver);

		// Act
		DataForgeHealthResponse response = await tool.GetHealth(new DataForgeHealthArgs {
			Uri = "http://ts1-core-dev04:88/saeenu_14895503_0420",
			Login = "Supervisor",
			Password = "Supervisor"
		});

		// Assert
		response.Success.Should().BeTrue(
			because: "the health tool should recover through direct syssetting reads when cliogate cannot supply DataForgeServiceUrl");
		handler.ProxyValuesDuringSend.Should().OnlyContain(value => value == null,
			because: "poisoned proxy env vars should be cleared for the whole Data Forge call scope");
		handler.RequestUris.Should().Contain(uri => uri.ToString() == "https://data-forge-stage.bpmonline.com/liveness",
			because: "the resolved service URL should come from the direct SysSettings fallback");
		directReader.Received(1).ReadTextValue("DataForgeServiceUrl");
		Environment.GetEnvironmentVariable("HTTP_PROXY").Should().Be("http://127.0.0.1:9",
			because: "proxy env vars should be restored after the Data Forge call finishes");
	}

	[Test]
	[Category("Unit")]
	[Description("Health tool should return a meaningful config-resolution error instead of masking the real failure as a generic DataForgeServiceUrl is required message.")]
	public async Task DataForgeHealth_Should_Return_Meaningful_Error_On_Config_Failure() {
		// Arrange
		ISysSettingsManager sysSettingsManager = Substitute.For<ISysSettingsManager>();
		sysSettingsManager.GetSysSettingValueByCode("DataForgeServiceUrl").Returns("<html>cliogate outdated</html>");
		ConfigureDefaultNumericSysSettings(sysSettingsManager);
		IDataForgeSysSettingDirectReader directReader = Substitute.For<IDataForgeSysSettingDirectReader>();
		directReader.ReadTextValue("DataForgeServiceUrl")
			.Returns(new DataForgeSysSettingReadResult(false, null, "direct SysSettings OData read failed with 401 Unauthorized"));
		IDataForgeClient resolvedClient = new DataForgeClient(
			new HttpClient(new ObservingHttpMessageHandler()),
			new DataForgeConfigResolver(new EnvironmentSettings(), sysSettingsManager, directReader),
			Substitute.For<ILogger>());
		IDataForgeClient fallbackClient = Substitute.For<IDataForgeClient>();
		IDataForgeMaintenanceClient maintenanceClient = Substitute.For<IDataForgeMaintenanceClient>();
		IRuntimeEntitySchemaReader runtimeEntitySchemaReader = Substitute.For<IRuntimeEntitySchemaReader>();
		IDataForgeContextService contextService = Substitute.For<IDataForgeContextService>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IDataForgeClient>(Arg.Any<EnvironmentOptions>()).Returns(resolvedClient);
		DataForgeTool tool = CreateTool(fallbackClient, maintenanceClient, runtimeEntitySchemaReader, contextService,
			commandResolver);

		// Act
		DataForgeHealthResponse response = await tool.GetHealth(new DataForgeHealthArgs {
			Uri = "http://ts1-core-dev04:88/saeenu_14895503_0420",
			Login = "Supervisor",
			Password = "Supervisor"
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "config-resolution failures should be surfaced as a structured tool error");
		response.Error.Should().NotBeNull(
			because: "the tool should include the root-cause error details in its structured payload");
		response.Error!.Message.Should().Contain("401 Unauthorized",
			because: "the structured error should preserve the real direct-read failure");
		response.Error.Message.Should().NotContain("DataForgeServiceUrl is required",
			because: "the root cause should not be masked by the previous generic missing-setting message");
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
	[TestCase(nameof(DataForgeTool.GetHealth), true, false)]
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
		IDataForgeClient dataForgeClient,
		IDataForgeMaintenanceClient maintenanceClient,
		IRuntimeEntitySchemaReader runtimeEntitySchemaReader,
		IDataForgeContextService contextService,
		IToolCommandResolver commandResolver) {
		return new(
			dataForgeClient,
			maintenanceClient,
			runtimeEntitySchemaReader,
			contextService,
			commandResolver,
			new DataForgeProxySafeExecutor());
	}

	private static void ConfigureDefaultNumericSysSettings(ISysSettingsManager sysSettingsManager) {
		sysSettingsManager.GetSysSettingValueByCode<int>("DataForgeServiceQueryTimeout").Returns(30_000);
		sysSettingsManager.GetSysSettingValueByCode<int>("DataForgeSimilarTablesResultLimit").Returns(50);
		sysSettingsManager.GetSysSettingValueByCode<int>("DataForgeLookupResultLimit").Returns(5);
		sysSettingsManager.GetSysSettingValueByCode<int>("DataForgeTableRelationshipsCountLimit").Returns(5);
	}

	private sealed class ObservingHttpMessageHandler : HttpMessageHandler {
		public List<string?> ProxyValuesDuringSend { get; } = [];
		public List<Uri> RequestUris { get; } = [];

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
			ProxyValuesDuringSend.Add(Environment.GetEnvironmentVariable("HTTP_PROXY"));
			RequestUris.Add(request.RequestUri!);
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
		}
	}
}
