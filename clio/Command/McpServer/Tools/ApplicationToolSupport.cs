using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;

namespace Clio.Command.McpServer.Tools;

internal static class ApplicationToolResultMapper {
	public static ApplicationContextResponse Map(ApplicationInfoResult result) {
		IReadOnlyList<ApplicationEntityInfoResult> entities = result.Entities;
		string? canonicalMainEntityName = ModelingGuardrails.ResolveCanonicalMainEntityName(
			result.PackageName,
			entities);
		return new ApplicationContextResponse(
			true,
			result.PackageUId,
			result.PackageName,
			canonicalMainEntityName,
			result.ApplicationId,
			result.ApplicationName,
			result.ApplicationCode,
			result.ApplicationVersion,
			entities
				.Select(entity => new ApplicationEntityResult(
					entity.UId,
					entity.Name,
					entity.Caption,
					entity.Columns
						.Select(column => new ApplicationColumnResult(
							column.Name,
							column.Caption,
							column.DataValueType,
							column.ReferenceSchema))
						.ToList()))
				.ToList(),
			result.Pages?
				.Select(page => new PageListItem {
					SchemaName = page.SchemaName,
					UId = page.UId,
					PackageName = page.PackageName,
					ParentSchemaName = page.ParentSchemaName
				})
				.ToList());
	}

	public static ApplicationSectionContextResponse Map(ApplicationSectionCreateResult result) {
		return new ApplicationSectionContextResponse(
			true,
			result.PackageUId,
			result.PackageName,
			result.ApplicationId,
			result.ApplicationName,
			result.ApplicationCode,
			result.ApplicationVersion,
			new ApplicationSectionResult(
				result.Section.Id,
				result.Section.Code,
				result.Section.Caption,
				result.Section.Description,
				result.Section.EntitySchemaName,
				result.Section.PackageId,
				result.Section.SectionSchemaUId,
				result.Section.IconId,
				result.Section.IconBackground,
				result.Section.ClientTypeId),
			result.Entity is null
				? null
				: new ApplicationEntityResult(
					result.Entity.UId,
					result.Entity.Name,
					result.Entity.Caption,
					result.Entity.Columns
						.Select(column => new ApplicationColumnResult(
							column.Name,
							column.Caption,
							column.DataValueType,
							column.ReferenceSchema))
						.ToList()),
			result.Pages
				.Select(page => new PageListItem {
					SchemaName = page.SchemaName,
					UId = page.UId,
					PackageName = page.PackageName,
					ParentSchemaName = page.ParentSchemaName
				})
				.ToList());
	}

	public static ApplicationSectionUpdateContextResponse Map(ApplicationSectionUpdateResult result) {
		return new ApplicationSectionUpdateContextResponse(
			true,
			result.PackageUId,
			result.PackageName,
			result.ApplicationId,
			result.ApplicationName,
			result.ApplicationCode,
			result.ApplicationVersion,
			new ApplicationSectionResult(
				result.PreviousSection.Id,
				result.PreviousSection.Code,
				result.PreviousSection.Caption,
				result.PreviousSection.Description,
				result.PreviousSection.EntitySchemaName,
				result.PreviousSection.PackageId,
				result.PreviousSection.SectionSchemaUId,
				result.PreviousSection.IconId,
				result.PreviousSection.IconBackground,
				result.PreviousSection.ClientTypeId),
			new ApplicationSectionResult(
				result.Section.Id,
				result.Section.Code,
				result.Section.Caption,
				result.Section.Description,
				result.Section.EntitySchemaName,
				result.Section.PackageId,
				result.Section.SectionSchemaUId,
				result.Section.IconId,
				result.Section.IconBackground,
				result.Section.ClientTypeId));
	}

	public static ApplicationSectionDeleteContextResponse Map(ApplicationSectionDeleteResult result) {
		return new ApplicationSectionDeleteContextResponse(
			true,
			result.PackageUId,
			result.PackageName,
			result.ApplicationId,
			result.ApplicationName,
			result.ApplicationCode,
			result.ApplicationVersion,
			new ApplicationSectionResult(
				result.DeletedSection.Id,
				result.DeletedSection.Code,
				result.DeletedSection.Caption,
				result.DeletedSection.Description,
				result.DeletedSection.EntitySchemaName,
				result.DeletedSection.PackageId,
				result.DeletedSection.SectionSchemaUId,
				result.DeletedSection.IconId,
				result.DeletedSection.IconBackground,
				result.DeletedSection.ClientTypeId));
	}

