using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.BusinessRules;
using Clio.Common;
using static Clio.Command.BusinessRules.BusinessRuleConstants;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Read-only environment probe that fetches the PAGE-level business rules of a source web page from
/// its <c>BusinessRule</c> add-on schema metadata and parses them into an intermediate read-model
/// (already reverse-mapped into the <c>create-page-business-rule</c> input shape). The conversion
/// itself is pure and lives in <see cref="WebToMobileAnalysisService.ConvertPageBusinessRules"/>.
/// Any failure degrades gracefully to <see cref="PageBusinessRuleProbeResult.ProbeOk"/> = false with
/// a note; it never throws so the conversion guide can always be returned. It writes nothing.
/// </summary>
public static class PageBusinessRuleProbe {

	private static readonly IReadOnlyDictionary<int, string> ComparisonNameByValue =
		SupportedComparisonTypeValues.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

	// typeName -> short action type. Page rules support ONLY element actions: hide/show + make-*.
	// Set values / apply filter / apply static filter do not exist at page level and are not recognized.
	private static readonly IReadOnlyDictionary<string, string> ActionShortByTypeName =
		SupportedPageActionTypeNames.ToDictionary(kvp => kvp.Value, kvp => kvp.Key, StringComparer.OrdinalIgnoreCase);

	/// <summary>Reads and parses the source page's page-level business rules. Best-effort, never throws.</summary>
	public static PageBusinessRuleProbeResult Probe(
		IToolCommandResolver commandResolver,
		string environment, string uri, string login, string password,
		string pageSchemaName, string packageUId) {
		if (commandResolver is null || string.IsNullOrWhiteSpace(pageSchemaName)) {
			return new PageBusinessRuleProbeResult {
				ProbeOk = false,
				Note = "Page-level business rules were not probed (missing environment client or page schema name)."
			};
		}
		if (string.IsNullOrWhiteSpace(packageUId) || !Guid.TryParse(packageUId, out Guid packageGuid)) {
			return new PageBusinessRuleProbeResult {
				ProbeOk = false,
				Note = "Page-level business rules were not probed (the source page package could not be resolved)."
			};
		}

		try {
			var options = new EnvironmentOptions {
				Environment = environment, Uri = uri, Login = login, Password = password
			};
			IPageBusinessRuleSchemaProvider schemaProvider =
				commandResolver.Resolve<IPageBusinessRuleSchemaProvider>(options);
			IAddonSchemaDesignerClient addonClient =
				commandResolver.Resolve<IAddonSchemaDesignerClient>(options);

			PageBusinessRuleSchemaContext context = schemaProvider.GetSchema(pageSchemaName, packageGuid);
			var request = new AddonGetRequestDto {
				AddonName = BusinessRuleAddonName,
				TargetSchemaUId = Guid.Parse(context.SchemaUId),
				TargetParentSchemaUId = context.ParentSchemaUId,
				TargetPackageUId = packageGuid,
				TargetSchemaManagerName = ClientUnitSchemaManagerName,
				UseFullHierarchy = true
			};

			AddonSchemaDto schema = addonClient.GetSchema(request);
			List<SourcePageBusinessRule> rules = ParseRules(schema);
			return new PageBusinessRuleProbeResult {
				ProbeOk = true,
				Note = rules.Count == 0 ? "No page-level business rules found on the source page." : null,
				Rules = rules
			};
		} catch (Exception ex) {
			return new PageBusinessRuleProbeResult {
				ProbeOk = false,
				Note = $"Could not read page-level business rules ({ex.Message}). Review and recreate them manually."
			};
		}
	}

	/// <summary>Parses the add-on metadata into source rules (one per case). Tolerant of partial/odd data.</summary>
	internal static List<SourcePageBusinessRule> ParseRules(AddonSchemaDto schema) {
		var result = new List<SourcePageBusinessRule>();
		if (schema is null || string.IsNullOrWhiteSpace(schema.MetaData)) {
			return result;
		}

		JsonNode root;
		try {
			root = JsonNode.Parse(schema.MetaData);
		} catch {
			return result;
		}
		if (root is not JsonObject rootObj
			|| rootObj["rules"] is not JsonArray ruleNodes) {
			return result;
		}

		Dictionary<string, string> captionByUId = BuildCaptionResources(schema);

		foreach (JsonNode ruleNode in ruleNodes) {
			if (ruleNode is not JsonObject rule) {
				continue;
			}
			string uId = StringOf(rule["uId"]);
			string caption = StringOf(rule["caption"]);
			if (string.IsNullOrWhiteSpace(caption) && !string.IsNullOrWhiteSpace(uId)) {
				captionByUId.TryGetValue(uId, out caption);
			}
			if (string.IsNullOrWhiteSpace(caption)) {
				caption = StringOf(rule["name"]);
			}

			if (rule["cases"] is not JsonArray cases || cases.Count == 0) {
				continue;
			}

			int caseIndex = 0;
			foreach (JsonNode caseNode in cases) {
				if (caseNode is not JsonObject ruleCase) {
					continue;
				}
				caseIndex++;
				List<SourcePageRuleAction> actions = ParseActions(ruleCase["actions"] as JsonArray);
				if (actions.Count == 0) {
					continue;
				}
				string caseCaption = cases.Count > 1 && !string.IsNullOrWhiteSpace(caption)
					? $"{caption} (case {caseIndex})"
					: caption;
				result.Add(new SourcePageBusinessRule {
					Caption = caseCaption,
					Condition = ConvertCondition(ruleCase["condition"]),
					Actions = actions
				});
			}
		}
		return result;
	}

