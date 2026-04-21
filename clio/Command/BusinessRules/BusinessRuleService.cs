using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Package;
using static Clio.Command.BusinessRules.BusinessRuleConstants;

namespace Clio.Command.BusinessRules;

public interface IBusinessRuleService {
	BusinessRuleCreateResult Create(BusinessRuleCreateRequest request);
}

public sealed record BusinessRuleCreateRequest(
	string PackageName,
	string EntitySchemaName,
	BusinessRule Rule
);

public sealed record BusinessRuleCreateResult(string RuleName);

internal sealed class BusinessRuleService(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IRemoteEntitySchemaDesignerClient entitySchemaDesignerClient,
	IApplicationPackageListProvider applicationPackageListProvider,
	IJsonConverter jsonConverter)
	: IBusinessRuleService {
	
	public BusinessRuleCreateResult Create(BusinessRuleCreateRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		BusinessRuleValidator.ValidateRequest(request);

		Guid packageUId = applicationPackageListProvider.GetPackages()
			.FirstOrDefault(p => string.Equals(p.Descriptor.Name, request.PackageName.Trim(), StringComparison.OrdinalIgnoreCase))
			?.Descriptor.UId
			?? throw new InvalidOperationException($"Package '{request.PackageName}' was not found.");
		EntityDesignSchemaDto entitySchema = LoadEntitySchema(request.EntitySchemaName, packageUId);
		IReadOnlyDictionary<string, EntitySchemaColumnDto> columnMap = BusinessRuleToDtoConverter.BuildColumnMap(entitySchema);
		BusinessRuleValidator.ValidateRuleAgainstSchema(request.Rule, columnMap);
		AddonSchemaResponseDto addonResponse = LoadAddonSchema(entitySchema, packageUId);
		BusinessRulesAddonMetadata metadata = ParseMetadata(addonResponse.Schema?.MetaData);
		List<AddonResourceDto> resources = NormalizeResourceKeys(addonResponse.Schema?.Resources?.ToList() ?? []);

		BusinessRuleMetadataDto createdRule = BusinessRuleToDtoConverter.BuildRule(columnMap, request.Rule);
		metadata.Rules.Add(createdRule);
		UpsertCaptionResource(resources, createdRule.UId, request.Rule.Caption.Trim());

		AddonSchemaDto schema = addonResponse.Schema
			?? throw new InvalidOperationException("AddonSchemaDesignerService did not return a schema payload.");
		schema.MetaData = JsonSerializer.Serialize(metadata, JsonOptions);
		schema.Resources = resources;

		SaveAddonSchema(schema);

		return new BusinessRuleCreateResult(createdRule.Name);
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

	private AddonSchemaResponseDto LoadAddonSchema(
		EntityDesignSchemaDto entitySchema,
		Guid packageUId) {
		string responseBody = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build($"{AddonDesignerBasePath}/GetSchema"),
			JsonSerializer.Serialize(new AddonGetRequestDto {
				AddonName = BusinessRuleAddonName,
				TargetSchemaUId = entitySchema.UId,
				TargetParentSchemaUId = entitySchema.ParentSchema?.UId ?? Guid.Empty,
				TargetPackageUId = packageUId,
				TargetSchemaManagerName = EntitySchemaManagerName,
				UseFullHierarchy = true
			}, JsonOptions));
		AddonSchemaResponseDto response = Deserialize<AddonSchemaResponseDto>(
			responseBody,
			"AddonSchemaDesignerService returned an empty response.");
		if (!response.Success) {
			throw new InvalidOperationException(response.ErrorInfo?.Message ?? "AddonSchemaDesignerService.GetSchema failed.");
		}

		return response;
	}

	private void SaveAddonSchema(AddonSchemaDto schema) {
		string responseBody = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build($"{AddonDesignerBasePath}/SaveSchema"),
			JsonSerializer.Serialize(schema, JsonOptions));
		AddonSaveResponseDto response = Deserialize<AddonSaveResponseDto>(
			responseBody,
			"AddonSchemaDesignerService.SaveSchema returned an empty response.");
		if (!response.Success || response.Value == false) {
			throw new InvalidOperationException(response.ErrorInfo?.Message ?? "AddonSchemaDesignerService.SaveSchema failed.");
		}

		applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build("/rest/WorkplaceService/ResetScriptCache"),
			string.Empty);
	}

	private static BusinessRulesAddonMetadata ParseMetadata(string? metaData) {
		if (string.IsNullOrWhiteSpace(metaData)) {
			return new BusinessRulesAddonMetadata();
		}

		return JsonSerializer.Deserialize<BusinessRulesAddonMetadata>(metaData, JsonOptions)
			?? new BusinessRulesAddonMetadata();
	}

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

	private T Deserialize<T>(string responseBody, string emptyMessage) {
		if (string.IsNullOrWhiteSpace(responseBody)) {
			throw new InvalidOperationException(emptyMessage);
		}

		try {
			return JsonSerializer.Deserialize<T>(responseBody, JsonOptions)
				?? throw new InvalidOperationException(emptyMessage);
		} catch (JsonException) {
			string correctedJson = jsonConverter.CorrectJson(responseBody);
			return JsonSerializer.Deserialize<T>(correctedJson, JsonOptions)
				?? throw new InvalidOperationException(emptyMessage);
		}
	}
}
