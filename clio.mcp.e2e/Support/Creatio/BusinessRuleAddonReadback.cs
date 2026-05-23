using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Mcp.E2E.Support.Configuration;
using FluentAssertions;

namespace Clio.Mcp.E2E.Support.Creatio;

internal static class BusinessRuleAddonReadback {
	public static async Task AssertEntityRuleExistsAsync(
		McpE2ESettings settings,
		string environmentName,
		string packageName,
		string entitySchemaName,
		string expectedCaption,
		string expectedActionTypeName,
		IReadOnlyCollection<string> expectedActionItems,
		string expectedConditionPath,
		CancellationToken cancellationToken) {
		string packageUId = await QueryPackageUIdAsync(settings, environmentName, packageName, cancellationToken);
		SchemaMetadata schema = await QueryEntityTargetSchemaMetadataAsync(
			settings,
			environmentName,
			entitySchemaName,
			packageUId,
			cancellationToken);
		await AssertRuleExistsInAddonAsync(
			settings,
			environmentName,
			packageUId,
			schema,
			"EntitySchemaManager",
			expectedCaption,
			expectedActionTypeName,
			expectedActionItems,
			expectedConditionPath,
			cancellationToken);
	}

	public static async Task AssertEntityApplyFilterRuleFamilyExistsAsync(
		McpE2ESettings settings,
		string environmentName,
		string packageName,
		string entitySchemaName,
		string expectedCaption,
		string targetPath,
		string targetFilterPath,
		string sourcePath,
		string? sourceFilterPath,
		bool expectClearChild,
		bool expectPopulateChild,
		CancellationToken cancellationToken) {
		string packageUId = await QueryPackageUIdAsync(settings, environmentName, packageName, cancellationToken);
		SchemaMetadata schema = await QueryEntityTargetSchemaMetadataAsync(
			settings,
			environmentName,
			entitySchemaName,
			packageUId,
			cancellationToken);
		JsonObject addonResponse = await CallJsonServiceAsync(
			settings,
			environmentName,
			"ServiceModel/AddonSchemaDesignerService.svc/GetSchema",
			new JsonObject {
				["addonName"] = "BusinessRule",
				["targetSchemaUId"] = schema.UId,
				["targetParentSchemaUId"] = schema.ParentUId,
				["targetPackageUId"] = packageUId,
				["targetSchemaManagerName"] = "EntitySchemaManager",
				["useFullHierarchy"] = true
			},
			cancellationToken);

		addonResponse["success"]?.GetValue<bool>().Should().BeTrue(
			because: "BusinessRule add-on readback should succeed for the same entity schema targeted by the destructive apply-filter test");
		string? metadata = addonResponse["schema"]?["metaData"]?.GetValue<string>();
		metadata.Should().NotBeNullOrWhiteSpace(
			because: "BusinessRule add-on readback should include the saved apply-filter metadata payload");
		AssertApplyFilterRuleFamilyMetadata(
			metadata!,
			expectedCaption,
			targetPath,
			targetFilterPath,
			sourcePath,
			sourceFilterPath,
			expectClearChild,
			expectPopulateChild);
	}

	public static async Task AssertPageRuleExistsAsync(
		McpE2ESettings settings,
		string environmentName,
		string packageName,
		string rootSchemaUId,
		string expectedCaption,
		string expectedActionTypeName,
		IReadOnlyCollection<string> expectedActionItems,
		string expectedConditionPath,
		CancellationToken cancellationToken) {
		rootSchemaUId.Should().NotBeNullOrWhiteSpace(
			because: "get-page should return the root schema identifier needed to resolve the page business-rule add-on target");
		string packageUId = await QueryPackageUIdAsync(settings, environmentName, packageName, cancellationToken);
		SchemaMetadata schema = await QueryPageTargetSchemaMetadataAsync(
			settings,
			environmentName,
			rootSchemaUId,
			packageUId,
			cancellationToken);
		await AssertRuleExistsInAddonAsync(
			settings,
			environmentName,
			packageUId,
			schema,
			"ClientUnitSchemaManager",
			expectedCaption,
			expectedActionTypeName,
			expectedActionItems,
			expectedConditionPath,
			cancellationToken);
	}

