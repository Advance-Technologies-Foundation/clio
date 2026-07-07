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
	[Description(McpToolDescriptions.EnvironmentName)]
	public string? EnvironmentName { get; init; }

	[JsonPropertyName("uri")]
	[Description(McpToolDescriptions.Uri)]
	public string? Uri { get; init; }

	[JsonPropertyName("login")]
	[Description(McpToolDescriptions.Login)]
	public string? Login { get; init; }

	[JsonPropertyName("password")]
	[Description(McpToolDescriptions.Password)]
	public string? Password { get; init; }

	[JsonPropertyName("resources")]
	[Description("JSON object string of explicit localizable string key-value pairs for page labels, captions, titles, validator messages, and #ResourceString(key)# macros.")]
	public string? Resources { get; init; }

	[JsonPropertyName("skip-sampling")]
	[Description("If true, skip AI semantic review before saving. Default: false")]
	public bool? SkipSampling { get; init; }
}
