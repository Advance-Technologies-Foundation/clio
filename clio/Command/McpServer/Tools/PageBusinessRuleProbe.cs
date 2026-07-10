using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
[SuppressMessage("Critical Code Smell", "S3776:Cognitive Complexity of methods should not be too high", Justification = "ParseRules is a tolerant metadata parser; its branchy shape mirrors the persisted business-rule structure and splitting it would obscure that mapping.")]
[SuppressMessage("Minor Code Smell", "S1192:String literals should not be duplicated", Justification = "Persisted business-rule JSON keys (conditions/leftExpression/comparisonType) read more clearly inline in this parser.")]
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
				JsonNode condition = ConvertCondition(ruleCase["condition"], out PageRuleConditionIssue conditionIssue);
				result.Add(new SourcePageBusinessRule {
					Caption = caseCaption,
					Condition = condition,
					ConditionIssue = conditionIssue,
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
			// Page-level business rules carry ONLY element actions (hide/show + make-editable/read-only/
			// required/optional): the probe reads strictly the page's ClientUnitSchema BusinessRule add-on,
			// while object-level rules live on the EntitySchema (a different schema manager) and never reach
			// here. So every action type maps; there is no need to filter by action type. An unmapped typeName
			// is a data anomaly that surfaces loudly (the probe degrades to ProbeOk=false) rather than being
			// silently dropped.
			string typeName = StringOf(action["typeName"]);
			actions.Add(new SourcePageRuleAction {
				ActionType = ActionShortByTypeName[typeName],
				ElementItems = SplitItems(action["items"])
			});
		}
		return actions;
	}

	/// <summary>
	/// Reverse-maps a persisted condition group into the create-page-business-rule input shape, which supports
	/// only a single flat <c>conditions</c> array under ONE logical operator. Creatio persists UI-authored rules
	/// wrapped in nested groups (even a single condition is wrapped), so leaf conditions are collected recursively
	/// and FLATTENED — this is lossless as long as the tree uses a single logical operator. When the tree mixes
	/// AND and OR across groups that each carry ≥2 real operands (e.g. <c>A AND (B OR C)</c>), flattening would
	/// change when the rule fires; that cannot be represented in the flat input shape, so <paramref name="convertible"/>
	/// reports the reason via <paramref name="issue"/> and the caller drops the rule for manual recreation instead
	/// of emitting wrong semantics.
	/// </summary>
	private static JsonNode ConvertCondition(JsonNode conditionNode, out PageRuleConditionIssue issue) {
		issue = PageRuleConditionIssue.None;
		if (conditionNode is not JsonObject group) {
			return null;
		}

		// A flat single-operator representation is faithful iff every group with ≥2 leaf-bearing operands shares
		// one operator. Wrapper groups with 0/1 operand are pass-through — their operator is irrelevant. Two or
		// more distinct "active" operators means genuinely mixed AND/OR that cannot be flattened losslessly.
		var activeIsOr = new HashSet<bool>();
		CollectActiveGroupOperators(group, activeIsOr);
		if (activeIsOr.Count > 1) {
			issue = PageRuleConditionIssue.MixedAndOr;
			return null;
		}

		// A PRESENT comparison operator that maps to nothing supported would otherwise be silently rewritten to
		// the "is-not-filled-in" default (which is only correct for a genuinely ABSENT comparison) — a
		// plausible-but-wrong rule. Drop the rule instead.
		if (HasUnrecognizedComparison(group)) {
			issue = PageRuleConditionIssue.UnrecognizedComparison;
			return null;
		}

		var conditions = new JsonArray();
		CollectLeafConditions(group, conditions);

		// Emit the single active operator when present (it governs all leaves); otherwise (0 or 1 leaf, all
		// operators vacuous) fall back to the outer group's operator.
		bool isOr = activeIsOr.Count == 1 ? activeIsOr.First() : IntOf(group["logicalOperation"]) == LogicalOr;
		return new JsonObject {
			["logicalOperation"] = isOr ? "OR" : "AND",
			["conditions"] = conditions
		};
	}

	/// <summary>
	/// Collects the logical operator (true = OR, false = AND) of every group that has ≥2 leaf-bearing operands —
	/// i.e. every group whose operator actually governs more than one operand. Groups that contribute 0 or 1 leaf
	/// are wrappers whose operator is vacuous and are ignored. If the resulting set has more than one entry, the
	/// condition mixes AND and OR and cannot be flattened without changing its meaning.
	/// </summary>
	private static void CollectActiveGroupOperators(JsonObject group, HashSet<bool> activeIsOr) {
		if (group["conditions"] is not JsonArray inner) {
			return;
		}
		int operands = 0;
		foreach (JsonNode child in inner) {
			if (child is not JsonObject childObj) {
				continue;
			}
			if (CountLeaves(childObj) > 0) {
				operands++;
			}
			CollectActiveGroupOperators(childObj, activeIsOr); // no-op for leaf nodes (no "conditions" array)
		}
		if (operands >= 2) {
			activeIsOr.Add(IntOf(group["logicalOperation"]) == LogicalOr);
		}
	}

	/// <summary>Counts leaf conditions (nodes carrying leftExpression/comparisonType) under a node, recursing into groups.</summary>
	private static int CountLeaves(JsonObject node) {
		if (node["conditions"] is JsonArray inner) {
			int sum = 0;
			foreach (JsonNode child in inner) {
				if (child is JsonObject childObj) {
					sum += CountLeaves(childObj);
				}
			}
			return sum;
		}
		return node["leftExpression"] is null && node["comparisonType"] is null ? 0 : 1;
	}

	/// <summary>
	/// True when any leaf in the (possibly nested) group carries a PRESENT comparison operator that
	/// <see cref="ResolveComparisonName"/> cannot map (numeric outside the supported set, or an unmapped name).
	/// A genuinely absent comparison is fine (it becomes the Creatio default "is-not-filled-in").
	/// </summary>
	private static bool HasUnrecognizedComparison(JsonObject node) {
		if (node["conditions"] is JsonArray inner) {
			foreach (JsonNode child in inner) {
				if (child is JsonObject childObj && HasUnrecognizedComparison(childObj)) {
					return true;
				}
			}
			return false;
		}
		bool isLeaf = node["leftExpression"] is not null || node["comparisonType"] is not null;
		return isLeaf && ResolveComparisonName(node["comparisonType"]) is null;
	}

	/// <summary>
	/// Recursively lifts every leaf condition out of a (possibly nested) condition group into
	/// <paramref name="conditions"/>. A node with a <c>conditions</c> array is a group — recurse into each
	/// child; otherwise it is a bare leaf (carries <c>leftExpression</c>/<c>comparisonType</c>) — convert and
	/// add it. This guarantees conditions always convert instead of being silently emptied by nesting.
	/// </summary>
	private static void CollectLeafConditions(JsonObject node, JsonArray conditions) {
		if (node["conditions"] is JsonArray inner) {
			foreach (JsonNode child in inner) {
				if (child is JsonObject childObj) {
					CollectLeafConditions(childObj, conditions);
				}
			}
			return;
		}
		if (node["leftExpression"] is null && node["comparisonType"] is null) {
			return; // not a leaf condition (e.g. an empty/degenerate group node) — nothing to lift.
		}
		JsonNode converted = ConvertSingleCondition(node);
		if (converted is not null) {
			conditions.Add(converted);
		}
	}

	private static JsonNode ConvertSingleCondition(JsonNode node) {
		if (node is not JsonObject condition) {
			return null;
		}
		var result = new JsonObject {
			["leftExpression"] = ConvertExpression(condition["leftExpression"]),
			["comparisonType"] = ResolveComparisonName(condition["comparisonType"])
		};
		JsonNode right = ConvertExpression(condition["rightExpression"]);
		if (right is not null) {
			result["rightExpression"] = right;
		}
		return result;
	}

	/// <summary>
	/// Resolves the create-page-business-rule comparison name from a persisted condition. Creatio omits the
	/// comparison for a bare "is (not) filled in" check, so a genuinely ABSENT value defaults to
	/// <c>is-not-filled-in</c> (Creatio's default, enum 0). A PRESENT but unrecognized value (a numeric outside
	/// the supported set, or an unmapped name) returns <c>null</c> — the caller drops the rule rather than
	/// silently rewriting the comparison. Accepts the numeric enum value, an already-kebab supported name, or a
	/// PascalCase enum name (e.g. <c>IsFilledIn</c>).
	/// </summary>
	private static string ResolveComparisonName(JsonNode comparisonNode) {
		const string defaultComparison = "is-not-filled-in";
		// Genuinely absent -> Creatio's default (bare "is (not) filled in").
		if (comparisonNode is not JsonValue value) {
			return defaultComparison;
		}
		// A numeric value is definitive: mapped -> name; present but unknown -> null (do not fall through).
		if (value.TryGetValue(out int numeric)) {
			return ComparisonNameByValue.TryGetValue(numeric, out string byValue) ? byValue : null;
		}
		string text = value.ToString();
		if (string.IsNullOrWhiteSpace(text)) {
			return defaultComparison;
		}
		if (int.TryParse(text, out int parsed)) {
			return ComparisonNameByValue.TryGetValue(parsed, out string byParsed) ? byParsed : null;
		}
		if (SupportedComparisonTypeValues.ContainsKey(text)) {
			return text; // already a supported kebab name (e.g. "is-filled-in").
		}
		// Present PascalCase name: mapped -> kebab; otherwise unrecognized -> null.
		return PascalEnumToKebab.TryGetValue(text, out string mapped) ? mapped : null;
	}

	// Creatio BusinessRuleComparisonType PascalCase enum names -> create-page-business-rule kebab names.
	private static readonly IReadOnlyDictionary<string, string> PascalEnumToKebab =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
			["IsNotFilledIn"] = "is-not-filled-in",
			["IsFilledIn"] = "is-filled-in",
			["Equal"] = "equal",
			["NotEqual"] = "not-equal",
			["Less"] = "less-than",
			["LessOrEqual"] = "less-than-or-equal",
			["Greater"] = "greater-than",
			["GreaterOrEqual"] = "greater-than-or-equal",
			["Contain"] = "contain",
			["NotContain"] = "not-contain"
		};

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
