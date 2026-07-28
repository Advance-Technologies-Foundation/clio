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
							column.ReferenceSchema,
							column.Required))
						.ToList(),
					entity.IsVirtual))
				.ToList(),
			result.Pages?
				.Select(page => new PageListItem {
					SchemaName = page.SchemaName,
					UId = page.UId,
					PackageName = page.PackageName,
					ParentSchemaName = page.ParentSchemaName
				})
				.ToList(),
			result.SchemaNamePrefix);
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
							column.ReferenceSchema,
							column.Required))
						.ToList(),
					result.Entity.IsVirtual),
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

		if (!string.IsNullOrWhiteSpace(optionalTemplateData?.EntitySchemaName)
				&& optionalTemplateData.UseExistingEntitySchema != true) {
			throw new ArgumentException(
				"entitySchemaName is only valid together with useExistingEntitySchema=true. " +
				"The entity must already exist in Creatio before create-app is called. " +
				"To create a new app with an auto-generated entity, omit optional-template-data-json entirely.");
		}

		if (optionalTemplateData?.UseExistingEntitySchema == true
				&& string.IsNullOrWhiteSpace(optionalTemplateData.EntitySchemaName)) {
			throw new ArgumentException(
				"entitySchemaName is required when useExistingEntitySchema=true.");
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

	/// <summary>
	/// Maps a classified section-create failure (ENG-90679) onto the structured error envelope,
	/// carrying <c>error-class</c>, <c>section-created</c>, and <c>retry-guidance</c> so the calling
	/// agent can distinguish a transport failure from an in-flight server-side operation.
	/// </summary>
	/// <param name="exception">Classified section-create failure.</param>
	/// <returns>Structured error envelope.</returns>
	public static ApplicationSectionContextResponse CreateSectionContextErrorResponse(
		ApplicationSectionCreateException exception) {
		return new ApplicationSectionContextResponse(
			false,
			Error: SensitiveErrorTextRedactor.Redact(exception.Message),
			ErrorClass: exception.FailureClass.ToWireValue(),
			SectionCreated: exception.SectionCreated switch {
				true => "true",
				false => "false",
				null => "unknown"
			},
			RetryGuidance: exception.RetryGuidance);
	}

	/// <summary>
	/// Builds the "in-progress" envelope returned when section creation exceeds the MCP response
	/// deadline (ENG-91316). The backend operation keeps running on the long-lived server, so this is
	/// not a failure: the agent must poll instead of retrying or falling back to standalone page
	/// creation. Mirrors the <c>creatio-timeout</c> error-class so existing client guidance applies,
	/// but uses <c>section-created: in-progress</c> to distinguish "still creating" from the
	/// verification-failed <c>unknown</c>.
	/// </summary>
	/// <param name="caption">Requested section caption, surfaced in the guidance message.</param>
	/// <param name="code">Optional explicit section code; helps the agent recognise the generated page schemas.</param>
	/// <returns>Structured in-progress envelope.</returns>
	public static ApplicationSectionContextResponse CreateSectionInProgressResponse(string caption, string? code) {
		string codeHint = string.IsNullOrWhiteSpace(code)
			? string.Empty
			: $" (code '{code.Trim()}', pages '{code.Trim()}_ListPage' / '{code.Trim()}_FormPage')";
		return new ApplicationSectionContextResponse(
			false,
			Error: $"Section '{caption}'{codeHint} is still being created server-side and did not finish "
				+ "within the response deadline.",
			ErrorClass: ApplicationSectionCreateFailureClass.CreatioTimeout.ToWireValue(),
			SectionCreated: "in-progress",
			RetryGuidance: "The section creation is still running on the server. Do NOT retry create-app-section "
				+ "(a retry would create a duplicate section) and do NOT fall back to create-page. Wait a "
				+ "short while, then poll list-app-sections and get-app-info until the section and its "
				+ "generated List and Form pages appear; only then continue. If the section still does not "
				+ "appear after several minutes of polling, the background creation has failed (not merely "
				+ "slowed) and a single retry of create-app-section is then safe.");
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
