using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Package;

namespace Clio.Mcp.E2E.Support.Creatio;

internal static class LookupRegistrationProbe {
	internal static LookupRegistrationSnapshot Read(string environmentName, string packageName, string lookupSchemaName) {
		EnvironmentSettings environmentSettings = RegisteredClioEnvironmentSettingsResolver.Resolve(environmentName);
		IApplicationClient applicationClient = new ApplicationClientFactory().CreateEnvironmentClient(environmentSettings);
		ServiceUrlBuilder serviceUrlBuilder = new(environmentSettings);
		LookupRegistrationSelectResponse lookupRows =
			SelectQueryHelper.ExecuteSelectQuery<LookupRegistrationSelectResponse>(
				applicationClient,
				serviceUrlBuilder,
				SelectQueryHelper.BuildSelectQuery(
					"Lookup",
					[
						new SelectQueryHelper.SelectQueryColumnDefinition("Id", "Id"),
						new SelectQueryHelper.SelectQueryColumnDefinition("Name", "Name")
					],
					[
						new SelectQueryHelper.SelectQueryFilterDefinition(
							"SysEntitySchema.Name",
							lookupSchemaName,
							SelectQueryHelper.TextDataValueType)
					]));
		PackageSchemaDataSelectResponse bindingRows =
			SelectQueryHelper.ExecuteSelectQuery<PackageSchemaDataSelectResponse>(
				applicationClient,
				serviceUrlBuilder,
				SelectQueryHelper.BuildSelectQuery(
					"SysPackageSchemaData",
					[
						new SelectQueryHelper.SelectQueryColumnDefinition("UId", "UId"),
						new SelectQueryHelper.SelectQueryColumnDefinition("SysSchema.Name", "EntitySchemaName")
					],
					[
						new SelectQueryHelper.SelectQueryFilterDefinition(
							"Name",
							$"Lookup_{lookupSchemaName}",
							SelectQueryHelper.TextDataValueType),
						new SelectQueryHelper.SelectQueryFilterDefinition(
							"SysPackage.Name",
							packageName,
							SelectQueryHelper.TextDataValueType)
					]));
		PackageSchemaDataBindingDto? bindingRow = bindingRows.Rows.SingleOrDefault();
		IReadOnlyList<string> boundRecordIds = bindingRow is null || !Guid.TryParse(bindingRow.UId, out Guid bindingUId)
			? []
			: FetchBoundRecordIds(applicationClient, serviceUrlBuilder, bindingUId);
		LookupRegistrationRowDto? lookupRow = lookupRows.Rows.SingleOrDefault();
		return new LookupRegistrationSnapshot(
			lookupRows.Rows.Count,
			lookupRow?.Id,
			lookupRow?.Name,
			bindingRows.Rows.Count,
			bindingRow?.UId,
			bindingRow?.EntitySchemaName,
			boundRecordIds);
	}

	private static IReadOnlyList<string> FetchBoundRecordIds(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		Guid bindingUId) {
		string response = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetBoundSchemaData),
			$$"""{"uId":"{{bindingUId}}"}""");
		using JsonDocument document = JsonDocument.Parse(response);
		if (!document.RootElement.TryGetProperty("items", out JsonElement itemsElement)) {
			return [];
		}

		JsonElement rowsElement = itemsElement.ValueKind switch {
			JsonValueKind.String when !string.IsNullOrWhiteSpace(itemsElement.GetString()) =>
				JsonDocument.Parse(itemsElement.GetString()!).RootElement.Clone(),
			JsonValueKind.Array => itemsElement.Clone(),
			_ => default
		};
		if (rowsElement.ValueKind != JsonValueKind.Array) {
			return [];
		}

		List<string> rowIds = [];
		foreach (JsonElement row in rowsElement.EnumerateArray()) {
			if (row.TryGetProperty("Id", out JsonElement idElement) &&
				idElement.ValueKind == JsonValueKind.String &&
				!string.IsNullOrWhiteSpace(idElement.GetString())) {
				rowIds.Add(idElement.GetString()!);
			}
		}

		return rowIds;
	}

	private sealed class LookupRegistrationSelectResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<LookupRegistrationRowDto> Rows { get; init; } = [];
	}

	private sealed class LookupRegistrationRowDto {
		[JsonPropertyName("Id")]
		public string? Id { get; init; }

		[JsonPropertyName("Name")]
		public string? Name { get; init; }
	}

	private sealed class PackageSchemaDataSelectResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<PackageSchemaDataBindingDto> Rows { get; init; } = [];
	}

	private sealed class PackageSchemaDataBindingDto {
		[JsonPropertyName("UId")]
		public string? UId { get; init; }

		[JsonPropertyName("EntitySchemaName")]
		public string? EntitySchemaName { get; init; }
	}
}

internal sealed record LookupRegistrationSnapshot(
	int LookupRowCount,
	string? LookupRowId,
	string? LookupRowTitle,
	int BindingCount,
	string? BindingUId,
	string? BindingEntitySchemaName,
	IReadOnlyList<string> BoundRecordIds);
