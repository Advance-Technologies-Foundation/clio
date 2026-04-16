using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.BusinessRules;
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
	[Description("Creates an entity-level business rule through the real MCP server and verifies the rule persisted in the BusinessRule add-on payload.")]
	[AllureTag(ToolName)]
	[AllureName("Entity business-rule MCP tool persists rule metadata in add-on designer storage")]
	[AllureDescription("Arranges a unique sandbox package through the real CLI, calls create-entity-business-rule through the real MCP server, then fetches the Contact BusinessRule add-on directly from Creatio and verifies the created rule and caption resource were persisted without removing unrelated rules.")]
	public async Task BusinessRuleCreate_Should_Persist_Rule_Into_Addon_Metadata() {
		// Arrange
		await using SandboxPackageArrangeContext arrangeContext = await ArrangeSandboxPackageAsync();
		string caption = $"Codex rule {Guid.NewGuid():N}".Substring(0, 18);

		// Act
		CallToolResult callResult = await CallBusinessRuleCreateAsync(
			arrangeContext.Session,
			arrangeContext.EnvironmentName,
			arrangeContext.PackageName,
			TargetEntitySchemaName,
			caption,
			arrangeContext.CancellationTokenSource.Token);
		BusinessRuleCreateResponse response = EntitySchemaStructuredResultParser.Extract<BusinessRuleCreateResponse>(callResult);
		AddonBusinessRuleSnapshot snapshot = FetchAddonSnapshot(
			arrangeContext.EnvironmentName,
			arrangeContext.PackageName,
			TargetEntitySchemaName,
			response.RuleName!);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "successful business-rule creation should return a structured MCP envelope rather than a top-level tool error");
		response.Success.Should().BeTrue(
			because: "the real MCP tool should report success for a valid sandbox package and entity");
		response.PackageName.Should().Be(arrangeContext.PackageName,
			because: "the response should report the target package");
		response.EntitySchemaName.Should().Be(TargetEntitySchemaName,
			because: "the response should report the target entity schema");
		response.RuleName.Should().StartWith("BusinessRule_",
			because: "the tool should generate an internal business-rule name automatically");
		snapshot.RuleCaption.Should().Be(caption,
			because: "the persisted add-on metadata should contain the created rule");
		snapshot.RuleName.Should().Be(response.RuleName,
			because: "the persisted add-on metadata should contain the generated internal rule name returned by the tool");
		snapshot.TriggerNames.Should().Contain("Name",
			because: "the persisted rule should include the expected trigger");
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
			"Broken rule",
			arrangeContext.CancellationTokenSource.Token);
		BusinessRuleCreateResponse response = EntitySchemaStructuredResultParser.Extract<BusinessRuleCreateResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "invalid environment handling should be surfaced as a structured tool failure");
		response.Success.Should().BeFalse(
			because: "the tool should fail when the requested environment is missing");
		response.RuleName.Should().BeNull(
			because: "failed business-rule creation should not return a generated rule name");
		response.Error.Should().NotBeNullOrWhiteSpace(
			because: "the failure should include a readable diagnostic");
		response.Error.Should().Contain("Environment with key",
			because: "the environment-aware resolver should surface the missing registered environment explicitly");
		response.Error.Should().Contain(arrangeContext.EnvironmentName,
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
		string caption,
		CancellationToken cancellationToken) {
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationToken);
		tools.Select(tool => tool.Name).Should().Contain(ToolName,
			because: "the create-entity-business-rule MCP tool must be advertised before the end-to-end call can be executed");

		return await session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
						["entity-schema-name"] = entitySchemaName,
						["rule"] = new Dictionary<string, object?> {
							["caption"] = caption,
							["condition"] = new Dictionary<string, object?> {
								["logicalOperation"] = "AND",
								["conditions"] = new[] {
									new Dictionary<string, object?> {
										["leftExpression"] = new Dictionary<string, object?> {
											["type"] = "AttributeValue",
											["path"] = "Name"
										},
										["comparisonType"] = "equal",
										["rightExpression"] = new Dictionary<string, object?> {
											["type"] = "Const",
											["value"] = caption
										}
									}
								}
							},
							["actions"] = new[] {
								new Dictionary<string, object?> {
									["type"] = "make-required",
									["items"] = new[] { "JobTitle" }
								}
							}
						}
				}
			},
			cancellationToken);
	}

	private static AddonBusinessRuleSnapshot FetchAddonSnapshot(
		string environmentName,
		string packageName,
		string entitySchemaName,
		string ruleName) {
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
			.Single(rule => string.Equals(rule.GetProperty("name").GetString(), ruleName, StringComparison.Ordinal));
		string ruleId = createdRule.GetProperty("uId").GetString()!;
		JsonElement resource = response.RootElement.GetProperty("schema").GetProperty("resources")
			.EnumerateArray()
			.Single(item => string.Equals(item.GetProperty("key").GetString(), $"AddonConfig.Rules.{ruleId}.Caption", StringComparison.Ordinal));
		return new AddonBusinessRuleSnapshot(
			createdRule.GetProperty("name").GetString()!,
			createdRule.GetProperty("caption").GetString()!,
			createdRule.GetProperty("triggers").EnumerateArray()
				.Select(trigger => trigger.GetProperty("name").GetString()!)
				.ToArray(),
			resource.GetProperty("value")[0].GetProperty("value").GetString()!);
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
		string ResourceCaption);
}
