using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the entity business-rule MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("create-entity-business-rule")]
[NonParallelizable]
public sealed class EntityBusinessRuleToolE2ETests {
	private const string ToolName = CreateEntityBusinessRuleTool.BusinessRuleCreateToolName;
	private const string TargetEntitySchemaName = "Contact";

	[Test]
	[Description("Creates a unary entity-level business rule through the real MCP server and verifies the rule persisted in the BusinessRule add-on payload without rightExpression metadata.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool persists unary rule metadata in add-on designer storage")]
	[AllureDescription("Arranges a unique sandbox package through the real CLI, calls create-entity-business-rule with an is-filled-in comparison through the real MCP server, then fetches the Contact BusinessRule add-on directly from Creatio and verifies the created rule, comparison type, and omitted rightExpression payload.")]
	public async Task BusinessRuleCreate_Should_Persist_Unary_Rule_Into_Addon_Metadata() {
		// Arrange
		await using SandboxPackageArrangeContext arrangeContext = await ArrangeSandboxPackageAsync();
		string caption = $"Codex unary {Guid.NewGuid():N}".Substring(0, 18);

		// Act
		CallToolResult callResult = await CallBusinessRuleCreateAsync(
			arrangeContext.Session,
			arrangeContext.EnvironmentName,
			arrangeContext.PackageName,
			TargetEntitySchemaName,
			CreateUnaryRule(caption),
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		AddonBusinessRuleSnapshot snapshot = FetchAddonSnapshotByCaption(
			arrangeContext.EnvironmentName,
			arrangeContext.PackageName,
			TargetEntitySchemaName,
			caption);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "successful business-rule creation should return a structured MCP envelope rather than a top-level tool error");
		execution.ExitCode.Should().Be(0,
			because: "the real MCP tool should report success exit code for a valid sandbox package and entity");
		execution.Output.Should().Contain(message => message.Value == $"Rule name: {snapshot.RuleName}",
			because: "the MCP execution log should include the generated internal business-rule name");
		snapshot.RuleCaption.Should().Be(caption,
			because: "the persisted add-on metadata should contain the created rule");
		snapshot.RuleName.Should().StartWith("BusinessRule_",
			because: "the tool should generate an internal business-rule name automatically");
		snapshot.TriggerNames.Should().Contain("Name",
			because: "the persisted unary rule should include the left-attribute trigger");
		snapshot.ComparisonType.Should().Be(1,
			because: "is-filled-in should persist the Creatio not-null comparison type");
		snapshot.HasRightExpression.Should().BeFalse(
			because: "unary comparisons should omit rightExpression from persisted metadata");
		snapshot.ResourceCaption.Should().Be(caption,
			because: "the persisted add-on resources should register the generated caption resource");
	}

	[Test]
	[Description("Creates a relational entity-level business rule through the real MCP server and verifies the persisted comparison type and temporal rightExpression metadata.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool persists relational rule metadata in add-on designer storage")]
	[AllureDescription("Arranges a unique sandbox package through the real CLI, calls create-entity-business-rule with a less-than-or-equal DateTime comparison through the real MCP server, then fetches the Contact BusinessRule add-on directly from Creatio and verifies the created rule, comparison type, and temporal rightExpression payload.")]
	public async Task BusinessRuleCreate_Should_Persist_Relational_Rule_Into_Addon_Metadata() {
		// Arrange
		await using SandboxPackageArrangeContext arrangeContext = await ArrangeSandboxPackageAsync();
		string caption = $"Codex rel {Guid.NewGuid():N}".Substring(0, 18);

		// Act
		CallToolResult callResult = await CallBusinessRuleCreateAsync(
			arrangeContext.Session,
			arrangeContext.EnvironmentName,
			arrangeContext.PackageName,
			TargetEntitySchemaName,
			CreateTemporalRelationalRule(caption),
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		AddonBusinessRuleSnapshot snapshot = FetchAddonSnapshotByCaption(
			arrangeContext.EnvironmentName,
			arrangeContext.PackageName,
			TargetEntitySchemaName,
			caption);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "successful relational business-rule creation should return a structured MCP envelope rather than a top-level tool error");
		execution.ExitCode.Should().Be(0,
			because: "the real MCP tool should report success exit code for a valid relational business-rule payload");
		execution.Output.Should().Contain(message => message.Value == $"Rule name: {snapshot.RuleName}",
			because: "the MCP execution log should include the generated internal business-rule name");
		snapshot.RuleCaption.Should().Be(caption,
			because: "the persisted add-on metadata should contain the created relational rule");
		snapshot.TriggerNames.Should().Contain("CreatedOn",
			because: "the persisted relational rule should include the left-attribute trigger");
		snapshot.ComparisonType.Should().Be(6,
			because: "less-than-or-equal should persist the Creatio relational comparison type");
		snapshot.HasRightExpression.Should().BeTrue(
			because: "binary relational comparisons should persist rightExpression metadata");
		snapshot.RightExpressionType.Should().Be("Const",
			because: "the relational test uses a constant DateTime operand");
		snapshot.RightExpressionDataValueTypeName.Should().Be("DateTime",
			because: "the temporal constant should inherit the DateTime runtime type");
		snapshot.RightExpressionValue.Should().StartWith("2025-01-01T00:00:00",
			because: "the persisted DateTime value should preserve the cutoff timestamp");
		snapshot.RightExpressionValue.Should().EndWith("Z",
			because: "the persisted DateTime value should be normalized to UTC");
		snapshot.ResourceCaption.Should().Be(caption,
			because: "the persisted add-on resources should register the generated caption resource");
	}

