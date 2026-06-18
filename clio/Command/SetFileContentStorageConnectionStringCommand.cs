using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("set-file-content-storage-connection-string", Aliases = ["set-fcs-connection-string"],
	HelpText = "Set the connection string of a file content storage in a running Creatio application")]
public class SetFileContentStorageConnectionStringOptions : RemoteCommandOptions {

	#region Properties: Public

	[Value(0, MetaName = "Code", Required = true, HelpText = "File content storage code")]
	public string Code { get; set; }

	[Value(1, MetaName = "ConnectionString", Required = true, HelpText = "New connection string value")]
	public string ConnectionString { get; set; }

	#endregion

}

/// <summary>
/// Updates the connection string of a <c>SysFileContentStorage</c> record using the Creatio DataService.
/// The change is applied through the ORM layer, which encrypts the value transparently and triggers
/// entity events — invalidating the file storage configuration cache without an application restart.
/// </summary>
public class SetFileContentStorageConnectionStringCommand
	: RemoteCommand<SetFileContentStorageConnectionStringOptions> {

	#region Fields: Private

	private readonly IServiceUrlBuilder _serviceUrlBuilder;

	#endregion

	#region Constructors: Public

	public SetFileContentStorageConnectionStringCommand(IApplicationClient applicationClient,
			EnvironmentSettings settings, IServiceUrlBuilder serviceUrlBuilder)
		: base(applicationClient, settings) {
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	#endregion

	#region Methods: Private

	private static string BuildUpdateQueryBody(string code, string connectionString) {
		return JsonSerializer.Serialize(new {
			rootSchemaName = "SysFileContentStorage",
			operationType = 3,
			filters = new {
				filterType = 6,
				logicalOperation = 0,
				isEnabled = true,
				items = new Dictionary<string, object> {
					["codeFilter"] = new {
						filterType = 1,
						comparisonType = 3,
						isEnabled = true,
						trimDateTimeParameterToDate = false,
						leftExpression = new { expressionType = 0, columnPath = "Code" },
						rightExpression = new {
							expressionType = 2,
							parameter = new { value = code, dataValueType = 1 }
						}
					}
				}
			},
			columnValues = new {
				items = new Dictionary<string, object> {
					["ConnectionString"] = new {
						expressionType = 2,
						parameter = new { value = connectionString, dataValueType = 1 }
					}
				}
			}
		});
	}

	private int HandleResponse(string response, string code) {
		if (string.IsNullOrWhiteSpace(response)) {
			Logger.WriteError($"No response received when updating file content storage '{code}'.");
			CommandSuccess = false;
			return 1;
		}
		var parsed = JsonSerializer.Deserialize<UpdateQueryResponse>(response,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
		if (parsed is { Success: false }) {
			CommandSuccess = false;
			Logger.WriteError(
				$"Failed to update connection string for '{code}': {parsed.ErrorInfo?.Message ?? "Unknown error"}");
			return 1;
		}
		if (parsed?.RowsAffected == 0) {
			CommandSuccess = false;
			Logger.WriteError($"File content storage with code '{code}' was not found.");
			return 1;
		}
		Logger.WriteInfo($"Connection string for file content storage '{code}' was updated successfully.");
		return 0;
	}

	#endregion

	#region Methods: Public

	public override int Execute(SetFileContentStorageConnectionStringOptions options) {
		string url = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Update);
		string body = BuildUpdateQueryBody(options.Code, options.ConnectionString);
		string response = ApplicationClient.ExecutePostRequest(url, body);
		return HandleResponse(response, options.Code);
	}

	#endregion

	#region Class: UpdateQueryResponse

	private sealed class UpdateQueryResponse {

		[JsonPropertyName("rowsAffected")]
		public int RowsAffected { get; set; }

		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("errorInfo")]
		public ErrorInfoDto ErrorInfo { get; set; }

	}

	private sealed class ErrorInfoDto {

		[JsonPropertyName("message")]
		public string Message { get; set; }

	}

	#endregion

}