	private static async Task AssertRuleExistsInAddonAsync(
		McpE2ESettings settings,
		string environmentName,
		string packageUId,
		SchemaMetadata schema,
		string schemaManagerName,
		string expectedCaption,
		string expectedActionTypeName,
		IReadOnlyCollection<string> expectedActionItems,
		string expectedConditionPath,
		CancellationToken cancellationToken) {
		JsonObject addonResponse = await CallJsonServiceAsync(
			settings,
			environmentName,
			"ServiceModel/AddonSchemaDesignerService.svc/GetSchema",
			new JsonObject {
				["addonName"] = "BusinessRule",
				["targetSchemaUId"] = schema.UId,
				["targetParentSchemaUId"] = schema.ParentUId,
				["targetPackageUId"] = packageUId,
				["targetSchemaManagerName"] = schemaManagerName,
				["useFullHierarchy"] = true
			},
			cancellationToken);

		addonResponse["success"]?.GetValue<bool>().Should().BeTrue(
			because: "BusinessRule add-on readback should succeed for the same schema and package targeted by the destructive MCP test");
		string? metadata = addonResponse["schema"]?["metaData"]?.GetValue<string>();
		metadata.Should().NotBeNullOrWhiteSpace(
			because: "BusinessRule add-on readback should include the saved metadata payload");
		AssertRuleMetadata(metadata!, expectedCaption, expectedActionTypeName, expectedActionItems, expectedConditionPath);
	}

	private static async Task<SchemaMetadata> QueryEntityTargetSchemaMetadataAsync(
		McpE2ESettings settings,
		string environmentName,
		string entitySchemaName,
		string packageUId,
		CancellationToken cancellationToken) {
		JsonObject response = await CallJsonServiceAsync(
			settings,
			environmentName,
			"ServiceModel/EntitySchemaDesignerService.svc/GetSchemaDesignItem",
			new JsonObject {
				["name"] = entitySchemaName,
				["packageUId"] = packageUId,
				["useFullHierarchy"] = true,
				["cultures"] = new JsonArray("en-US")
			},
			cancellationToken);
		response["success"]?.GetValue<bool>().Should().BeTrue(
			because: "entity business-rule readback should use the same designer resolution as the create path");
		JsonObject? schema = response["schema"] as JsonObject;
		schema.Should().NotBeNull(
			because: "EntitySchemaDesignerService.GetSchemaDesignItem should return the exact entity schema targeted by creation");
		string? schemaUId = GetSchemaUId(schema);
		string parentUId = GetSchemaUId(schema!["parentSchema"]) ?? Guid.Empty.ToString();
		schemaUId.Should().NotBeNullOrWhiteSpace(
			because: "the exact entity schema identifier is needed for add-on readback");
		return new SchemaMetadata(schemaUId!, parentUId);
	}

	private static async Task<SchemaMetadata> QueryPageTargetSchemaMetadataAsync(
		McpE2ESettings settings,
		string environmentName,
		string rootSchemaUId,
		string packageUId,
		CancellationToken cancellationToken) {
		JsonObject response = await CallJsonServiceAsync(
			settings,
			environmentName,
			"ServiceModel/ClientUnitSchemaDesignerService.svc/GetParentSchemas",
			new JsonObject {
				["schemaUId"] = rootSchemaUId,
				["packageUId"] = packageUId,
				["useFullHierarchy"] = true,
				["userLevelSchema"] = false
			},
			cancellationToken);
		response["success"]?.GetValue<bool>().Should().BeTrue(
			because: "page business-rule readback should resolve the same target-package hierarchy used by the create path");
		JsonArray? values = response["values"] as JsonArray;
		values.Should().NotBeNullOrEmpty(
			because: "page business-rule readback should receive at least the target schema from the designer hierarchy");
		string? schemaUId = GetSchemaUId(values![0]);
		string parentUId = values.Count > 1
			? GetSchemaUId(values[1]) ?? Guid.Empty.ToString()
			: Guid.Empty.ToString();
		schemaUId.Should().NotBeNullOrWhiteSpace(
			because: "the first designer hierarchy item is the actual page schema targeted by the business-rule add-on");
		return new SchemaMetadata(schemaUId!, parentUId);
	}