	[Test]
	[Description("Returns a readable structured failure when the target environment name is invalid.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool reports invalid environment failures readably")]
	[AllureDescription("Starts the real MCP server and calls create-entity-business-rule with a missing environment name, then verifies the tool returns a readable structured failure and does not hide the problem behind an opaque transport error.")]
	public async Task BusinessRuleCreate_Should_Report_Invalid_Environment_Readably() {
		// Arrange
		await using InvalidEnvironmentArrangeContext arrangeContext = await ArrangeInvalidEnvironmentAsync();

		// Act
		CallToolResult callResult = await CallBusinessRuleCreateAsync(
			arrangeContext.Session,
			arrangeContext.EnvironmentName,
			"UsrPkg",
			TargetEntitySchemaName,
			CreateEqualsRule("Broken rule"),
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "invalid environment handling should be surfaced as a structured tool failure");
		execution.ExitCode.Should().NotBe(0,
			because: "the tool should fail when the requested environment is missing");
		string combinedOutput = string.Join(
			Environment.NewLine,
			(execution.Output ?? []).Select(message => $"{message.MessageType}: {message.Value}"));
		combinedOutput.Should().NotBeNullOrWhiteSpace(
			because: "the failure should include a readable diagnostic");
		combinedOutput.Should().Contain("Environment with key",
			because: "the environment-aware resolver should surface the missing registered environment explicitly");
		combinedOutput.Should().Contain(arrangeContext.EnvironmentName,
			because: "the error should identify the missing environment");
	}

