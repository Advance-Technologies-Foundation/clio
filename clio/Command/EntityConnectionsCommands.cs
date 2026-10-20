namespace Clio.Command {
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Runtime.Serialization;
	using Clio.Command.EntityConnections;
	using Clio.Common;
	using CommandLine;
	using Newtonsoft.Json;
	using JsonPropertyNameAttribute = System.Text.Json.Serialization.JsonPropertyNameAttribute;

	/// <summary>
	/// Options for the <c>get-entity-connections</c> command: reads an object's effective "Connections" (the Object
	/// designer "Connections" node, shown as fields by the Freedom UI <c>crt.Connections</c> component).
	/// </summary>
	[Verb("get-entity-connections", Aliases = ["entity-connections-get"],
		HelpText = "Read an object's Connections (ordered lookup columns shown by the Freedom UI Connections component)")]
	public class GetEntityConnectionsOptions : EnvironmentOptions {
		[Option("entity-schema-name", Required = true, HelpText = "Object (entity schema) name, e.g. 'Account'")]
		public string EntitySchemaName { get; set; }

		[Option("package-name", Required = true, HelpText = "Package whose EntityConnections add-on layer is read")]
		public string PackageName { get; set; }
	}

	/// <summary>
	/// Options for the <c>update-entity-connections</c> command: sets an object's "Connections" to a full ordered list
	/// of lookup columns by writing the package's <c>EntityConnections</c> add-on layer.
	/// </summary>
	[Verb("update-entity-connections", Aliases = ["entity-connections-set", "set-entity-connections"],
		HelpText = "Set an object's Connections to a full ordered list of lookup columns (writes the EntityConnections add-on)")]
	public class UpdateEntityConnectionsOptions : EnvironmentOptions {
		[Option("entity-schema-name", Required = true, HelpText = "Object (entity schema) name, e.g. 'Account'")]
		public string EntitySchemaName { get; set; }

		[Option("package-name", Required = true, HelpText = "Package whose EntityConnections add-on layer is written")]
		public string PackageName { get; set; }

		[Option("connections", Required = true,
			HelpText = "Full desired ordered list of lookup column names, comma-separated (e.g. 'Owner,PrimaryContact'); an empty string removes every connection")]
		public string Connections { get; set; }

		/// <summary>The <see cref="Connections"/> value split into column names; empty for a blank value.</summary>
		public IReadOnlyList<string> ParseConnections() =>
			string.IsNullOrWhiteSpace(Connections)
				? []
				: Connections.Split(',').Select(name => name.Trim()).ToList();
	}

	/// <summary>
	/// Reads an object's connections. Thin CLI/MCP front-end over <see cref="IEntityConnectionsService.Get"/>.
	/// Read-only.
	/// </summary>
	public sealed class GetEntityConnectionsCommand : Command<GetEntityConnectionsOptions> {
		private readonly IEntityConnectionsService _service;
		private readonly ILogger _logger;

		/// <summary>Initializes the command.</summary>
		public GetEntityConnectionsCommand(IEntityConnectionsService service, ILogger logger) {
			_service = service;
			_logger = logger;
		}

		/// <inheritdoc />
		public override int Execute(GetEntityConnectionsOptions options) {
			bool success = TryGet(options, out EntityConnectionsResponse response);
			_logger.WriteInfo(JsonConvert.SerializeObject(response));
			return success ? 0 : 1;
		}

		/// <summary>Reads the connections into the response envelope; never throws.</summary>
		/// <returns><c>true</c> when the read succeeded.</returns>
		public bool TryGet(GetEntityConnectionsOptions options, out EntityConnectionsResponse response) {
			if (options is null) {
				response = EntityConnectionsResponse.Fail("options is required");
				return false;
			}
			try {
				EntityConnectionsReadResult result = _service.Get(
					new EntityConnectionsReadRequest(options.PackageName, options.EntitySchemaName));
				response = EntityConnectionsResponse.From(result, changed: null);
				return true;
			} catch (Exception ex) when (EntityConnectionsResponse.IsReportable(ex)) {
				response = EntityConnectionsResponse.Fail(ex.Message);
				return false;
			}
		}
	}

	/// <summary>
	/// Sets an object's connections. Thin CLI/MCP front-end over <see cref="IEntityConnectionsService.Update"/>.
	/// </summary>
	public sealed class UpdateEntityConnectionsCommand : Command<UpdateEntityConnectionsOptions> {
		private readonly IEntityConnectionsService _service;
		private readonly ILogger _logger;

		/// <summary>Initializes the command.</summary>
		public UpdateEntityConnectionsCommand(IEntityConnectionsService service, ILogger logger) {
			_service = service;
			_logger = logger;
		}

		/// <inheritdoc />
		public override int Execute(UpdateEntityConnectionsOptions options) {
			bool success = TryUpdate(options, options?.ParseConnections(), out EntityConnectionsResponse response);
			_logger.WriteInfo(JsonConvert.SerializeObject(response));
			return success ? 0 : 1;
		}

		/// <summary>Writes the connections and returns the state read back; never throws.</summary>
		/// <param name="options">Target object, package and environment.</param>
		/// <param name="connections">The full desired ordered list of column names — the only source on the MCP path;
		/// <see cref="UpdateEntityConnectionsOptions.Connections"/> is the CLI form and is not read here.</param>
		/// <param name="response">The response envelope.</param>
		/// <returns><c>true</c> when the update succeeded (including a no-op).</returns>
		public bool TryUpdate(
			UpdateEntityConnectionsOptions options,
			IReadOnlyList<string> connections,
			out EntityConnectionsResponse response) {
			if (options is null) {
				response = EntityConnectionsResponse.Fail("options is required");
				return false;
			}
			try {
				EntityConnectionsUpdateResult result = _service.Update(
					new EntityConnectionsUpdateRequest(options.PackageName, options.EntitySchemaName, connections));
				response = EntityConnectionsResponse.From(result.State, result.Changed);
				return true;
			} catch (Exception ex) when (EntityConnectionsResponse.IsReportable(ex)) {
				response = EntityConnectionsResponse.Fail(ex.Message);
				return false;
			}
		}
	}

	/// <summary>
	/// The <c>get-entity-connections</c> / <c>update-entity-connections</c> response envelope.
	/// </summary>
	[DataContract]
	public sealed class EntityConnectionsResponse {
		[DataMember(Name = "success")]
		[JsonProperty("success")]
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[DataMember(Name = "error")]
		[JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("error")]
		public string Error { get; set; }

		[DataMember(Name = "changed")]
		[JsonProperty("changed", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("changed")]
		public bool? Changed { get; set; }

		[DataMember(Name = "entitySchemaName")]
		[JsonProperty("entitySchemaName", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("entitySchemaName")]
		public string EntitySchemaName { get; set; }

		[DataMember(Name = "entitySchemaUId")]
		[JsonProperty("entitySchemaUId", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("entitySchemaUId")]
		public string EntitySchemaUId { get; set; }

		[DataMember(Name = "packageName")]
		[JsonProperty("packageName", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("packageName")]
		public string PackageName { get; set; }

		[DataMember(Name = "connections")]
		[JsonProperty("connections", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("connections")]
		public List<EntityConnectionDto> Connections { get; set; }

		[DataMember(Name = "packageLayer")]
		[JsonProperty("packageLayer", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("packageLayer")]
		public EntityConnectionsPackageLayerDto PackageLayer { get; set; }

		[DataMember(Name = "warnings")]
		[JsonProperty("warnings", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("warnings")]
		public List<string> Warnings { get; set; }

		/// <summary>
		/// Whether an exception is an expected failure reported in the envelope: invalid input, a refused or failed
		/// operation, or a transport/serialization failure talking to Creatio. Anything else propagates.
		/// </summary>
		public static bool IsReportable(Exception exception) =>
			exception is ArgumentException or InvalidOperationException or System.Net.Http.HttpRequestException
				or System.Net.WebException or System.IO.IOException or System.Text.Json.JsonException
				or TimeoutException or System.Threading.Tasks.TaskCanceledException;

		/// <summary>Builds a failure envelope.</summary>
		public static EntityConnectionsResponse Fail(string error) => new() { Success = false, Error = error };

		/// <summary>Builds a success envelope from a service result.</summary>
		public static EntityConnectionsResponse From(EntityConnectionsReadResult result, bool? changed) => new() {
			Success = true,
			Changed = changed,
			EntitySchemaName = result.EntitySchemaName,
			EntitySchemaUId = result.EntitySchemaUId,
			PackageName = result.PackageName,
			Connections = result.Connections.Select(connection => new EntityConnectionDto {
				ColumnName = connection.ColumnName,
				ColumnUId = connection.ColumnUId,
				Caption = connection.Caption,
				ReferenceSchemaName = connection.ReferenceSchemaName,
				Position = connection.Position
			}).ToList(),
			PackageLayer = new EntityConnectionsPackageLayerDto {
				Items = result.PackageLayer.Items.ToList(),
				Removed = result.PackageLayer.Removed.ToList()
			},
			Warnings = result.Warnings.Count == 0 ? null : result.Warnings.ToList()
		};
	}

	/// <summary>One effective connection in an <see cref="EntityConnectionsResponse"/>.</summary>
	[DataContract]
	public sealed class EntityConnectionDto {
		[DataMember(Name = "columnName")]
		[JsonProperty("columnName", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("columnName")]
		public string ColumnName { get; set; }

		[DataMember(Name = "columnUId")]
		[JsonProperty("columnUId")]
		[JsonPropertyName("columnUId")]
		public string ColumnUId { get; set; }

		[DataMember(Name = "caption")]
		[JsonProperty("caption", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("caption")]
		public string Caption { get; set; }

		[DataMember(Name = "referenceSchemaName")]
		[JsonProperty("referenceSchemaName", NullValueHandling = NullValueHandling.Ignore)]
		[JsonPropertyName("referenceSchemaName")]
		public string ReferenceSchemaName { get; set; }

		[DataMember(Name = "position")]
		[JsonProperty("position")]
		[JsonPropertyName("position")]
		public int Position { get; set; }
	}

	/// <summary>The package's <c>EntityConnections</c> add-on content in an <see cref="EntityConnectionsResponse"/>.</summary>
	[DataContract]
	public sealed class EntityConnectionsPackageLayerDto {
		[DataMember(Name = "items")]
		[JsonProperty("items")]
		[JsonPropertyName("items")]
		public List<string> Items { get; set; }

		[DataMember(Name = "removed")]
		[JsonProperty("removed")]
		[JsonPropertyName("removed")]
		public List<string> Removed { get; set; }
	}
}
