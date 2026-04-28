using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Command.AddonSchemaDesigner;
using Clio.Command.EntitySchemaDesigner;
using Clio.Package;
using static Clio.Command.BusinessRules.BusinessRuleConstants;

namespace Clio.Command.BusinessRules;

/// <summary>
/// Creates entity business rules by appending add-on metadata to the target entity schema.
/// </summary>
public interface IBusinessRuleService {
	/// <summary>
	/// Creates a new business rule in the target package and entity schema.
	/// </summary>
	/// <param name="request">Business-rule creation input.</param>
	/// <returns>Generated metadata about the created rule.</returns>
	BusinessRuleCreateResult Create(BusinessRuleCreateRequest request);
}

/// <summary>
/// Describes the package, entity schema, and business-rule definition to create.
/// </summary>
public sealed record BusinessRuleCreateRequest(
	string PackageName,
	string EntitySchemaName,
	BusinessRule Rule
);

/// <summary>
/// Returns the generated internal rule name created in add-on metadata.
/// </summary>
public sealed record BusinessRuleCreateResult(string RuleName);

internal sealed class BusinessRuleService(
	IAddonSchemaDesignerClient addonSchemaDesignerClient,
	IRemoteEntitySchemaDesignerClient entitySchemaDesignerClient,
	IApplicationPackageListProvider applicationPackageListProvider)
	: IBusinessRuleService {
	
	public BusinessRuleCreateResult Create(BusinessRuleCreateRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		ValidateCreateRequest(request);

		Guid packageUId = applicationPackageListProvider.GetPackages()
			.FirstOrDefault(p => string.Equals(p.Descriptor.Name, request.PackageName.Trim(), StringComparison.OrdinalIgnoreCase))
			?.Descriptor.UId
			?? throw new InvalidOperationException($"Package '{request.PackageName}' was not found.");
		EntityDesignSchemaDto entitySchema = LoadEntitySchema(request.EntitySchemaName, packageUId);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = BusinessRuleHelpers.BuildColumnMap(entitySchema);
		BusinessRuleValidator.Validate(request.Rule, columnMap);
		AddonSchemaDto schema = addonSchemaDesignerClient.GetSchema(BuildAddonSchemaRequest(entitySchema, packageUId));
		JsonObject metadata = ParseMetadata(schema.MetaData);
		JsonArray rules = GetOrCreateRules(metadata);
		List<AddonResourceDto> resources = NormalizeResourceKeys(schema.Resources.ToList());

		BusinessRuleMetadataDto createdRule = BusinessRuleMetadataConverter.ToMetadata(columnMap, request.Rule);
		rules.Add(SerializeCreatedRule(createdRule));
		UpsertCaptionResource(resources, createdRule.UId, request.Rule.Caption.Trim());

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

		return new BusinessRuleCreateResult(createdRule.Name);
	}

	private static void ValidateCreateRequest(BusinessRuleCreateRequest request) {
		if (string.IsNullOrWhiteSpace(request.PackageName)) {
			throw new ArgumentException("package-name is required.");
		}

		if (string.IsNullOrWhiteSpace(request.EntitySchemaName)) {
			throw new ArgumentException("entity-schema-name is required.");
		}

		if (request.Rule is null) {
			throw new ArgumentException("rule is required.");
		}
	}

	private EntityDesignSchemaDto LoadEntitySchema(
		string entitySchemaName,
		Guid packageUId) {
		Clio.Command.EntitySchemaDesigner.DesignerResponse<EntityDesignSchemaDto> response = entitySchemaDesignerClient.GetSchemaDesignItem(
			new GetSchemaDesignItemRequestDto {
				Name = entitySchemaName.Trim(),
				PackageUId = packageUId,
				UseFullHierarchy = true,
				Cultures = [EntitySchemaDesignerSupport.DefaultCultureName]
			},
			new RemoteCommandOptions());
		return response.Schema
			?? throw new InvalidOperationException($"Entity schema '{entitySchemaName}' was not returned.");
	}

	private static AddonGetRequestDto BuildAddonSchemaRequest(
		EntityDesignSchemaDto entitySchema,
		Guid packageUId) {
		return new AddonGetRequestDto {
			AddonName = BusinessRuleAddonName,
			TargetSchemaUId = entitySchema.UId,
			TargetParentSchemaUId = entitySchema.ParentSchema?.UId ?? Guid.Empty,
			TargetPackageUId = packageUId,
			TargetSchemaManagerName = EntitySchemaManagerName,
			UseFullHierarchy = true
		};
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
			if (parts.Length == 4) {
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