	private static async Task<string> QueryPackageUIdAsync(
		McpE2ESettings settings,
		string environmentName,
		string packageName,
		CancellationToken cancellationToken) {
		JsonObject response = await ExecuteSelectQueryAsync(
			settings,
			environmentName,
			"SysPackage",
			BuildFilterGroup(("byName", BuildEqFilter("Name", 1, packageName))),
			BuildColumns(("UId", "UId")),
			cancellationToken);
		JsonArray rows = ReadRows(response, $"Package '{packageName}' should exist in the destructive MCP sandbox.");
		string? packageUId = rows[0]?["UId"]?.GetValue<string>();
		packageUId.Should().NotBeNullOrWhiteSpace(
			because: "SysPackage query should return the target package identifier for add-on readback");
		return packageUId!;
	}

	private static async Task<JsonObject> ExecuteSelectQueryAsync(
		McpE2ESettings settings,
		string environmentName,
		string rootSchemaName,
		JsonObject filters,
		JsonObject columns,
		CancellationToken cancellationToken) =>
		await CallJsonServiceAsync(
			settings,
			environmentName,
			"/DataService/json/SyncReply/SelectQuery",
			new JsonObject {
				["rootSchemaName"] = rootSchemaName,
				["operationType"] = 0,
				["filters"] = filters,
				["columns"] = columns,
				["rowCount"] = 1
			},
			cancellationToken);

