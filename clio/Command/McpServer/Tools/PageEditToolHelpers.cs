using System;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Clio.Command.McpServer.Tools;

internal static class PageEditToolHelpers {
	internal static (PageUpdateResponse? response, string? resolveError) TryExecuteSaveBody(
		IToolCommandResolver commandResolver,
		string schemaName, string editedBody,
		string? environmentName, string? uri, string? login, string? password, string? resources) {
		PageUpdateResponse saveResponse;
		lock (McpToolExecutionLock.SyncRoot) {
			var updateOptions = new PageUpdateOptions {
				SchemaName = schemaName,
				Body = editedBody,
				Environment = environmentName,
				Uri = uri,
				Login = login,
				Password = password,
				Resources = resources
			};
			PageUpdateCommand updateCommand;
			try {
				updateCommand = commandResolver.Resolve<PageUpdateCommand>(updateOptions);
			} catch (Exception ex) {
				return (null, ex.Message);
			}
			updateCommand.TryUpdatePage(updateOptions, out saveResponse);
		}
		return (saveResponse, null);
	}
}

public abstract record PageEditToolArgs {
	[JsonPropertyName("environment-name")]
	[Description("Registered clio environment name, e.g. 'local'. Preferred for normal MCP work.")]
	public string? EnvironmentName { get; init; }

	[JsonPropertyName("uri")]
	[Description("Direct Creatio URL. Emergency fallback only.")]
	public string? Uri { get; init; }

	[JsonPropertyName("login")]
	[Description("Direct Creatio login paired with uri. Emergency fallback only.")]
	public string? Login { get; init; }

	[JsonPropertyName("password")]
	[Description("Direct Creatio password paired with uri. Emergency fallback only.")]
	public string? Password { get; init; }

	[JsonPropertyName("resources")]
	[Description("JSON object string of resource key-value pairs for #ResourceString(key)# macros in the field labels")]
	public string? Resources { get; init; }

	[JsonPropertyName("skip-sampling")]
	[Description("If true, skip AI semantic review before saving. Default: false")]
	public bool? SkipSampling { get; init; }
}