	[AllureStep("Arrange sandbox package for entity business-rule MCP tests")]
	private static async Task<SandboxPackageArrangeContext> ArrangeSandboxPackageAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run destructive entity business-rule MCP end-to-end tests.");
		}

		TestConfiguration.EnsureSandboxIsConfigured(settings);
		string rootDirectory = Path.Combine(Path.GetTempPath(), $"clio-entity-business-rule-mcp-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(rootDirectory);
		string workspaceName = $"workspace-{Guid.NewGuid():N}";
		string workspacePath = Path.Combine(rootDirectory, workspaceName);
		string packageName = $"Pkg{Guid.NewGuid():N}".Substring(0, 18);
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(8));

		try {
			await ClioCliCommandRunner.EnsureCliogateInstalledAsync(
				settings,
				settings.Sandbox.EnvironmentName!,
				cancellationTokenSource.Token);
		} catch (Exception ex) {
			Assert.Ignore(
				$"Skipping destructive entity business-rule MCP end-to-end test because cliogate could not be installed or verified for '{settings.Sandbox.EnvironmentName}'. {ex.Message}");
		}

		await CreateEmptyWorkspaceAsync(settings, rootDirectory, workspaceName, cancellationTokenSource.Token);
		await AddPackageAsync(settings, workspacePath, packageName, cancellationTokenSource.Token);
		await PushWorkspaceAsync(settings, workspacePath, settings.Sandbox.EnvironmentName!, cancellationTokenSource.Token);
		await EnablePackageHotfixAsync(settings, workspacePath, packageName, settings.Sandbox.EnvironmentName!, cancellationTokenSource.Token);

		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new SandboxPackageArrangeContext(rootDirectory, settings.Sandbox.EnvironmentName!, packageName, session, cancellationTokenSource);
	}

	[AllureStep("Arrange invalid-environment MCP session for entity business-rule tool")]
	private static async Task<InvalidEnvironmentArrangeContext> ArrangeInvalidEnvironmentAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new InvalidEnvironmentArrangeContext($"missing-env-{Guid.NewGuid():N}", session, cancellationTokenSource);
	}

	private static async Task<CallToolResult> CallBusinessRuleCreateAsync(
		McpServerSession session,
		string environmentName,
		string packageName,
		string entitySchemaName,
		IReadOnlyDictionary<string, object?> rule,
		CancellationToken cancellationToken) {
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationToken);
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the create-entity-business-rule MCP tool must be advertised before the end-to-end call can be executed");

		return await session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["environmentName"] = environmentName,
				["packageName"] = packageName,
				["entitySchemaName"] = entitySchemaName,
				["rule"] = rule
			},
			cancellationToken);
	}

	private static IReadOnlyDictionary<string, object?> CreateEqualsRule(string caption) {
		return CreateRule(
			caption,
			"Name",
			"equal",
			CreateConstExpression(caption),
			"make-required",
			"JobTitle");
	}

	private static IReadOnlyDictionary<string, object?> CreateUnaryRule(string caption) {
		return CreateRule(
			caption,
			"Name",
			"is-filled-in",
			null,
			"make-read-only",
			"JobTitle");
	}

	private static IReadOnlyDictionary<string, object?> CreateTemporalRelationalRule(string caption) {
		return CreateRule(
			caption,
			"CreatedOn",
			"less-than-or-equal",
			CreateConstExpression("2025-01-01T00:00:00Z"),
			"make-required",
			"JobTitle");
	}

	private static IReadOnlyDictionary<string, object?> CreateRule(
		string caption,
		string leftPath,
		string comparisonType,
		IReadOnlyDictionary<string, object?>? rightExpression,
		string actionType,
		params string[] actionItems) {
		return new Dictionary<string, object?> {
			["caption"] = caption,
			["condition"] = CreateCondition(leftPath, comparisonType, rightExpression),
			["actions"] = new[] { CreateAction(actionType, actionItems) }
		};
	}

	private static IReadOnlyDictionary<string, object?> CreateCondition(
		string leftPath,
		string comparisonType,
		IReadOnlyDictionary<string, object?>? rightExpression) {
		Dictionary<string, object?> condition = new() {
			["leftExpression"] = CreateAttributeExpression(leftPath),
			["comparisonType"] = comparisonType
		};
		if (rightExpression is not null) {
			condition["rightExpression"] = rightExpression;
		}

		return new Dictionary<string, object?> {
			["logicalOperation"] = "AND",
			["conditions"] = new[] { condition }
		};
	}

	private static IReadOnlyDictionary<string, object?> CreateAttributeExpression(string path) =>
		new Dictionary<string, object?> {
			["type"] = "AttributeValue",
			["path"] = path
		};

	private static IReadOnlyDictionary<string, object?> CreateConstExpression(object value) =>
		new Dictionary<string, object?> {
			["type"] = "Const",
			["value"] = value
		};

	private static IReadOnlyDictionary<string, object?> CreateAction(string actionType, params string[] items) =>
		new Dictionary<string, object?> {
			["type"] = actionType,
			["items"] = items
		};

	private static AddonBusinessRuleSnapshot FetchAddonSnapshotByCaption(
		string environmentName,
		string packageName,
		string entitySchemaName,
		string caption) {
		EnvironmentSettings environment = RegisteredClioEnvironmentSettingsResolver.Resolve(environmentName);
		IApplicationClient client = new CreatioClientAdapter(
			environment.Uri!,
			environment.Login!,
			environment.Password!,
			environment.IsNetCore);
		ServiceUrlBuilder serviceUrlBuilder = new(environment);
		Guid packageUId = ResolvePackageUId(client, serviceUrlBuilder, packageName);
		(Guid entitySchemaUId, Guid parentSchemaUId) = ResolveEntitySchema(client, serviceUrlBuilder, entitySchemaName, packageUId);
		string responseBody = client.ExecutePostRequest(
			serviceUrlBuilder.Build("ServiceModel/AddonSchemaDesignerService.svc/GetSchema"),
			JsonSerializer.Serialize(new {
				addonName = "BusinessRule",
				targetSchemaUId = entitySchemaUId,
				targetParentSchemaUId = parentSchemaUId,
				targetPackageUId = packageUId,
				targetSchemaManagerName = "EntitySchemaManager",
				useFullHierarchy = true
			}));
		using JsonDocument response = JsonDocument.Parse(responseBody);
		string metaData = response.RootElement.GetProperty("schema").GetProperty("metaData").GetString()!;
		using JsonDocument metaDataDocument = JsonDocument.Parse(metaData);
		JsonElement createdRule = metaDataDocument.RootElement.GetProperty("rules")
			.EnumerateArray()
			.Single(rule => string.Equals(rule.GetProperty("caption").GetString(), caption, StringComparison.Ordinal));
		string ruleId = createdRule.GetProperty("uId").GetString()!;
		JsonElement resource = response.RootElement.GetProperty("schema").GetProperty("resources")
			.EnumerateArray()
			.Single(item => string.Equals(item.GetProperty("key").GetString(), $"AddonConfig.Rules.{ruleId}.Caption", StringComparison.Ordinal));
		JsonElement createdCondition = createdRule.GetProperty("cases")[0]
			.GetProperty("condition")
			.GetProperty("conditions")[0];
		bool hasRightExpression = createdCondition.TryGetProperty("rightExpression", out JsonElement rightExpression);
		string? rightExpressionType = hasRightExpression ? rightExpression.GetProperty("type").GetString() : null;
		string? rightExpressionDataValueTypeName =
			hasRightExpression && rightExpression.TryGetProperty("dataValueTypeName", out JsonElement dataValueTypeName)
				? dataValueTypeName.GetString()
				: null;
		string? rightExpressionValue =
			hasRightExpression && rightExpression.TryGetProperty("value", out JsonElement valueElement)
				? valueElement.ValueKind == JsonValueKind.String
					? valueElement.GetString()
					: valueElement.GetRawText()
				: null;
		return new AddonBusinessRuleSnapshot(
			createdRule.GetProperty("name").GetString()!,
			createdRule.GetProperty("caption").GetString()!,
			createdRule.GetProperty("triggers").EnumerateArray()
				.Select(trigger => trigger.GetProperty("name").GetString()!)
				.ToArray(),
			resource.GetProperty("value")[0].GetProperty("value").GetString()!,
			createdCondition.GetProperty("comparisonType").GetInt32(),
			hasRightExpression,
			rightExpressionType,
			rightExpressionDataValueTypeName,
			rightExpressionValue);
	}

	private static Guid ResolvePackageUId(IApplicationClient client, ServiceUrlBuilder serviceUrlBuilder, string packageName) {
		string responseBody = client.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select),
			JsonSerializer.Serialize(BuildTextEqualsSelectQuery("SysPackage", "Name", packageName, "UId")));
		using JsonDocument response = JsonDocument.Parse(responseBody);
		return response.RootElement.GetProperty("rows")[0].GetProperty("UId").GetGuid();
	}

	private static (Guid SchemaUId, Guid ParentSchemaUId) ResolveEntitySchema(
		IApplicationClient client,
		ServiceUrlBuilder serviceUrlBuilder,
		string entitySchemaName,
		Guid packageUId) {
		string responseBody = client.ExecutePostRequest(
			serviceUrlBuilder.Build("ServiceModel/EntitySchemaDesignerService.svc/GetSchemaDesignItem"),
			JsonSerializer.Serialize(new {
				name = entitySchemaName,
				packageUId,
				useFullHierarchy = true,
				cultures = new[] { "en-US" }
			}));
		using JsonDocument response = JsonDocument.Parse(responseBody);
		JsonElement schema = response.RootElement.GetProperty("schema");
		return (
			schema.GetProperty("uId").GetGuid(),
			schema.GetProperty("parentSchema").GetProperty("uId").GetGuid());
	}

	private static object BuildTextEqualsSelectQuery(string rootSchemaName, string columnName, string value, string alias) {
		return new {
			rootSchemaName,
			operationType = 0,
			allColumns = false,
			isDistinct = false,
			ignoreDisplayValues = false,
			rowCount = -1,
			rowsOffset = -1,
			isPageable = false,
			conditionalValues = (object?)null,
			isHierarchical = false,
			hierarchicalMaxDepth = 0,
			hierarchicalColumnFiltersValue = new {
				filterType = 6,
				isEnabled = true,
				items = new Dictionary<string, object>(),
				logicalOperation = 0,
				trimDateTimeParameterToDate = false
			},
			hierarchicalColumnName = (string?)null,
			hierarchicalColumnValue = (object?)null,
			hierarchicalFullDataLoad = false,
			useLocalization = true,
			useRecordDeactivation = false,
			columns = new {
				items = new Dictionary<string, object> {
					[alias] = new {
						expression = new {
							expressionType = 0,
							columnPath = alias
						},
						orderDirection = 0,
						orderPosition = -1,
						isVisible = true
					}
				}
			},
			filters = new {
				filterType = 6,
				isEnabled = true,
				trimDateTimeParameterToDate = false,
				logicalOperation = 0,
				items = new Dictionary<string, object> {
					["filter0"] = new {
						filterType = 1,
						comparisonType = 3,
						isEnabled = true,
						trimDateTimeParameterToDate = false,
						leftExpression = new {
							expressionType = 0,
							columnPath = columnName
						},
						rightExpression = new {
							expressionType = 2,
							parameter = new {
								value,
								dataValueType = 1
							}
						}
					}
				}
			},
			__type = "Terrasoft.Nui.ServiceModel.DataContract.SelectQuery",
			queryKind = 0,
			serverESQCacheParameters = new {
				cacheLevel = 0,
				cacheGroup = string.Empty,
				cacheItemName = string.Empty
			},
			queryOptimize = false,
			useMetrics = false,
			querySource = 0
		};
	}

	private static async Task CreateEmptyWorkspaceAsync(
		McpE2ESettings settings,
		string rootDirectory,
		string workspaceName,
		CancellationToken cancellationToken) {
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["create-workspace", workspaceName, "--empty", "--directory", rootDirectory],
			workingDirectory: rootDirectory,
			cancellationToken: cancellationToken);
	}

	private static async Task AddPackageAsync(
		McpE2ESettings settings,
		string workspacePath,
		string packageName,
		CancellationToken cancellationToken) {
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["add-package", packageName],
			workingDirectory: workspacePath,
			cancellationToken: cancellationToken);
	}

	private static async Task PushWorkspaceAsync(
		McpE2ESettings settings,
		string workspacePath,
		string environmentName,
		CancellationToken cancellationToken) {
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["push-workspace", "-e", environmentName],
			workingDirectory: workspacePath,
			cancellationToken: cancellationToken);
	}

	private static async Task EnablePackageHotfixAsync(
		McpE2ESettings settings,
		string workspacePath,
		string packageName,
		string environmentName,
		CancellationToken cancellationToken) {
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["pkg-hotfix", packageName, "true", "-e", environmentName],
			workingDirectory: workspacePath,
			cancellationToken: cancellationToken);
	}

	private sealed record SandboxPackageArrangeContext(
		string RootDirectory,
		string EnvironmentName,
		string PackageName,
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
			if (Directory.Exists(RootDirectory)) {
				Directory.Delete(RootDirectory, recursive: true);
			}
		}
	}

	private sealed record InvalidEnvironmentArrangeContext(
		string EnvironmentName,
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}

	private sealed record AddonBusinessRuleSnapshot(
		string RuleName,
		string RuleCaption,
		IReadOnlyList<string> TriggerNames,
		string ResourceCaption,
		int ComparisonType,
		bool HasRightExpression,
		string? RightExpressionType,
		string? RightExpressionDataValueTypeName,
		string? RightExpressionValue);
}
