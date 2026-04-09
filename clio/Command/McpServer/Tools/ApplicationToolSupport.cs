using System.Collections.Generic;
using System.Linq;
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
}

internal static class ApplicationToolHelper {
	public static ApplicationListResponse CreateListResponse(IReadOnlyList<ApplicationListItemResult> applications) {
		return new ApplicationListResponse(true, applications);
	}

	public static ApplicationListResponse CreateListErrorResponse(string message) {
		return new ApplicationListResponse(false, Error: message);
	}

	public static ApplicationContextResponse CreateContextResponse(ApplicationContextResponse response) {
		return response;
	}

	public static ApplicationContextResponse CreateContextErrorResponse(string message) {
		return new ApplicationContextResponse(false, Error: message);
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
}