	public static ApplicationSectionListContextResponse Map(ApplicationSectionGetListResult result) {
		return new ApplicationSectionListContextResponse(
			true,
			result.PackageUId,
			result.PackageName,
			result.ApplicationId,
			result.ApplicationName,
			result.ApplicationCode,
			result.ApplicationVersion,
			result.Sections
				.Select(s => new ApplicationSectionResult(
					s.Id,
					s.Code,
					s.Caption,
					s.Description,
					s.EntitySchemaName,
					s.PackageId,
					s.SectionSchemaUId,
					s.IconId,
					s.IconBackground,
					s.ClientTypeId))
				.ToList());
	}
}

internal static class ApplicationToolHelper {
	public static ApplicationListResponse CreateListResponse(IReadOnlyList<ApplicationListItemResult> applications) {
		return new ApplicationListResponse(true, applications);
	}

	public static ApplicationListResponse CreateListErrorResponse(string message) {
		return new ApplicationListResponse(false, Error: message);
	}

	public static ApplicationContextResponse CreateContextResponse(
		ApplicationContextResponse response,
		ApplicationDataForgeResult? dataForge = null) {
		return dataForge is null
			? response
			: response with { DataForge = dataForge };
	}

	public static ApplicationContextResponse CreateContextErrorResponse(string message) {
		return new ApplicationContextResponse(false, Error: message);
	}

	public static ApplicationOptionalTemplateData? ParseOptionalTemplateData(string? optionalTemplateDataJson) {
		if (string.IsNullOrWhiteSpace(optionalTemplateDataJson)) {
			return null;
		}

		ApplicationOptionalTemplateDataJsonArgs? optionalTemplateData;
		try {
			optionalTemplateData = JsonSerializer.Deserialize<ApplicationOptionalTemplateDataJsonArgs>(
				optionalTemplateDataJson,
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
		} catch (JsonException ex) {
			throw new ArgumentException(
				$"Invalid optional-template-data-json format: {ex.Message}",
				nameof(optionalTemplateDataJson),
				ex);
		}

		if (optionalTemplateData?.UseAiContentGeneration == true) {
			throw new ArgumentException(
				"useAiContentGeneration=true is not supported in application tools.");
		}

		return optionalTemplateData is null
			? null
			: new ApplicationOptionalTemplateData(
				optionalTemplateData.EntitySchemaName,
				optionalTemplateData.UseExistingEntitySchema,
				optionalTemplateData.UseAiContentGeneration,
				optionalTemplateData.AppSectionDescription);
	}

	public static ApplicationSectionContextResponse CreateSectionContextResponse(ApplicationSectionContextResponse response) {
		return response;
	}

	public static ApplicationSectionContextResponse CreateSectionContextErrorResponse(string message) {
		return new ApplicationSectionContextResponse(false, Error: message);
	}

	public static ApplicationSectionUpdateContextResponse CreateSectionUpdateContextResponse(ApplicationSectionUpdateContextResponse response) {
		return response;
	}

	public static ApplicationSectionUpdateContextResponse CreateSectionUpdateContextErrorResponse(string message) {
		return new ApplicationSectionUpdateContextResponse(false, Error: message);
	}

	public static ApplicationSectionDeleteContextResponse CreateSectionDeleteContextResponse(ApplicationSectionDeleteContextResponse response) {
		return response;
	}

	public static ApplicationSectionDeleteContextResponse CreateSectionDeleteContextErrorResponse(string message) {
		return new ApplicationSectionDeleteContextResponse(false, Error: message);
	}

	public static ApplicationSectionListContextResponse CreateSectionListContextResponse(ApplicationSectionListContextResponse response) {
		return response;
	}

	public static ApplicationSectionListContextResponse CreateSectionListContextErrorResponse(string message) {
		return new ApplicationSectionListContextResponse(false, Error: message);
	}
}