	private static List<SourcePageRuleAction> ParseActions(JsonArray actionNodes) {
		var actions = new List<SourcePageRuleAction>();
		if (actionNodes is null) {
			return actions;
		}
		foreach (JsonNode actionNode in actionNodes) {
			if (actionNode is not JsonObject action) {
				continue;
			}
			string typeName = StringOf(action["typeName"]);
			if (string.IsNullOrWhiteSpace(typeName)
				|| !ActionShortByTypeName.TryGetValue(typeName, out string shortType)) {
				continue; // not a page-level element action (e.g. set-values) — skip; it never converts.
			}
			actions.Add(new SourcePageRuleAction {
				ActionType = shortType,
				ElementItems = SplitItems(action["items"])
			});
		}
		return actions;
	}

	/// <summary>Reverse-maps a persisted condition group into the create-page-business-rule input shape.</summary>
	private static JsonNode ConvertCondition(JsonNode conditionNode) {
		if (conditionNode is not JsonObject group) {
			return null;
		}

		var conditions = new JsonArray();
		// A group carries "conditions"; a bare single condition carries "leftExpression".
		if (group["conditions"] is JsonArray inner) {
			foreach (JsonNode c in inner) {
				JsonNode converted = ConvertSingleCondition(c);
				if (converted is not null) {
					conditions.Add(converted);
				}
			}
		} else if (group["leftExpression"] is not null) {
			JsonNode converted = ConvertSingleCondition(group);
			if (converted is not null) {
				conditions.Add(converted);
			}
		}

		string logicalOperation = IntOf(group["logicalOperation"]) == LogicalOr ? "OR" : "AND";
		return new JsonObject {
			["logicalOperation"] = logicalOperation,
			["conditions"] = conditions
		};
	}

	private static JsonNode ConvertSingleCondition(JsonNode node) {
		if (node is not JsonObject condition) {
			return null;
		}
		int comparisonValue = IntOf(condition["comparisonType"]);
		if (!ComparisonNameByValue.TryGetValue(comparisonValue, out string comparisonName)) {
			return null; // unknown comparison operator — skip rather than emit an invalid condition.
		}
		var result = new JsonObject {
			["leftExpression"] = ConvertExpression(condition["leftExpression"]),
			["comparisonType"] = comparisonName
		};
		JsonNode right = ConvertExpression(condition["rightExpression"]);
		if (right is not null) {
			result["rightExpression"] = right;
		}
		return result;
	}

	private static JsonNode ConvertExpression(JsonNode expressionNode) {
		if (expressionNode is not JsonObject expression) {
			return null;
		}
		var result = new JsonObject { ["type"] = StringOf(expression["type"]) ?? AttributeValueExpressionType };
		string path = StringOf(expression["path"]);
		if (!string.IsNullOrWhiteSpace(path)) {
			result["path"] = path;
		}
		if (expression["value"] is JsonNode value) {
			result["value"] = value.DeepClone();
		}
		string formula = StringOf(expression["expression"]);
		if (!string.IsNullOrWhiteSpace(formula)) {
			result["expression"] = formula;
		}
		return result;
	}

	/// <summary>Builds ruleUId → caption from add-on resources (keys like "AddonConfig.Rules.{uId}.Caption").</summary>
	private static Dictionary<string, string> BuildCaptionResources(AddonSchemaDto schema) {
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (schema?.Resources is null) {
			return map;
		}
		foreach (AddonResourceDto resource in schema.Resources) {
			if (string.IsNullOrWhiteSpace(resource.Key)
				|| !resource.Key.EndsWith("Caption", StringComparison.OrdinalIgnoreCase)) {
				continue;
			}
			string[] parts = resource.Key.Split('.');
			// Take the segment immediately before the trailing "Caption" — that is the rule UId.
			if (parts.Length < 2) {
				continue;
			}
			string uId = parts[^2];
			string value = resource.Value?.FirstOrDefault()?.Value;
			if (!string.IsNullOrWhiteSpace(uId) && !string.IsNullOrWhiteSpace(value) && !map.ContainsKey(uId)) {
				map[uId] = value;
			}
		}
		return map;
	}

	private static List<string> SplitItems(JsonNode itemsNode) {
		var items = new List<string>();
		switch (itemsNode) {
			case JsonValue:
				string csv = StringOf(itemsNode);
				if (!string.IsNullOrWhiteSpace(csv)) {
					items.AddRange(csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
				}
				break;
			case JsonArray array:
				foreach (JsonNode element in array) {
					string value = StringOf(element);
					if (!string.IsNullOrWhiteSpace(value)) {
						items.Add(value.Trim());
					}
				}
				break;
		}
		return items;
	}

	private static string StringOf(JsonNode node) =>
		node is JsonValue value && value.TryGetValue(out string s) ? s : node?.ToString();

	private static int IntOf(JsonNode node) {
		if (node is JsonValue value) {
			if (value.TryGetValue(out int i)) {
				return i;
			}
			if (int.TryParse(value.ToString(), out int parsed)) {
				return parsed;
			}
		}
		return int.MinValue;
	}
}
