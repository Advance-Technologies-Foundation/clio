using System;
using System.Collections.Generic;
using System.Text.Json;
using Clio.Common;

namespace Clio.Command.SchemaTransfer;

/// <summary>
/// Talks to the ClioGate schema-transfer routes of one Creatio environment.
/// </summary>
public interface ISchemaTransferClient {

	/// <summary>
	/// Lists every package layer that carries a schema of the given name.
	/// </summary>
	/// <param name="schemaName">Schema name to look up.</param>
	/// <param name="managerName">Optional schema manager to narrow the lookup to.</param>
	/// <returns>The matching layers; empty when the schema does not exist in the environment.</returns>
	/// <exception cref="InvalidOperationException">Thrown when the environment reports a failure.</exception>
	IReadOnlyList<SchemaLayerDto> FindLayers(string schemaName, string managerName);

	/// <summary>
	/// Exports one schema.
	/// </summary>
	/// <param name="schemaName">Schema name.</param>
	/// <param name="packageName">Package that owns the layer to export; required when the name is ambiguous.</param>
	/// <param name="managerName">Optional schema manager, to narrow an ambiguous name further.</param>
	/// <returns>The identity of the exported layer and the verbatim platform payload.</returns>
	/// <exception cref="InvalidOperationException">
	/// Thrown when the schema is missing, or when the name matches more than one layer — in which case the
	/// message names the packages, so the caller can retry with an explicit package.
	/// </exception>
	(SchemaLayerDto Schema, string SchemaData) Export(string schemaName, string packageName, string managerName);

	/// <summary>
	/// Imports a previously exported payload into a package.
	/// </summary>
	/// <param name="schemaData">Verbatim payload from <see cref="Export"/>.</param>
	/// <param name="packageName">Target package name.</param>
	/// <returns>The platform importer's own diagnostic string; may be empty.</returns>
	/// <exception cref="InvalidOperationException">Thrown when the environment reports a failure.</exception>
	string Import(string schemaData, string packageName);
}

/// <inheritdoc cref="ISchemaTransferClient"/>
public sealed class SchemaTransferClient : ISchemaTransferClient {

	private static readonly JsonSerializerOptions SerializerOptions = new() {
		PropertyNameCaseInsensitive = true
	};

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;

	/// <summary>
	/// Initializes a new instance of the <see cref="SchemaTransferClient"/> class.
	/// </summary>
	/// <param name="applicationClient">Client of the target environment.</param>
	/// <param name="serviceUrlBuilder">Builder of the ClioGate route URLs.</param>
	public SchemaTransferClient(IApplicationClient applicationClient, IServiceUrlBuilder serviceUrlBuilder) {
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	/// <inheritdoc/>
	public IReadOnlyList<SchemaLayerDto> FindLayers(string schemaName, string managerName) {
		FindSchemaLayersResponse response = Post<FindSchemaLayersResponse>(
			ServiceUrlBuilder.KnownRoute.FindSchemaLayers,
			new Dictionary<string, string> {
				["schemaName"] = schemaName,
				["managerName"] = managerName
			});
		EnsureSucceeded(response.Success, response.ErrorInfo?.Message,
			$"Could not look up schema '{schemaName}'.");
		return response.Layers ?? [];
	}

	/// <inheritdoc/>
	public (SchemaLayerDto Schema, string SchemaData) Export(string schemaName, string packageName,
		string managerName) {
		ExportSchemaGateResponse response = Post<ExportSchemaGateResponse>(
			ServiceUrlBuilder.KnownRoute.ExportSchema,
			new Dictionary<string, string> {
				["schemaName"] = schemaName,
				["packageName"] = packageName,
				["managerName"] = managerName
			});
		EnsureSucceeded(response.Success, response.ErrorInfo?.Message,
			$"Could not export schema '{schemaName}'.");
		if (string.IsNullOrEmpty(response.SchemaData)) {
			throw new InvalidOperationException(
				$"The environment reported success but returned no payload for schema '{schemaName}'.");
		}
		return (response.Schema, response.SchemaData);
	}

	/// <inheritdoc/>
	public string Import(string schemaData, string packageName) {
		ImportSchemaGateResponse response = Post<ImportSchemaGateResponse>(
			ServiceUrlBuilder.KnownRoute.ImportSchema,
			new Dictionary<string, string> {
				["schemaData"] = schemaData,
				["packageName"] = packageName
			});
		EnsureSucceeded(response.Success, response.ErrorInfo?.Message,
			$"Could not import the schema into package '{packageName}'.");
		return response.ImportResult;
	}

	private T Post<T>(ServiceUrlBuilder.KnownRoute route, Dictionary<string, string> body)
		where T : Clio.Common.Responses.BaseResponse {
		string url = _serviceUrlBuilder.Build(route);
		string responseBody = _applicationClient.ExecutePostRequest(url, JsonSerializer.Serialize(body));
		T response;
		try {
			response = JsonSerializer.Deserialize<T>(responseBody, SerializerOptions);
		}
		catch (JsonException exception) {
			// A non-JSON body here is almost always an auth redirect or a WCF error page, and its raw HTML is
			// useless in a CLI or MCP transcript — name the route instead.
			throw new InvalidOperationException(
				$"{url} did not return a JSON response ({exception.Message}). "
				+ "Check that cliogate 2.0.0.46 or newer is installed on the environment.");
		}
		return response
			?? throw new InvalidOperationException($"{url} returned an empty response.");
	}

	private static void EnsureSucceeded(bool success, string platformMessage, string fallbackMessage) {
		if (success) {
			return;
		}
		throw new InvalidOperationException(
			string.IsNullOrWhiteSpace(platformMessage) ? fallbackMessage : platformMessage);
	}
}
