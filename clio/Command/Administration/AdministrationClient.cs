using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Common;

namespace Clio.Command.Administration;

/// <summary>Uses the existing authenticated transport with endpoint-specific, fail-closed parsing.</summary>
public sealed class AdministrationClient(IApplicationClient client, IServiceUrlBuilder urls)
	: IAdministrationClient {

	private const int RequestTimeoutMs = 60000;
	private const int MaximumResponseCharacters = 4 * 1024 * 1024;
	private const string InvalidResponse = "Creatio returned an invalid administration response. Inspect the target before retrying.";
	private const string FailedOperation = "Creatio rejected the administration operation. Check permissions and target state; partial changes may exist.";

	/// <inheritdoc />
	public JsonElement Post(ServiceUrlBuilder.KnownRoute route, string resultProperty, object body,
		AdministrationResponseKind kind) {
		string response = Send(route, body);
		using JsonDocument document = Parse(response);
		if (document.RootElement.ValueKind != JsonValueKind.Object
			|| !document.RootElement.TryGetProperty(resultProperty, out JsonElement result)) {
			throw new InvalidOperationException(InvalidResponse);
		}
		if (kind is AdministrationResponseKind.Boolean or AdministrationResponseKind.True) {
			if (result.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) {
				throw new InvalidOperationException(InvalidResponse);
			}
			if (kind == AdministrationResponseKind.True && !result.GetBoolean()) {
				throw new InvalidOperationException(FailedOperation);
			}
			return result.Clone();
		}
		return ParseStringResult(result, kind);
	}

	private static JsonElement ParseStringResult(JsonElement result, AdministrationResponseKind kind) {
		if (result.ValueKind != JsonValueKind.String) {
			throw new InvalidOperationException(InvalidResponse);
		}
		string inner = result.GetString();
		if (kind == AdministrationResponseKind.ErrorString) {
			if (inner != string.Empty) {
				throw new InvalidOperationException(FailedOperation);
			}
			return result.Clone();
		}
		using JsonDocument innerDocument = Parse(inner);
		JsonElement value = innerDocument.RootElement;
		if (kind == AdministrationResponseKind.Array) {
			if (value.ValueKind != JsonValueKind.Array) {
				throw new InvalidOperationException(InvalidResponse);
			}
			return value.Clone();
		}
		RequireSuccess(value);
		return value.Clone();
	}

	/// <inheritdoc />
	public JsonElement Select(string schema, IReadOnlyList<string> columns,
		IReadOnlyDictionary<string, object> filters, int offset = 0, int limit = 100) {
		if (offset < 0 || limit is < 1 or > 200 || columns.Count == 0) {
			throw new ArgumentException("Administration reads require columns, a nonnegative offset and a limit from 1 to 200.");
		}
		Dictionary<string, object> columnItems = columns.ToDictionary(column => column, column => (object)new {
			expression = new { expressionType = 0, columnPath = column },
			orderDirection = column == "Id" ? 1 : 0,
			orderPosition = column == "Id" ? 0 : -1
		});
		Dictionary<string, object> filterItems = filters.ToDictionary(pair => pair.Key, pair => (object)new {
			filterType = 1, comparisonType = 3, isEnabled = true,
			leftExpression = new { expressionType = 0, columnPath = pair.Key },
			rightExpression = new { expressionType = 2, parameter = new {
				dataValueType = pair.Value switch { Guid => 0, bool => 12, int => 4, _ => 1 },
				value = pair.Value
			} }
		});
		foreach (KeyValuePair<string, object> pair in filters.Where(pair => pair.Value is int[])) {
			filterItems[pair.Key] = new {
				filterType = 4, comparisonType = 3, isEnabled = true,
				leftExpression = new { expressionType = 0, columnPath = pair.Key },
				rightExpressions = ((int[])pair.Value).Select(value => Parameter(value)).ToArray()
			};
		}
		object body = new {
			rootSchemaName = schema, operationType = 0, rowCount = limit, rowsOffset = offset,
			isPageable = true,
			columns = new { items = columnItems },
			filters = new { filterType = 6, logicalOperation = 0, isEnabled = true, items = filterItems }
		};
		string response = Send(ServiceUrlBuilder.KnownRoute.Select, body);
		using JsonDocument document = Parse(response);
		JsonElement root = document.RootElement;
		RequireSuccess(root);
		if (!root.TryGetProperty("rows", out JsonElement rows) || rows.ValueKind != JsonValueKind.Array
			|| (root.TryGetProperty("notFoundColumns", out JsonElement missing)
				&& (missing.ValueKind != JsonValueKind.Array || missing.GetArrayLength() != 0))
			|| rows.EnumerateArray().Any(row => row.ValueKind != JsonValueKind.Object
				|| columns.Any(column => !row.TryGetProperty(column, out _)))) {
			throw new InvalidOperationException(InvalidResponse);
		}
		return rows.Clone();
	}

	/// <inheritdoc />
	public void WriteEntity(string schema, Guid id, IReadOnlyDictionary<string, object> values, bool insert) {
		if (id == Guid.Empty) { throw new ArgumentException("An exact entity ID is required."); }
		Dictionary<string, object> columns = values.ToDictionary(pair => pair.Key, pair => Parameter(pair.Value));
		if (insert) { columns["Id"] = Parameter(id); }
		object body = new {
			rootSchemaName = schema, operationType = insert ? 1 : 2,
			columnValues = new { items = columns }, filters = IdentityFilter(id)
		};
		using JsonDocument result = Parse(Send(insert ? ServiceUrlBuilder.KnownRoute.Insert : ServiceUrlBuilder.KnownRoute.Update, body));
		RequireSuccess(result.RootElement);
		RequireSingleAffectedRow(result.RootElement);
	}

	/// <inheritdoc />
	public void DeleteEntity(string schema, Guid id) {
		if (id == Guid.Empty) { throw new ArgumentException("An exact entity ID is required."); }
		using JsonDocument result = Parse(Send(ServiceUrlBuilder.KnownRoute.Delete,
			new { rootSchemaName = schema, operationType = 3, filters = IdentityFilter(id) }));
		RequireSuccess(result.RootElement);
		RequireSingleAffectedRow(result.RootElement);
	}

	private string Send(ServiceUrlBuilder.KnownRoute route, object body) {
		try {
			return client.ExecutePostRequest(urls.Build(route), JsonSerializer.Serialize(body), RequestTimeoutMs, maxAttempts: 1);
		} catch (Exception) {
			throw new InvalidOperationException("Administration transport failed. Inspect the target before retrying; partial changes may exist.");
		}
	}

	private static object Parameter(object value) => new {
		expressionType = 2, parameter = new {
			dataValueType = value switch { Guid => 0, bool => 12, int => 4, _ => 1 }, value
		}
	};

	private static object IdentityFilter(Guid id) => new {
		filterType = 6, logicalOperation = 0, isEnabled = true, items = new {
			identity = new { filterType = 1, comparisonType = 3, isEnabled = true,
				leftExpression = new { expressionType = 0, columnPath = "Id" }, rightExpression = Parameter(id) }
		}
	};

	private static void RequireSingleAffectedRow(JsonElement root) {
		if (!root.TryGetProperty("rowsAffected", out JsonElement count) || !count.TryGetInt32(out int affected) || affected != 1) {
			throw new InvalidOperationException("Administration write did not affect exactly one record. Inspect before retrying.");
		}
	}

	private static JsonDocument Parse(string response) {
		if (string.IsNullOrWhiteSpace(response) || response.Length > MaximumResponseCharacters) {
			throw new InvalidOperationException(InvalidResponse);
		}
		try {
			return JsonDocument.Parse(response);
		} catch (JsonException) {
			// Never attach the raw body or parser exception: a credential endpoint can echo input.
			throw new InvalidOperationException(InvalidResponse);
		}
	}

	private static void RequireSuccess(JsonElement value) {
		if (value.ValueKind != JsonValueKind.Object
			|| (!value.TryGetProperty("success", out JsonElement success)
				&& !value.TryGetProperty("Success", out success))) {
			throw new InvalidOperationException(InvalidResponse);
		}
		if (success.ValueKind != JsonValueKind.True) {
			throw new InvalidOperationException(FailedOperation);
		}
	}
}
