using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Command;

/// <summary>
/// Resolves available Creatio entity schemas for lookup user task parameters.
/// </summary>
public interface IUserTaskLookupSchemaResolver {

	/// <summary>
	/// Resolves an entity schema by schema name or schema UId for a specific package context.
	/// </summary>
	/// <param name="packageUId">Package UId used by the designer lookup discovery service.</param>
	/// <param name="lookupValue">Schema name or schema UId provided by the caller.</param>
	/// <returns>The resolved lookup schema descriptor.</returns>
	UserTaskLookupSchema Resolve(Guid packageUId, string lookupValue);
}

/// <summary>
/// Loads available entity schemas through the same designer endpoint used by Creatio when creating lookup parameters.
/// </summary>
public class UserTaskLookupSchemaResolver(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder)
	: IUserTaskLookupSchemaResolver {

	/// <inheritdoc />
	public UserTaskLookupSchema Resolve(Guid packageUId, string lookupValue) {
		string normalizedLookupValue = lookupValue?.Trim();
		if (string.IsNullOrWhiteSpace(normalizedLookupValue)) {
			throw new InvalidOperationException(
				"Lookup parameters must include a non-empty 'lookup' value with an entity schema name or schema UId.");
		}

		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetAvailableEntitySchemas);
		string requestBody = JsonSerializer.Serialize(new GetAvailableEntitySchemasRequestDto {
			PackageUId = packageUId
		});
		string responseJson = applicationClient.ExecutePostRequest(url, requestBody);
		GetAvailableEntitySchemasResponse response = UserTaskSchemaSupport
			.Deserialize<GetAvailableEntitySchemasResponse>(responseJson, "GetAvailableEntitySchemas");

		List<UserTaskLookupSchema> items = response.Items ?? [];
		if (Guid.TryParse(normalizedLookupValue, out Guid schemaUId)) {
			UserTaskLookupSchema byUId = items.FirstOrDefault(item => item.UId == schemaUId);
			return byUId ?? throw new InvalidOperationException(
				$"Lookup schema '{normalizedLookupValue}' was not found in Creatio.");
		}

		List<UserTaskLookupSchema> matches = items
			.Where(item => string.Equals(item.Name, normalizedLookupValue, StringComparison.OrdinalIgnoreCase))
			.ToList();
		if (matches.Count == 1) {
			return matches[0];
		}

		if (matches.Count > 1) {
			throw new InvalidOperationException(
				$"Lookup schema '{normalizedLookupValue}' resolved to multiple Creatio schemas.");
		}

		throw new InvalidOperationException(
			$"Lookup schema '{normalizedLookupValue}' was not found in Creatio.");
	}
}

/// <summary>
/// Represents a lookup entity schema returned by Creatio designer services.
/// </summary>
public sealed class UserTaskLookupSchema {

	/// <summary>
	/// Gets or sets the schema UId.
	/// </summary>
	[JsonPropertyName("uId")]
	public Guid UId { get; set; }

	/// <summary>
	/// Gets or sets the entity schema name.
	/// </summary>
	[JsonPropertyName("name")]
	public string Name { get; set; }

	/// <summary>
	/// Gets or sets the user-facing entity caption.
	/// </summary>
	[JsonPropertyName("caption")]
	public string Caption { get; set; }
}

internal sealed class GetAvailableEntitySchemasRequestDto {
	[JsonPropertyName("packageUId")]
	public Guid PackageUId { get; set; }
}

internal sealed class GetAvailableEntitySchemasResponse {
	[JsonPropertyName("items")]
	public List<UserTaskLookupSchema> Items { get; set; }
}
