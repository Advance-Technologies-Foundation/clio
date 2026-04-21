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
		string? entitySchemaUId = ResolveEntitySchemaUId(applicationClient, serviceUrlBuilder, lookupSchemaName);
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
					entitySchemaUId is null
						? []
						: [
							new SelectQueryHelper.SelectQueryFilterDefinition(
								"SysEntitySchemaUId",
								entitySchemaUId,
								SelectQueryHelper.GuidDataValueType)
						]));
		string? packageUId = ResolvePackageUId(applicationClient, serviceUrlBuilder, packageName);
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
							"SysPackage.UId",
							packageUId ?? Guid.Empty.ToString(),
							SelectQueryHelper.GuidDataValueType)
					]));
		PackageSchemaDataBindingDto? bindingRow = bindingRows.Rows.FirstOrDefault();
		IReadOnlyList<string> boundRecordIds = bindingRow is null || !Guid.TryParse(bindingRow.UId, out Guid bindingUId)
			? []
			: FetchBoundRecordIds(applicationClient, serviceUrlBuilder, bindingUId);
		LookupRegistrationRowDto? lookupRow = lookupRows.Rows.FirstOrDefault();
		return new LookupRegistrationSnapshot(
			lookupRows.Rows.Count,
			lookupRow?.Id,
			lookupRow?.Name,
			bindingRows.Rows.Count,
			bindingRow?.UId,
			bindingRow?.EntitySchemaName,
			boundRecordIds);
	}

	private static string? ResolveEntitySchemaUId(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		string lookupSchemaName) {
		string responseJson = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RuntimeEntitySchemaRequest),
			$$"""{"Name":"{{lookupSchemaName}}"}""");
		RuntimeEntitySchemaResponseDto? response = JsonSerializer.Deserialize<RuntimeEntitySchemaResponseDto>(
			responseJson,
			RuntimeEntitySchemaJsonOptions);
		if (response?.Success != true || response.Schema is null) {
			return null;
		}

		return response.Schema.UId == Guid.Empty ? null : response.Schema.UId.ToString();
	}

	private static string? ResolvePackageUId(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		string packageName) {
		PackageSelectResponse response =
			SelectQueryHelper.ExecuteSelectQuery<PackageSelectResponse>(
				applicationClient,
				serviceUrlBuilder,
				SelectQueryHelper.BuildSelectQuery(
					"SysPackage",
					[
						new SelectQueryHelper.SelectQueryColumnDefinition("UId", "UId")
					],
					[
						new SelectQueryHelper.SelectQueryFilterDefinition(
							"Name",
							packageName,
							SelectQueryHelper.TextDataValueType)
					]));
		return response.Rows.SingleOrDefault()?.UId;
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

	private sealed record RuntimeEntitySchemaResponseDto(
		[property: JsonPropertyName("success")] bool Success,
		[property: JsonPropertyName("schema")] RuntimeEntitySchemaPayloadDto? Schema);

	private sealed record RuntimeEntitySchemaPayloadDto(
		[property: JsonPropertyName("UId")] Guid UId);

	private static readonly JsonSerializerOptions RuntimeEntitySchemaJsonOptions = new() {
		PropertyNameCaseInsensitive = true
	};

	private sealed class PackageSelectResponse : SelectQueryHelper.SelectQueryResponseBaseDto {
		[JsonPropertyName("rows")]
		public List<PackageRowDto> Rows { get; init; } = [];
	}

	private sealed class PackageRowDto {
		[JsonPropertyName("UId")]
		public string? UId { get; init; }
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
