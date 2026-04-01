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
}