	private static async Task<JsonObject> CallJsonServiceAsync(
		McpE2ESettings settings,
		string environmentName,
		string servicePath,
		JsonObject body,
		CancellationToken cancellationToken) {
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
			settings,
			[
				"call-service",
				"-e", environmentName,
				"--service-path", servicePath,
				"-b", body.ToJsonString()
			],
			cancellationToken: cancellationToken);
		result.ExitCode.Should().Be(0,
			because: $"readback service calls should succeed. stdout: {result.StandardOutput}. stderr: {result.StandardError}");
		return ParseJsonObject(result.StandardOutput);
	}

	private static JsonArray ReadRows(JsonObject response, string because) {
		response["success"]?.GetValue<bool>().Should().BeTrue(
			because: because);
		JsonArray? rows = response["rows"] as JsonArray;
		rows.Should().NotBeNull(because: because);
		rows!.Should().NotBeEmpty(because: because);
		return rows;
	}

	private static JsonObject ParseJsonObject(string output) {
		string trimmed = output.Trim();
		int start = trimmed.IndexOf('{');
		int end = trimmed.LastIndexOf('}');
		start.Should().BeGreaterThanOrEqualTo(0,
			because: $"service output should contain a JSON object. output: {output}");
		end.Should().BeGreaterThan(start,
			because: $"service output should contain a complete JSON object. output: {output}");
		return JsonNode.Parse(trimmed[start..(end + 1)])!.AsObject();
	}

	private static void AssertRuleMetadata(
		string metadata,
		string expectedCaption,
		string expectedActionTypeName,
		IReadOnlyCollection<string> expectedActionItems,
		string expectedConditionPath) {
		JsonObject root = JsonNode.Parse(metadata)!.AsObject();
		JsonArray rules = root["rules"] as JsonArray ?? [];
		JsonObject? rule = rules.OfType<JsonObject>().SingleOrDefault(candidate =>
			string.Equals(candidate["caption"]?.GetValue<string>(), expectedCaption, StringComparison.Ordinal));
		rule.Should().NotBeNull(
			because: "the destructive MCP test should verify the rule was persisted to the intended add-on target");
		AssertConditionMetadata(rule!, expectedConditionPath);
		AssertActionMetadata(rule!, expectedActionTypeName, expectedActionItems);
		AssertTriggerMetadata(rule!, expectedConditionPath);
	}

	private static void AssertApplyFilterRuleFamilyMetadata(
		string metadata,
		string expectedCaption,
		string targetPath,
		string targetFilterPath,
		string sourcePath,
		string? sourceFilterPath,
		bool expectClearChild,
		bool expectPopulateChild) {
		JsonObject root = JsonNode.Parse(metadata)!.AsObject();
		JsonArray rules = root["rules"] as JsonArray ?? [];
		JsonObject? parentRule = rules.OfType<JsonObject>().SingleOrDefault(candidate =>
			string.Equals(candidate["caption"]?.GetValue<string>(), expectedCaption, StringComparison.Ordinal));
		parentRule.Should().NotBeNull(
			because: "the destructive MCP test should verify the parent apply-filter rule was persisted");

		JsonObject parentAction = parentRule!["cases"]?[0]?["actions"]?[0]!.AsObject()
			?? throw new InvalidOperationException("The persisted parent apply-filter rule should contain exactly one action.");
		ReadString(parentAction["typeName"]).Should().Be("Terrasoft.Core.BusinessRules.Models.Actions.BusinessRuleActionFilterLookup",
			because: "apply-filter rules should persist the filter-lookup action type");
		ReadString(parentAction["leftExpression"]?["path"]).Should().Be(targetPath,
			because: "the parent apply-filter rule should preserve the target lookup path");
		ReadString(parentAction["leftExpression"]?["filterExpression"]).Should().Be(targetFilterPath,
			because: "the parent apply-filter rule should preserve the target-side filter path");
		ReadString(parentAction["rightExpression"]?["path"]).Should().Be(sourcePath,
			because: "the parent apply-filter rule should preserve the source lookup path");
		ReadString(parentAction["rightExpression"]?["filterExpression"]).Should().Be(
			string.IsNullOrWhiteSpace(sourceFilterPath) ? "null" : sourceFilterPath,
			because: "the parent apply-filter rule should preserve the source-side filter path or the UI null sentinel");
		parentAction["clearValue"]?.GetValue<bool>().Should().Be(expectClearChild,
			because: "the parent apply-filter rule should reflect whether an autogenerated clear child rule was requested");
		parentAction["populateValue"]?.GetValue<bool>().Should().Be(expectPopulateChild,
			because: "the parent apply-filter rule should reflect whether an autogenerated populate child rule was requested");

		string parentRuleUId = ReadString(parentRule["uId"]) ?? throw new InvalidOperationException("The persisted parent rule should have a uId.");
		string parentActionUId = ReadString(parentAction["uId"]) ?? throw new InvalidOperationException("The persisted parent action should have a uId.");
		AssertApplyFilterParentTriggers(parentRule, sourcePath);

		List<JsonObject> childRules = rules.OfType<JsonObject>()
			.Where(candidate =>
				string.Equals(ReadString(candidate["parentUId"]), parentRuleUId, StringComparison.Ordinal)
				&& string.Equals(ReadString(candidate["parentActionUId"]), parentActionUId, StringComparison.Ordinal))
			.ToList();
		childRules.Count.Should().Be((expectClearChild ? 1 : 0) + (expectPopulateChild ? 1 : 0),
			because: "the persisted add-on metadata should contain exactly the requested autogenerated apply-filter child rules");

		string targetRelatedPath = $"{targetPath}.{targetFilterPath}";
		string sourceComparisonPath = string.IsNullOrWhiteSpace(sourceFilterPath)
			? sourcePath
			: $"{sourcePath}.{sourceFilterPath}";
		if (expectClearChild) {
			JsonObject clearRule = childRules.Single(rule =>
				string.Equals(ReadString(rule["caption"]), $"ChildRule-{parentRuleUId}-ClearValue", StringComparison.Ordinal));
			AssertApplyFilterClearChild(clearRule, sourceComparisonPath, targetPath, targetRelatedPath);
		}

		if (expectPopulateChild) {
			JsonObject populateRule = childRules.Single(rule =>
				string.Equals(ReadString(rule["caption"]), $"ChildRule-{parentRuleUId}-PopulateValue", StringComparison.Ordinal));
			AssertApplyFilterPopulateChild(populateRule, targetPath, sourcePath, targetRelatedPath);
		}
	}

	private static void AssertApplyFilterParentTriggers(JsonObject parentRule, string expectedSourcePath) {
		JsonArray? triggers = parentRule["triggers"] as JsonArray;
		triggers.Should().NotBeNullOrEmpty(
			because: "the persisted apply-filter parent rule should include trigger metadata");
		triggers!.OfType<JsonObject>().Any(trigger =>
			ReadInt(trigger["type"]) == 2
			&& string.IsNullOrEmpty(ReadString(trigger["name"])))
			.Should().BeTrue(
			because: "apply-filter parent rules should reload on data loaded");
		triggers.OfType<JsonObject>().Any(trigger =>
			ReadInt(trigger["type"]) == 0
			&& string.Equals(ReadString(trigger["name"]), expectedSourcePath, StringComparison.Ordinal))
			.Should().BeTrue(
			because: "apply-filter parent rules should react when the source lookup changes");
	}

	private static void AssertApplyFilterClearChild(
		JsonObject clearRule,
		string sourceComparisonPath,
		string targetPath,
		string targetRelatedPath) {
		JsonObject clearCondition = clearRule["cases"]?[0]?["condition"]!.AsObject()
			?? throw new InvalidOperationException("The persisted apply-filter clear child should contain a direct condition.");
		ReadInt(clearCondition["comparisonType"]).Should().Be(2,
			because: "the clear child should compare source and target filter values for mismatch");
		ReadString(clearCondition["leftExpression"]?["path"]).Should().Be(sourceComparisonPath,
			because: "the clear child should compare against the source lookup path");
		ReadString(clearCondition["rightExpression"]?["path"]).Should().Be(targetRelatedPath,
			because: "the clear child should compare against the resolved target lookup path");

		JsonObject clearAction = clearRule["cases"]?[0]?["actions"]?[0]!.AsObject()
			?? throw new InvalidOperationException("The persisted apply-filter clear child should contain a set-values action.");
		JsonArray clearItems = clearAction["items"]!.AsArray();
		clearItems.Should().HaveCount(1,
			because: "the clear child should reset exactly one target lookup");
		ReadString(clearItems[0]?["expression"]?["path"]).Should().Be(targetPath,
			because: "the clear child should reset the target lookup root");
		ReadString(clearItems[0]?["value"]?["typeName"]).Should().Be("Terrasoft.Core.BusinessRules.Models.Expressions.BusinessRuleEmptyValueExpression",
			because: "the clear child should assign an empty lookup expression");
	}

	private static void AssertApplyFilterPopulateChild(
		JsonObject populateRule,
		string targetPath,
		string sourcePath,
		string targetRelatedPath) {
		JsonObject populateCondition = populateRule["cases"]?[0]?["condition"]!.AsObject()
			?? throw new InvalidOperationException("The persisted apply-filter populate child should contain a direct condition.");
		ReadInt(populateCondition["comparisonType"]).Should().Be(9,
			because: "the populate child should only run when the target lookup is filled in");
		ReadString(populateCondition["leftExpression"]?["path"]).Should().Be(targetPath,
			because: "the populate child should react to the selected target lookup");

		JsonObject populateAction = populateRule["cases"]?[0]?["actions"]?[0]!.AsObject()
			?? throw new InvalidOperationException("The persisted apply-filter populate child should contain a set-values action.");
		JsonArray populateItems = populateAction["items"]!.AsArray();
		populateItems.Should().HaveCount(1,
			because: "the populate child should assign exactly one source lookup");
		ReadString(populateItems[0]?["expression"]?["path"]).Should().Be(sourcePath,
			because: "the populate child should write back to the source lookup");
		ReadString(populateItems[0]?["value"]?["path"]).Should().Be(targetRelatedPath,
			because: "the populate child should copy the resolved target lookup path");
	}

	private static void AssertConditionMetadata(JsonObject rule, string expectedConditionPath) {
		JsonArray? conditions = rule["cases"]?[0]?["condition"]?["conditions"] as JsonArray;
		conditions.Should().NotBeNullOrEmpty(
			because: "the persisted business-rule payload should include the expected condition metadata");
		conditions!.OfType<JsonObject>().Any(condition =>
			string.Equals(ReadString(condition["leftExpression"]?["path"]), expectedConditionPath, StringComparison.Ordinal))
			.Should().BeTrue(
			because: "the persisted condition should target the attribute selected by the destructive E2E payload");
	}

	private static void AssertActionMetadata(
		JsonObject rule,
		string expectedActionTypeName,
		IReadOnlyCollection<string> expectedActionItems) {
		JsonArray? actions = rule["cases"]?[0]?["actions"] as JsonArray;
		actions.Should().NotBeNullOrEmpty(
			because: "the persisted business-rule payload should include action metadata");
		JsonObject? action = actions!.OfType<JsonObject>().SingleOrDefault(candidate =>
			string.Equals(candidate["typeName"]?.GetValue<string>(), expectedActionTypeName, StringComparison.Ordinal));
		action.Should().NotBeNull(
			because: "the persisted action should use the action metadata type selected by the destructive E2E payload");
		IReadOnlySet<string> actualItems = ReadActionItems(action!);
		actualItems.Should().BeEquivalentTo(expectedActionItems,
			because: "the persisted action should target exactly the attributes or elements selected by the destructive E2E payload");
	}

	private static void AssertTriggerMetadata(JsonObject rule, string expectedConditionPath) {
		JsonArray? triggers = rule["triggers"] as JsonArray;
		triggers.Should().NotBeNullOrEmpty(
			because: "the persisted business-rule payload should include trigger metadata");
		triggers!.OfType<JsonObject>().Any(trigger =>
			ReadInt(trigger["type"]) == 2
			&& string.IsNullOrEmpty(ReadString(trigger["name"])))
			.Should().BeTrue(
			because: "business rules should reload on data loaded");
		triggers.OfType<JsonObject>().Any(trigger =>
			ReadInt(trigger["type"]) == 0
			&& string.Equals(ReadString(trigger["name"]), expectedConditionPath, StringComparison.Ordinal))
			.Should().BeTrue(
			because: "business rules should react when the condition attribute changes");
	}

	private static IReadOnlySet<string> ReadActionItems(JsonObject action) {
		JsonNode? itemsNode = action["items"];
		if (itemsNode is JsonValue value
			&& value.TryGetValue(out string? items)
			&& items is not null) {
			return items
				.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.ToHashSet(StringComparer.Ordinal);
		}

		if (itemsNode is JsonArray array) {
			return array
				.Select(item => item?.GetValue<string>())
				.Where(item => !string.IsNullOrWhiteSpace(item))
				.ToHashSet(StringComparer.Ordinal)!;
		}

		return new HashSet<string>(StringComparer.Ordinal);
	}

	private static string? ReadString(JsonNode? node) =>
		node is null ? null : node.GetValue<string>();

	private static int? ReadInt(JsonNode? node) =>
		node is null ? null : node.GetValue<int>();

	private static string? GetSchemaUId(JsonNode? schemaNode) =>
		schemaNode?["uId"]?.GetValue<string>()
		?? schemaNode?["id"]?.GetValue<string>();

	private static JsonObject BuildFilterGroup(params (string Key, JsonObject Filter)[] filters) {
		JsonObject items = [];
		foreach ((string key, JsonObject filter) in filters) {
			items[key] = filter;
		}
		return new JsonObject {
			["filterType"] = 6,
			["logicalOperation"] = 0,
			["isEnabled"] = true,
			["items"] = items
		};
	}

	private static JsonObject BuildEqFilter(string columnPath, int dataValueType, string value) =>
		new() {
			["filterType"] = 1,
			["comparisonType"] = 3,
			["isEnabled"] = true,
			["leftExpression"] = new JsonObject {
				["expressionType"] = 0,
				["columnPath"] = columnPath
			},
			["rightExpression"] = new JsonObject {
				["expressionType"] = 2,
				["parameter"] = new JsonObject {
					["dataValueType"] = dataValueType,
					["value"] = value
				}
			}
		};

	private static JsonObject BuildColumns(params (string Alias, string Path)[] columns) {
		JsonObject items = [];
		foreach ((string alias, string path) in columns) {
			items[alias] = new JsonObject {
				["expression"] = new JsonObject {
					["expressionType"] = 0,
					["columnPath"] = path
				}
			};
		}
		return new JsonObject {
			["items"] = items
		};
	}

	private sealed record SchemaMetadata(string UId, string ParentUId);
}
