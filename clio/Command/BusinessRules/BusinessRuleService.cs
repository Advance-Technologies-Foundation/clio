using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Package;
using Clio.UserEnvironment;
using static Clio.Command.BusinessRules.BusinessRuleConstants;

namespace Clio.Command.BusinessRules;

/// <summary>
/// Creates object-level Freedom UI business rules through the entity add-on authoring flow.
/// </summary>
public interface IBusinessRuleService {
	/// <summary>
	/// Creates an object business rule in the requested package and environment.
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <param name="request">Structured object business-rule request.</param>
	/// <returns>Structured information about the created business rule.</returns>
	BusinessRuleCreateResult Create(string environmentName, BusinessRuleCreateRequest request);
}

/// <summary>
/// Structured request for object business-rule creation.
/// </summary>
public sealed record BusinessRuleCreateRequest(
	string PackageName,
	string EntitySchemaName,
	BusinessRule Rule
);

/// <summary>
/// Structured result for object business-rule creation.
/// </summary>
public sealed record BusinessRuleCreateResult(string RuleName);

/// <summary>
/// Default object business-rule creator backed by <c>AddonSchemaDesignerService.svc</c>.
/// </summary>
public sealed class BusinessRuleService(
	ISettingsRepository settingsRepository,
	IApplicationClientFactory applicationClientFactory,
	IApplicationPackageListProvider applicationPackageListProvider,
	IJsonConverter jsonConverter)
	: IBusinessRuleService {

	/// <inheritdoc />
	public BusinessRuleCreateResult Create(string environmentName, BusinessRuleCreateRequest request) {
		if (string.IsNullOrWhiteSpace(environmentName)) {
			throw new ArgumentException("environment-name is required.", nameof(environmentName));
		}

		ArgumentNullException.ThrowIfNull(request);
		BusinessRuleValidator.ValidateRequest(request);

		EnvironmentSettings environmentSettings = settingsRepository.FindEnvironment(environmentName)
			?? throw new InvalidOperationException(
				$"Environment with key '{environmentName}' not found. Check your clio configuration.");
		IApplicationClient client = applicationClientFactory.CreateEnvironmentClient(environmentSettings);
		ServiceUrlBuilder serviceUrlBuilder = new(environmentSettings);

		Guid packageUId = applicationPackageListProvider.GetPackages()
			.FirstOrDefault(p => string.Equals(p.Descriptor.Name, request.PackageName.Trim(), StringComparison.OrdinalIgnoreCase))
			?.Descriptor.UId
			?? throw new InvalidOperationException($"Package '{request.PackageName}' was not found.");
		EntityDesignSchemaDto entitySchema = LoadEntitySchema(client, serviceUrlBuilder, request.EntitySchemaName, packageUId);
		IReadOnlyDictionary<string, EntityColumnDescriptor> columnIndex = BusinessRuleToDtoConverter.BuildColumnIndex(entitySchema);
		BusinessRuleValidator.ValidateRuleAgainstSchema(request.Rule, columnIndex);
		AddonSchemaResponseDto addonResponse = LoadAddonSchema(client, serviceUrlBuilder, entitySchema, packageUId);
		BusinessRulesAddonMetadata metadata = ParseMetadata(addonResponse.Schema?.MetaData);
		List<AddonResourceDto> resources = NormalizeResourceKeys(addonResponse.Schema?.Resources?.ToList() ?? []);

		BusinessRuleMetadataDto createdRule = BusinessRuleToDtoConverter.BuildRule(columnIndex, request.Rule);
		metadata.Rules.Add(createdRule);
		UpsertCaptionResource(resources, createdRule.UId, request.Rule.Caption.Trim());

		AddonSchemaDto schema = addonResponse.Schema
			?? throw new InvalidOperationException("AddonSchemaDesignerService did not return a schema payload.");
		schema.MetaData = JsonSerializer.Serialize(metadata, JsonOptions);
		schema.Resources = resources;

		SaveAddonSchema(client, serviceUrlBuilder, schema);

		return new BusinessRuleCreateResult(createdRule.Name);
	}

	private EntityDesignSchemaDto LoadEntitySchema(
		IApplicationClient client,
		ServiceUrlBuilder serviceUrlBuilder,
		string entitySchemaName,
		Guid packageUId) {
		string responseBody = client.ExecutePostRequest(
			serviceUrlBuilder.Build($"{EntitySchemaDesignerBasePath}/GetSchemaDesignItem"),
			JsonSerializer.Serialize(new {
				name = entitySchemaName.Trim(),
				packageUId,
				useFullHierarchy = true,
				cultures = new[] { EntitySchemaDesignerSupport.DefaultCultureName }
			}, JsonOptions));
		DesignerResponse<EntityDesignSchemaDto> response = Deserialize<DesignerResponse<EntityDesignSchemaDto>>(
			responseBody,
			"EntitySchemaDesignerService returned an empty response.");
		if (!response.Success || response.Schema is null) {
			throw new InvalidOperationException(
				response.ErrorInfo?.Message ?? $"Entity schema '{entitySchemaName}' was not returned.");
		}

		return response.Schema;
	}

	private AddonSchemaResponseDto LoadAddonSchema(
		IApplicationClient client,
		ServiceUrlBuilder serviceUrlBuilder,
		EntityDesignSchemaDto entitySchema,
		Guid packageUId) {
		string responseBody = client.ExecutePostRequest(
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

	private void SaveAddonSchema(IApplicationClient client, ServiceUrlBuilder serviceUrlBuilder, AddonSchemaDto schema) {
		string responseBody = client.ExecutePostRequest(
			serviceUrlBuilder.Build($"{AddonDesignerBasePath}/SaveSchema"),
			JsonSerializer.Serialize(schema, JsonOptions));
		AddonSaveResponseDto response = Deserialize<AddonSaveResponseDto>(
			responseBody,
			"AddonSchemaDesignerService.SaveSchema returned an empty response.");
		if (!response.Success || response.Value == false) {
			throw new InvalidOperationException(response.ErrorInfo?.Message ?? "AddonSchemaDesignerService.SaveSchema failed.");
		}
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
