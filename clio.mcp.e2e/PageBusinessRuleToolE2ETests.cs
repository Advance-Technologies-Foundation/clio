using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using System.Text.Json.Nodes;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the page business-rule MCP tool.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature(CreatePageBusinessRuleTool.BusinessRuleCreateToolName)]
[NonParallelizable]
public sealed class PageBusinessRuleToolE2ETests : McpContractFixtureBase {
	private const string ToolName = CreatePageBusinessRuleTool.BusinessRuleCreateToolName;

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Exposes a page-only action contract for create-page-business-rules through the full get-tool-contract payload on the lazy tool surface.")]
	[AllureTag(ToolName)]
	[AllureName("Page business-rule MCP tool exposes a page-only action contract")]
	[AllureDescription("Starts the real clio MCP server, expands the create-page-business-rules contract via get-tool-contract, and verifies the action enum validator lists only page actions.")]
	public async Task BusinessRuleCreate_Should_Advertise_Page_Only_Action_Runtime_Schema() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		// The tool is a hidden long-tail tool on the lazy surface, so its raw polymorphic anyOf runtime
		// schema is no longer advertised through tools/list; the equivalent page-only action guarantee is
		// carried by the curated contract's action enum validator returned by get-tool-contract.
		CallToolResult contractResult = await arrangeContext.Session.CallToolAsync(
			ToolContractGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> { ["tool-names"] = new[] { ToolName } }
			},
			arrangeContext.CancellationTokenSource.Token);
		ToolContractGetResponse contracts =
			EntitySchemaStructuredResultParser.Extract<ToolContractGetResponse>(contractResult);

		// Assert
		ToolContractDefinition contract = contracts.Tools.Should().ContainSingle(tool => tool.Name == ToolName,
			because: "get-tool-contract must expand the create-page-business-rules contract on the lazy surface")
			.Which;
		ToolContractValidator actionValidator = contract.InputSchema.Validators.Should()
			.ContainSingle(validator => validator.Code == "unsupported-action",
				because: "the page business-rule contract must declare the enum validator that gates action types")
			.Which;
		actionValidator.Context.Should().Contain("hide-element",
			because: "page-only visibility actions must be listed as supported page action types");
		actionValidator.Context.Should().Contain("show-element",
			because: "page-only visibility actions must be listed as supported page action types");
		actionValidator.Context.Should().NotContain("set-values",
			because: "entity-only actions should not appear in the page business-rule action contract");
		actionValidator.Context.Should().NotContain("apply-filter,",
			because: "entity-only filter actions should not appear in the page business-rule action contract");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Binds a show-element page business-rule payload through the real MCP server and reports an invalid environment failure from command execution.")]
	[AllureTag(ToolName)]
	[AllureName("Page business-rule MCP tool binds show/hide payloads")]
	[AllureDescription("Starts the real clio MCP server, calls create-page-business-rule with a show-element payload and an intentionally missing environment, then verifies the request reaches command execution instead of failing MCP payload binding.")]
	public async Task BusinessRuleCreate_Should_Bind_ShowElement_Payload_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-page-business-rule-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["page-schema-name"] = "UsrCase_FormPage",
					["rules"] = new object[] { CreateShowElementRule()
 }
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid show-element payloads should bind and return the standard command execution envelope");
		execution.ExitCode.Should().NotBe(0,
			because: "the intentionally missing environment should fail during command execution");
		execution.Output.Should().Contain(message =>
				ContainsText(message.Value, invalidEnvironmentName),
			because: "the failure should come from resolving the requested environment, not from deserializing the page action payload");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Binds a system-variable page condition payload through the real MCP server and reports an invalid environment failure from command execution.")]
	[AllureTag(ToolName)]
	[AllureName("Page business-rule MCP tool binds system-variable conditions")]
	[AllureDescription("Starts the real clio MCP server, calls create-page-business-rule with a SysValue condition payload and an intentionally missing environment, then verifies the request reaches command execution instead of failing MCP payload binding.")]
	public async Task BusinessRuleCreate_Should_Bind_SysValue_Condition_Payload_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-page-sys-value-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["page-schema-name"] = "UsrCase_FormPage",
					["rules"] = new object[] { CreateSysValueConditionRule()
 }
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "valid system-variable page condition payloads should bind and return the standard command execution envelope");
		execution.ExitCode.Should().NotBe(0,
			because: "the intentionally missing environment should fail during command execution");
		execution.Output.Should().Contain(message =>
				ContainsText(message.Value, invalidEnvironmentName),
			because: "the failure should come from resolving the requested environment, not from deserializing the SysValue page payload");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Binds a multi-rule page batch payload through the real MCP server and reports an invalid environment failure for the whole batch.")]
	[AllureTag(ToolName)]
	[AllureName("Page business-rule MCP tool binds a multi-rule batch")]
	[AllureDescription("Starts the real clio MCP server, calls create-page-business-rules with two rules in one call and an intentionally missing environment, then verifies the multi-element rules array binds and the structured response references the missing environment instead of failing MCP payload binding.")]
	public async Task BusinessRuleCreate_Should_Bind_Multiple_Rules_Batch_And_Report_Invalid_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-page-batch-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = invalidEnvironmentName,
					["package-name"] = "UsrPkg",
					["page-schema-name"] = "UsrCase_FormPage",
					["rules"] = new object[] { CreateShowElementRule(), CreateSysValueConditionRule() }
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid two-rule page batch payload should bind and return the structured batch response, not an MCP binding error");
		callResult.Content!.Select(content => content.ToString()).Should().Contain(message =>
				ContainsText(message, invalidEnvironmentName),
			because: "the whole batch fails on the missing environment, so the structured response should reference it");
	}

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("Creates a page business rule for Contacts_FormPage in the Custom package through the real MCP server and Creatio environment.")]
	[AllureTag(ToolName)]
	[AllureName("Page business-rule MCP tool creates a Contact page rule")]
	[AllureDescription("Requires a reachable sandbox environment and destructive opt-in. Reads Contacts_FormPage, builds a valid page show/hide rule from its bundle, and verifies that Creatio command execution succeeds.")]
	public async Task BusinessRuleCreate_Should_Create_Contact_Page_Rule_In_Creatio() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("AllowDestructiveMcpTests is false - skipping destructive create-page-business-rule test.");
		}
		string environmentName = await ResolveReachableEnvironmentAsync(settings);
		string packageName = ResolvePackageName(settings);
		await ClioCliCommandRunner.EnsureCliogateInstalledAsync(settings, environmentName);
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(5));
		PageRuleTarget target = await ResolveContactPageRuleTargetAsync(
			arrangeContext.Session,
			arrangeContext.CancellationTokenSource.Token,
			environmentName);
		// Intentional destructive fixture: Contacts_FormPage in Custom is the canonical sandbox target.
		// There is no business-rule delete action yet, so each run writes a uniquely captioned rule and verifies readback.
		string caption = $"MCP E2E Contact page {Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["page-schema-name"] = target.PageSchemaName,
					["rules"] = new object[] { CreateContactPageRule(target, caption)
 }
				}
			},
			arrangeContext.CancellationTokenSource.Token);
		BusinessRuleBatchResponse batchResponse = McpCommandExecutionParser.ExtractBusinessRuleBatchResponse(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "a valid Contact page show/hide rule should return the structured batch-create response, not an MCP error");
		batchResponse.Created.Should().Be(1,
			because: "the single Contact page rule should be created in the configured Creatio sandbox");
		batchResponse.Failed.Should().Be(0,
			because: "no rule in the batch should fail when the payload is valid");
		batchResponse.Results.Should().ContainSingle(result => result.Success && !string.IsNullOrWhiteSpace(result.RuleName),
			because: "the per-rule result should report success and the generated internal rule name");
		await BusinessRuleAddonReadback.AssertPageRuleExistsAsync(
			settings,
			environmentName,
			packageName,
			target.RootSchemaUId,
			McpCommandExecutionParser.ExtractBusinessRuleName(batchResponse),
			"Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionHideElement",
			[target.ElementName],
			target.AttributeName,
			arrangeContext.CancellationTokenSource.Token);
	}

	private static bool ContainsText(string? value, string expectedText) =>
		value != null && value.Contains(expectedText, StringComparison.OrdinalIgnoreCase);

	private static string ResolvePackageName(McpE2ESettings settings) =>
		string.IsNullOrWhiteSpace(settings.Sandbox.PackageName)
			? "Custom"
			: settings.Sandbox.PackageName;

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) {
		string? configuredEnvironmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(configuredEnvironmentName)) {
			Assert.Ignore("create-page-business-rule MCP E2E requires McpE2E:Sandbox:EnvironmentName for destructive tests.");
			return string.Empty;
		}

		if (await CanReachEnvironmentAsync(settings, configuredEnvironmentName)) {
			return configuredEnvironmentName;
		}

		Assert.Ignore(
			$"create-page-business-rule MCP E2E requires a reachable configured sandbox environment. Environment '{configuredEnvironmentName}' was not reachable.");
		return string.Empty;
	}

	private static async Task<bool> CanReachEnvironmentAsync(McpE2ESettings settings, string environmentName) {
		using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));
		try {
			ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
				settings,
				["ping-app", "-e", environmentName],
				cancellationToken: cts.Token);
			return result.ExitCode == 0;
		} catch (OperationCanceledException) {
			return false;
		}
	}

	private static async Task<PageRuleTarget> ResolveContactPageRuleTargetAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName) {
		// The rule needs a real Freedom UI form page bound to an entity datasource. A base-Contact page
		// (Contacts_FormPage) only ships with CRM editions, so a bare Studio sandbox has none. Discover any
		// seeded custom form page instead (the AutoTest seed installs several) and build the rule on the
		// first one that exposes a datasource-bound attribute, preferring a Contact-bound page so the test
		// keeps its original Contact intent where the stand provides one.
		foreach (string candidate in await ResolveCandidatePageSchemaNamesAsync(session, cancellationToken, environmentName)) {
			PageRuleTarget? target = await TryResolvePageRuleTargetAsync(session, cancellationToken, environmentName, candidate);
			if (target is not null) {
				return target;
			}
		}

		Assert.Ignore(
			$"No seeded Freedom UI form page with a datasource-bound attribute was found on environment '{environmentName}'. " +
			"Ensure cliogate is installed and the AutoTest seed (or a Contact form page) is available in the sandbox before running this test.");
		return null!;
	}

	private static async Task<IReadOnlyList<string>> ResolveCandidatePageSchemaNamesAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName) {
		// Legacy base-Contact names first (real CRM stands), then any seeded custom form page.
		List<string> candidates = ["Contacts_FormPage", "Contact_FormPage"];

		CallToolResult listResult = await session.CallToolAsync(
			PageListTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["search-pattern"] = "Usr",
					["limit"] = 200,
					["environment-name"] = environmentName
				}
			},
			cancellationToken);
		PageListResponse pages = EntitySchemaStructuredResultParser.Extract<PageListResponse>(listResult);
		if (pages.Success && pages.Pages is not null) {
			// Prefer Contact-named pages so the rule stays Contact-bound where the seed provides one,
			// then fall back to any other seeded form page.
			IEnumerable<string> seededFormPages = pages.Pages
				.Select(page => page.SchemaName)
				.Where(name => !string.IsNullOrWhiteSpace(name)
					&& name.EndsWith("_FormPage", StringComparison.OrdinalIgnoreCase))
				.OrderByDescending(name => name.Contains("Contact", StringComparison.OrdinalIgnoreCase));
			foreach (string name in seededFormPages) {
				if (!candidates.Contains(name, StringComparer.OrdinalIgnoreCase)) {
					candidates.Add(name);
				}
			}
		}

		return candidates;
	}

	private static async Task<PageRuleTarget?> TryResolvePageRuleTargetAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName,
		string candidatePageSchemaName) {
		CallToolResult callResult = await session.CallToolAsync(
			PageGetTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = candidatePageSchemaName,
					["environment-name"] = environmentName
				}
			},
			cancellationToken);
		PageGetResponse response = EntitySchemaStructuredResultParser.Extract<PageGetResponse>(callResult);
		if (!response.Success || response.Files is null || !File.Exists(response.Files.BundleFile)) {
			return null;
		}

		JsonObject bundle = JsonNode.Parse(await File.ReadAllTextAsync(response.Files.BundleFile, cancellationToken))!.AsObject();
		PageAttributeCandidate? attribute = TryResolveRuleAttribute(bundle);
		if (attribute is null) {
			return null;
		}

		string? elementName = TryResolvePageElementName(bundle, attribute.AttributeName);
		if (string.IsNullOrWhiteSpace(elementName)) {
			return null;
		}

		return new PageRuleTarget(candidatePageSchemaName, response.Page.RootSchemaUId, attribute.AttributeName, elementName);
	}

	// Returns a datasource-bound attribute to drive the rule condition, or null when the page exposes none.
	// Preference: a Contact "Name" attribute (original intent) -> any Contact attribute -> any datasource-bound
	// attribute on a non-Contact seeded page. Never asserts so the caller can skip to the next candidate page.
	private static PageAttributeCandidate? TryResolveRuleAttribute(JsonObject bundle) {
		JsonObject attributes = bundle["viewModelConfig"]?["attributes"] as JsonObject ?? [];
		JsonObject dataSources = bundle["modelConfig"]?["dataSources"] as JsonObject ?? [];
		List<PageAttributeCandidate> contactCandidates = [];
		List<PageAttributeCandidate> anyCandidates = [];
		foreach ((string attributeName, JsonNode? attributeNode) in attributes) {
			if (attributeNode is not JsonObject attribute
				|| !TryResolveDatasourcePath(attribute, out string datasourceName, out string columnName)) {
				continue;
			}
			PageAttributeCandidate candidate = new(attributeName, columnName);
			anyCandidates.Add(candidate);
			if (IsContactDatasource(dataSources, datasourceName)) {
				contactCandidates.Add(candidate);
			}
		}

		static PageAttributeCandidate? PreferName(List<PageAttributeCandidate> source) =>
			source.FirstOrDefault(candidate =>
				string.Equals(candidate.ColumnName, "Name", StringComparison.OrdinalIgnoreCase))
			?? source.FirstOrDefault();

		return PreferName(contactCandidates) ?? PreferName(anyCandidates);
	}

	// Returns the page element to hide, or null when the viewConfig exposes no named element.
	private static string? TryResolvePageElementName(JsonObject bundle, string attributeName) {
		string? boundElement = EnumerateObjects(bundle["viewConfig"])
			.FirstOrDefault(obj => string.Equals(obj["control"]?.GetValue<string>(), $"${attributeName}", StringComparison.Ordinal))
			?["name"]?.GetValue<string>();
		if (!string.IsNullOrWhiteSpace(boundElement)) {
			return boundElement;
		}

		return EnumerateObjects(bundle["viewConfig"])
			.Select(obj => obj["name"]?.GetValue<string>())
			.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
	}

	private static IEnumerable<JsonObject> EnumerateObjects(JsonNode? node) {
		Stack<JsonNode?> pending = new();
		pending.Push(node);
		while (pending.Count > 0) {
			JsonNode? current = pending.Pop();
			switch (current) {
				case JsonObject obj:
					yield return obj;
					foreach (KeyValuePair<string, JsonNode?> property in obj) {
						pending.Push(property.Value);
					}
					break;
				case JsonArray array:
					foreach (JsonNode? item in array) {
						pending.Push(item);
					}
					break;
			}
		}
	}

	private static bool TryResolveDatasourcePath(
		JsonObject attribute,
		out string datasourceName,
		out string columnName) {
		datasourceName = string.Empty;
		columnName = string.Empty;
		string path = attribute["modelConfig"]?["path"]?.GetValue<string>() ?? string.Empty;
		string[] parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length != 2) {
			return false;
		}
		datasourceName = parts[0];
		columnName = parts[1];
		return true;
	}

	private static bool IsContactDatasource(JsonObject dataSources, string datasourceName) =>
		string.Equals(
			dataSources[datasourceName]?["config"]?["entitySchemaName"]?.GetValue<string>(),
			"Contact",
			StringComparison.Ordinal);

	private static IReadOnlyDictionary<string, object?> CreateContactPageRule(PageRuleTarget target, string caption) =>
		new Dictionary<string, object?> {
			["caption"] = caption,
			["condition"] = new Dictionary<string, object?> {
				["logicalOperation"] = "AND",
				["conditions"] = new object[] {
					new Dictionary<string, object?> {
						["leftExpression"] = CreateAttributeExpression(target.AttributeName),
						["comparisonType"] = "is-filled-in"
					}
				}
			},
			["actions"] = new object[] {
				new Dictionary<string, object?> {
					["type"] = "hide-element",
					["items"] = new object[] { target.ElementName }
				}
			}
		};

	private static IReadOnlyDictionary<string, object?> CreateShowElementRule() =>
		new Dictionary<string, object?> {
			["caption"] = "Show escalation for high priority",
			["condition"] = new Dictionary<string, object?> {
				["logicalOperation"] = "AND",
				["conditions"] = new object[] {
					new Dictionary<string, object?> {
						["leftExpression"] = CreateAttributeExpression("PDS_Priority"),
						["comparisonType"] = "equal",
						["rightExpression"] = new Dictionary<string, object?> {
							["type"] = "Const",
							["value"] = "High"
						}
					}
				}
			},
			["actions"] = new object[] {
				new Dictionary<string, object?> {
					["type"] = "show-element",
					["items"] = new object[] { "EscalateButton" }
				}
			}
		};

	private static IReadOnlyDictionary<string, object?> CreateSysValueConditionRule() =>
		new Dictionary<string, object?> {
			["caption"] = "Hide reminder when due on or before today",
			["condition"] = new Dictionary<string, object?> {
				["logicalOperation"] = "AND",
				["conditions"] = new object[] {
					new Dictionary<string, object?> {
						["leftExpression"] = CreateAttributeExpression("PDS_UsrDueDate"),
						["comparisonType"] = "less-than-or-equal",
						["rightExpression"] = new Dictionary<string, object?> {
							["type"] = "SysValue",
							["sysValueName"] = "CurrentDate"
						}
					}
				}
			},
			["actions"] = new object[] {
				new Dictionary<string, object?> {
					["type"] = "hide-element",
					["items"] = new object[] { "ReminderLabel" }
				}
			}
		};

	private static IReadOnlyDictionary<string, object?> CreateAttributeExpression(string path) =>
		new Dictionary<string, object?> {
			["type"] = "AttributeValue",
			["path"] = path
		};

	private sealed record PageAttributeCandidate(string AttributeName, string ColumnName);

	private sealed record PageRuleTarget(string PageSchemaName, string RootSchemaUId, string AttributeName, string ElementName);
}
