using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Command.AddonSchemaDesigner;
using static Clio.Command.BusinessRules.BusinessRuleConstants;

namespace Clio.Command.BusinessRules;

internal abstract class BaseBusinessRuleService(
	IBusinessRulePackageResolver packageResolver,
	IBusinessRuleAddonService addonService) {

	protected IBusinessRulePackageResolver PackageResolver { get; } = packageResolver;

	protected IBusinessRuleAddonService AddonService { get; } = addonService;

	protected IReadOnlyList<BusinessRuleBatchItemResult> CreateBatch(
		AddonGetRequestDto addonRequest,
		IReadOnlyList<BusinessRule> rules,
		Func<BusinessRule, IReadOnlyList<BusinessRuleMetadataDto>> convert) {
		var results = new BusinessRuleBatchItemResult[rules.Count];
		var pending = new List<(int Index, string Caption, string RuleName)>();
		var toAppend = new List<BusinessRuleMetadataDto>();

		for (int index = 0; index < rules.Count; index++) {
			BusinessRule rule = rules[index];
			string caption = rule?.Caption ?? string.Empty;
			try {
				ArgumentNullException.ThrowIfNull(rule);
				IReadOnlyList<BusinessRuleMetadataDto> createdRules = convert(rule);
				if (createdRules.Count == 0) {
					results[index] = new BusinessRuleBatchItemResult(caption, false, null, "Rule produced no metadata.");
					continue;
				}

				pending.Add((index, caption, createdRules[0].Name));
				toAppend.AddRange(createdRules);
			} catch (Exception exception) {
				results[index] = new BusinessRuleBatchItemResult(caption, false, null, exception.Message);
			}
		}

		if (toAppend.Count > 0) {
			BusinessRuleBatchSave.StampOutcome(results, pending, () => AddonService.AppendRules(addonRequest, toAppend));
		}

		return results;
	}

	protected IReadOnlyList<BusinessRuleBatchItemResult> UpdateBatch(
		AddonGetRequestDto addonRequest,
		IReadOnlyList<BusinessRule> rules,
		Func<BusinessRule, JsonObject, IReadOnlyList<BusinessRuleMetadataDto>> convert) {
		ArgumentNullException.ThrowIfNull(addonRequest);
		ArgumentNullException.ThrowIfNull(rules);
		ArgumentNullException.ThrowIfNull(convert);

		AddonSchemaDto schema = AddonService.GetSchema(addonRequest);
		JsonObject metadata = BusinessRuleAddonMetadata.ParseMetadata(schema.MetaData);
		JsonArray metadataRules = BusinessRuleAddonMetadata.GetOrCreateRules(metadata);
		List<AddonResourceDto> resources = BusinessRuleAddonMetadata.NormalizeResourceKeys(schema.Resources.ToList());

		var results = new BusinessRuleBatchItemResult[rules.Count];
		var pending = new List<(int Index, string Name)>();
		HashSet<string> batchNames = new(StringComparer.OrdinalIgnoreCase);

		for (int index = 0; index < rules.Count; index++) {
			BusinessRule rule = rules[index];
			string identifier = string.IsNullOrWhiteSpace(rule?.Name)
				? rule?.Caption ?? string.Empty
				: rule.Name.Trim();
			try {
				ArgumentNullException.ThrowIfNull(rule);
				if (string.IsNullOrWhiteSpace(rule.Name)) {
					throw new ArgumentException("name is required to update a business rule.");
				}

				string name = rule.Name.Trim();
				if (!batchNames.Add(name)) {
					throw new InvalidOperationException(
						$"Business rule '{name}' appears more than once in the update batch.");
				}

				int ruleIndex = BusinessRuleAddonMetadata.FindSingleRuleIndexByName(metadataRules, name);
				if (ruleIndex < 0) {
					throw new InvalidOperationException($"Business rule '{name}' was not found.");
				}

				JsonObject existing = (JsonObject)metadataRules[ruleIndex]!;
				bool effectiveEnabled = rule.Enabled ?? BusinessRuleAddonMetadata.GetBool(existing, "enabled", defaultValue: true);
				BusinessRule effectiveRule = rule with { Enabled = effectiveEnabled };

				IReadOnlyList<BusinessRuleMetadataDto> generated = convert(effectiveRule, existing);
				BusinessRuleMetadataDto parent = generated[0];
				BusinessRuleAddonMetadata.RemoveChildRules(metadataRules, resources, parent.UId);
				ruleIndex = BusinessRuleAddonMetadata.FindSingleRuleIndexByName(metadataRules, name);
				metadataRules[ruleIndex] = BusinessRuleAddonMetadata.SerializeCreatedRule(parent);
				foreach (BusinessRuleMetadataDto child in generated.Skip(1)) {
					metadataRules.Add(BusinessRuleAddonMetadata.SerializeCreatedRule(child));
				}

				foreach (BusinessRuleMetadataDto generatedRule in generated
					.Where(generatedRule => !string.IsNullOrWhiteSpace(generatedRule.Caption))) {
					BusinessRuleAddonMetadata.UpsertCaptionResource(resources, generatedRule.UId, generatedRule.Caption.Trim());
				}

				pending.Add((index, name));
			} catch (Exception exception) {
				results[index] = new BusinessRuleBatchItemResult(identifier, false, null, exception.Message);
			}
		}

		if (pending.Count == 0) {
			return results;
		}

		schema.MetaData = metadata.ToJsonString(JsonOptions);
		schema.Resources = resources;
		try {
			AddonService.SaveSchema(schema);
			foreach ((int index, string name) in pending) {
				results[index] = new BusinessRuleBatchItemResult(name, true, name, null);
			}
		} catch (Exception exception) {
			foreach ((int index, string name) in pending) {
				results[index] = new BusinessRuleBatchItemResult(name, false, null, exception.Message);
			}
		}

		return results;
	}

	protected IReadOnlyList<BusinessRule> ReadCore(AddonGetRequestDto addonRequest) =>
		AddonService.ReadRules(addonRequest);

	protected IReadOnlyList<BusinessRuleBatchItemResult> DeleteCore(
		AddonGetRequestDto addonRequest,
		IReadOnlyList<string> ruleNames) {
		if (ruleNames is null || ruleNames.Count == 0) {
			throw new ArgumentException("rule-names is required and must contain at least one rule name.");
		}

		return AddonService.DeleteRules(addonRequest, ruleNames);
	}

	protected static void RequireSchemaFields(string packageName, string schemaName, string schemaFieldName) {
		if (string.IsNullOrWhiteSpace(packageName)) {
			throw new ArgumentException("package-name is required.");
		}

		if (string.IsNullOrWhiteSpace(schemaName)) {
			throw new ArgumentException($"{schemaFieldName} is required.");
		}
	}
}
