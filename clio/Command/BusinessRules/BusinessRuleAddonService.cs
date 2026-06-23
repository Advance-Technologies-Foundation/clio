using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.EntitySchemaDesigner;
using static Clio.Command.BusinessRules.BusinessRuleConstants;

namespace Clio.Command.BusinessRules;

internal interface IBusinessRuleAddonService {
	BusinessRuleCreateResult AppendRule(
		AddonGetRequestDto request,
		BusinessRule rule,
		IReadOnlyList<BusinessRuleMetadataDto> createdRules);

	/// <summary>
	/// Appends the combined metadata of multiple business rules to the target add-on schema in a
	/// single round-trip: one <c>GetSchema</c>, one <c>SaveSchema</c>, one <c>ResetClientScriptCache</c>,
	/// and one <c>BuildConfiguration</c> for the whole batch. This is the batch counterpart of
	/// <see cref="AppendRule"/>, which makes those four remote calls per rule.
	/// </summary>
	/// <param name="request">Identifies the target add-on schema to fetch and save once.</param>
	/// <param name="createdRules">Flattened metadata for every rule in the batch, appended in order.</param>
	/// <returns>The generated name of the first appended rule.</returns>
	BusinessRuleCreateResult AppendRules(
		AddonGetRequestDto request,
		IReadOnlyList<BusinessRuleMetadataDto> createdRules);
}

internal sealed class BusinessRuleAddonService(
	IAddonSchemaDesignerClient addonSchemaDesignerClient)
	: IBusinessRuleAddonService {

	public BusinessRuleCreateResult AppendRule(
		AddonGetRequestDto request,
		BusinessRule rule,
		IReadOnlyList<BusinessRuleMetadataDto> createdRules) =>
		AppendRules(request, createdRules);

	public BusinessRuleCreateResult AppendRules(
		AddonGetRequestDto request,
		IReadOnlyList<BusinessRuleMetadataDto> createdRules) {
		ArgumentNullException.ThrowIfNull(createdRules);
		if (createdRules.Count == 0) {
			throw new ArgumentException("At least one generated business rule is required.", nameof(createdRules));
		}

		AddonSchemaDto schema = addonSchemaDesignerClient.GetSchema(request);
		JsonObject metadata = ParseMetadata(schema.MetaData);
		JsonArray rules = GetOrCreateRules(metadata);
		List<AddonResourceDto> resources = NormalizeResourceKeys(schema.Resources.ToList());

		foreach (BusinessRuleMetadataDto createdRule in createdRules) {
			rules.Add(SerializeCreatedRule(createdRule));
			if (!string.IsNullOrWhiteSpace(createdRule.Caption)) {
				UpsertCaptionResource(resources, createdRule.UId, createdRule.Caption.Trim());
			}
		}

		schema.MetaData = metadata.ToJsonString(JsonOptions);
		schema.Resources = resources;

		addonSchemaDesignerClient.SaveSchema(schema);
		// Clears the server-side RequireJS module cache so the saved addon schema
		// is immediately visible to the current user without a full page reload.
		addonSchemaDesignerClient.ResetClientScriptCache();
		// Rebuilds static client content for changed schemas, broadcasts a
		// ConfigurationStructureChanged event to online users so their
		// crt-business-rules-cache is cleared immediately, and writes a new
		// ConfigurationHash to disk so offline users get cache invalidation on
		// their next startup via the /api/ClientCache/Hashes hash comparison.
		addonSchemaDesignerClient.BuildConfiguration();

		return new BusinessRuleCreateResult(createdRules[0].Name);
	}

	private static JsonObject ParseMetadata(string? metaData) {
		if (string.IsNullOrWhiteSpace(metaData)) {
			return CreateEmptyMetadata();
		}

		try {
			return JsonNode.Parse(metaData) as JsonObject
				?? throw new InvalidOperationException("Business-rule add-on metadata root must be a JSON object.");
		} catch (JsonException exception) {
			throw new InvalidOperationException("Business-rule add-on metadata is not valid JSON.", exception);
		}
	}

	private static JsonObject CreateEmptyMetadata() =>
		new() {
			["typeName"] = BusinessRulesMetadataTypeName,
			["rules"] = new JsonArray()
		};

	private static JsonArray GetOrCreateRules(JsonObject metadata) {
		if (!metadata.TryGetPropertyValue("rules", out JsonNode? rulesNode) || rulesNode is null) {
			JsonArray createdRules = [];
			metadata["rules"] = createdRules;
			return createdRules;
		}

		return rulesNode as JsonArray
			?? throw new InvalidOperationException("Business-rule add-on metadata 'rules' property must be a JSON array.");
	}

	private static JsonNode SerializeCreatedRule(BusinessRuleMetadataDto createdRule) =>
		JsonSerializer.SerializeToNode(createdRule, JsonOptions)
		?? throw new InvalidOperationException("Generated business-rule metadata could not be serialized.");

	/// <summary>
	/// Normalizes resource keys received from GetSchema to the format expected by SaveSchema.
	/// The server returns 4-part keys like <c>AddonConfig.Rules.{guid}.Caption</c>;
	/// this method extracts parts [2] and [3] to produce <c>{guid}.Caption</c>.
	/// Mirrors the frontend logic in <c>AddonInfo._setAddonResources</c>
	/// (libs/studio-enterprise/util/schema-designer-utils/src/lib/models/addon-info.ts).
	/// </summary>
	private static List<AddonResourceDto> NormalizeResourceKeys(List<AddonResourceDto> resources) {
		for (int i = 0; i < resources.Count; i++) {
			string[] parts = resources[i].Key.Split('.');
			if (parts.Length == 4
				&& string.Equals(parts[0], "AddonConfig", StringComparison.Ordinal)
				&& string.Equals(parts[1], "Rules", StringComparison.Ordinal)) {
				resources[i].Key = $"{parts[2]}.{parts[3]}";
			}
		}
		return resources;
	}

	private static void UpsertCaptionResource(List<AddonResourceDto> resources, string ruleUId, string caption) {
		string key = $"{ruleUId}.Caption";
		AddonResourceDto? existing = resources.FirstOrDefault(resource =>
			string.Equals(resource.Key, key, StringComparison.OrdinalIgnoreCase));
		AddonResourceValueDto enUsValue = new() {
			Key = EntitySchemaDesignerSupport.DefaultCultureName,
			Value = caption
		};
		if (existing is null) {
			resources.Add(new AddonResourceDto {
				Key = key,
				Value = [enUsValue]
			});
			return;
		}

		existing.Value = [enUsValue];
	}
}
