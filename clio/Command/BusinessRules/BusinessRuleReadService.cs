using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.EntitySchemaDesigner;
using static Clio.Command.BusinessRules.BusinessRuleConstants;

namespace Clio.Command.BusinessRules;

/// <summary>
/// Reads existing entity and page business rules from Creatio without mutating metadata.
/// </summary>
public interface IBusinessRuleReadService {
	/// <summary>
	/// Lists business rules in one entity or page scope.
	/// </summary>
	/// <param name="request">Read request.</param>
	/// <returns>Normalized read response.</returns>
	BusinessRuleListResponse List(BusinessRuleReadRequest request);

	/// <summary>
	/// Gets one business rule from one entity or page scope.
	/// </summary>
	/// <param name="request">Get request with a rule name selector.</param>
	/// <returns>Normalized get response.</returns>
	BusinessRuleGetResponse Get(BusinessRuleGetRequest request);
}

internal sealed class BusinessRuleReadService(
	IBusinessRulePackageResolver packageResolver,
	IEntityBusinessRuleSchemaProvider entitySchemaProvider,
	IPageBusinessRuleSchemaProvider pageSchemaProvider,
	IAddonSchemaDesignerClient addonSchemaDesignerClient)
	: IBusinessRuleReadService {

	public BusinessRuleListResponse List(BusinessRuleReadRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		string normalizedScopeType = NormalizeScopeType(request.ScopeType);
		ValidateRequest(request.PackageName, request.SchemaName);

		try {
			AddonSchemaDto schema = ReadAddonSchema(request.PackageName, normalizedScopeType, request.SchemaName);
			IReadOnlyList<JsonObject> rules = ReadRules(schema.MetaData);
			IReadOnlyDictionary<string, string> captions = BuildCaptionMap(schema.Resources);
			IReadOnlyList<BusinessRuleReadSummary> summaries = rules
				.Select(rule => BusinessRuleMetadataReadConverter.ToSummary(rule, ReadCaption(rule, captions)))
				.ToList();
			return new BusinessRuleListResponse {
				Success = true,
				ScopeType = normalizedScopeType,
				SchemaName = request.SchemaName,
				Count = summaries.Count,
				Rules = summaries
			};
		} catch (Exception ex) {
			return CreateListError(normalizedScopeType, request.SchemaName, ex.Message);
		}
	}

	public BusinessRuleGetResponse Get(BusinessRuleGetRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		string normalizedScopeType = NormalizeScopeType(request.ScopeType);
		ValidateRequest(request.PackageName, request.SchemaName);
		if (string.IsNullOrWhiteSpace(request.RuleName)) {
			return CreateGetError(normalizedScopeType, request.SchemaName, "ruleName is required.");
		}

		try {
			AddonSchemaDto schema = ReadAddonSchema(request.PackageName, normalizedScopeType, request.SchemaName);
			IReadOnlyList<JsonObject> rules = ReadRules(schema.MetaData);
			IReadOnlyDictionary<string, string> captions = BuildCaptionMap(schema.Resources);
			JsonObject? match = rules.SingleOrDefault(rule =>
				string.Equals(ReadString(rule["name"]), request.RuleName.Trim(), StringComparison.OrdinalIgnoreCase));
			if (match is null) {
				return CreateGetError(
					normalizedScopeType,
					request.SchemaName,
					$"Business rule with ruleName '{request.RuleName}' was not found for {normalizedScopeType} schema '{request.SchemaName}'.");
			}

			return new BusinessRuleGetResponse {
				Success = true,
				ScopeType = normalizedScopeType,
				SchemaName = request.SchemaName,
				Rule = BusinessRuleMetadataReadConverter.FromMetadata(
					match,
					normalizedScopeType,
					ReadCaption(match, captions),
					strict: true)
			};
		} catch (Exception ex) {
			return CreateGetError(normalizedScopeType, request.SchemaName, ex.Message);
		}
	}

	private AddonSchemaDto ReadAddonSchema(
		string packageName,
		string scopeType,
		string schemaName) {
		Guid packageUId = packageResolver.ResolveUId(packageName);
		AddonGetRequestDto request = string.Equals(scopeType, BusinessRuleScopeTypes.Entity, StringComparison.Ordinal)
			? BuildEntityAddonRequest(schemaName, packageUId)
			: BuildPageAddonRequest(schemaName, packageUId);
		return addonSchemaDesignerClient.GetSchema(request);
	}

	private AddonGetRequestDto BuildEntityAddonRequest(string schemaName, Guid packageUId) {
		EntityDesignSchemaDto entitySchema = entitySchemaProvider.GetSchema(schemaName, packageUId);
		return new AddonGetRequestDto {
			AddonName = BusinessRuleAddonName,
			TargetSchemaUId = entitySchema.UId,
			TargetParentSchemaUId = entitySchema.ParentSchema?.UId ?? Guid.Empty,
			TargetPackageUId = packageUId,
			TargetSchemaManagerName = EntitySchemaManagerName,
			UseFullHierarchy = true
		};
	}

	private AddonGetRequestDto BuildPageAddonRequest(string schemaName, Guid packageUId) {
		PageBusinessRuleSchemaIdentity pageContext = pageSchemaProvider.GetSchemaIdentity(schemaName, packageUId);
		return new AddonGetRequestDto {
			AddonName = BusinessRuleAddonName,
			TargetSchemaUId = Guid.Parse(pageContext.SchemaUId),
			TargetParentSchemaUId = pageContext.ParentSchemaUId,
			TargetPackageUId = packageUId,
			TargetSchemaManagerName = ClientUnitSchemaManagerName,
			UseFullHierarchy = true
		};
	}

	private static IReadOnlyList<JsonObject> ReadRules(string? metaData) {
		if (string.IsNullOrWhiteSpace(metaData)) {
			return [];
		}
		JsonNode? metadataNode = JsonNode.Parse(metaData);
		if (metadataNode is not JsonObject metadata) {
			throw new InvalidOperationException("Business-rule add-on metadata root must be a JSON object.");
		}
		if (!metadata.TryGetPropertyValue("rules", out JsonNode? rulesNode) || rulesNode is null) {
			return [];
		}
		if (rulesNode is not JsonArray rulesArray) {
			throw new InvalidOperationException("Business-rule add-on metadata 'rules' property must be a JSON array.");
		}
		return rulesArray.OfType<JsonObject>().ToList();
	}

	private static IReadOnlyDictionary<string, string> BuildCaptionMap(IEnumerable<AddonResourceDto> resources) {
		Dictionary<string, string> captions = new(StringComparer.OrdinalIgnoreCase);
		foreach (AddonResourceDto resource in resources) {
			string? ruleUId = ExtractRuleUIdFromCaptionResource(resource.Key);
			if (string.IsNullOrWhiteSpace(ruleUId)) {
				continue;
			}
			string? caption = resource.Value
				.FirstOrDefault(value => string.Equals(value.Key, EntitySchemaDesignerSupport.DefaultCultureName, StringComparison.OrdinalIgnoreCase))
				?.Value
				?? resource.Value.FirstOrDefault()?.Value;
			if (!string.IsNullOrWhiteSpace(caption)) {
				captions[ruleUId] = caption;
			}
		}
		return captions;
	}

	private static string? ExtractRuleUIdFromCaptionResource(string key) {
		string[] parts = key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length == 2 && string.Equals(parts[1], "Caption", StringComparison.OrdinalIgnoreCase)) {
			return parts[0];
		}
		if (parts.Length == 4
			&& string.Equals(parts[0], "AddonConfig", StringComparison.Ordinal)
			&& string.Equals(parts[1], "Rules", StringComparison.Ordinal)
			&& string.Equals(parts[3], "Caption", StringComparison.OrdinalIgnoreCase)) {
			return parts[2];
		}
		return null;
	}

	private static string? ReadCaption(JsonObject rule, IReadOnlyDictionary<string, string> captions) {
		string? ruleUId = ReadString(rule["uId"]);
		return !string.IsNullOrWhiteSpace(ruleUId) && captions.TryGetValue(ruleUId, out string? caption)
			? caption
			: null;
	}

	private static string? ReadString(JsonNode? node) =>
		node is JsonValue && node.GetValueKind() == JsonValueKind.String
			? node.GetValue<string>()
			: null;

	private static string NormalizeScopeType(string scopeType) {
		if (string.Equals(scopeType, BusinessRuleScopeTypes.Entity, StringComparison.OrdinalIgnoreCase)) {
			return BusinessRuleScopeTypes.Entity;
		}
		if (string.Equals(scopeType, BusinessRuleScopeTypes.Page, StringComparison.OrdinalIgnoreCase)) {
			return BusinessRuleScopeTypes.Page;
		}
		throw new ArgumentException("scopeType must be either 'entity' or 'page'.");
	}

	private static void ValidateRequest(string packageName, string schemaName) {
		if (string.IsNullOrWhiteSpace(packageName)) {
			throw new ArgumentException("packageName is required.");
		}
		if (string.IsNullOrWhiteSpace(schemaName)) {
			throw new ArgumentException("schemaName is required.");
		}
	}

	private static BusinessRuleListResponse CreateListError(
		string scopeType,
		string schemaName,
		string error) =>
		new() {
			Success = false,
			ScopeType = scopeType,
			SchemaName = schemaName,
			Error = error
		};

	private static BusinessRuleGetResponse CreateGetError(
		string scopeType,
		string schemaName,
		string error) =>
		new() {
			Success = false,
			ScopeType = scopeType,
			SchemaName = schemaName,
			Error = error
		};
}
