using System;
using System.Text.Json;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

#region Class: GenerateSourceCodeOptions

[Verb("generate-source-code", Aliases = ["gsc"], HelpText = "Generate source code for schemas in Creatio")]
public class GenerateSourceCodeOptions : RemoteCommandOptions
{

	[Option('m', "modified", Required = false, Default = false,
		HelpText = "Generate source code only for modified schemas")]
	public bool Modified { get; set; }

	[Option('r', "required", Required = false, Default = false,
		HelpText = "Generate source code only for schemas that require it")]
	public bool Required { get; set; }

	[Option('b', "background", Required = false, Default = false,
		HelpText = "Run source code generation in background (matches UI behaviour for generate-all)")]
	public bool Background { get; set; }
	
	

}

#endregion

#region Class: GenerateSourceCodeCommand

public class GenerateSourceCodeCommand : RemoteCommand<GenerateSourceCodeOptions>
{

	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private bool _isSuccess;
	private GenerateSourceCodeOptions _options;

	public GenerateSourceCodeCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilder)
		: base(applicationClient, settings) {
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	protected override string ServicePath => _options switch {
		{ Background: true } => _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GenerateAllSchemasSourcesInBackground),
		{ Modified: true } => _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GenerateModifiedSchemasSources),
		{ Required: true } => _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GenerateRequiredSchemasSources),
		_ => _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GenerateAllSchemasSources)
	};

	protected override string GetRequestData(GenerateSourceCodeOptions options) => string.Empty;

	public override int Execute(GenerateSourceCodeOptions options) {
		_options = options;
		Logger.WriteWarning("This command may take a while to complete, please wait...");
		Logger.WriteWarning("Clio will timeout if the operation takes more than 60 minutes. You can adjust the timeout using --timeout option.");
		return base.Execute(options);
	}

	protected override void ProceedResponse(string response, GenerateSourceCodeOptions options) {
		base.ProceedResponse(response, options);
		try {
			if (string.IsNullOrWhiteSpace(response)) {
				CommandSuccess = _isSuccess = false;
				Logger.WriteError("Empty response received from server.");
				Logger.WriteError($"Endpoint: {ServiceUri}");
				return;
			}
			string trimmed = response.TrimStart();
			if (trimmed.StartsWith("<", StringComparison.Ordinal)) {
				CommandSuccess = _isSuccess = false;
				Logger.WriteError("Server returned non-JSON response (looks like HTML).");
				Logger.WriteError($"Endpoint: {ServiceUri}");
				return;
			}
			CreatioResponse model = JsonSerializer.Deserialize<CreatioResponse>(response);
			CommandSuccess = _isSuccess = model.Success;
			if (!model.Success) {
				Logger.WriteError($"{model.ErrorInfo?.ErrorCode}: {model.ErrorInfo?.Message}");
			}
		}
		catch (Exception e) {
			CommandSuccess = _isSuccess = false;
			Logger.WriteError(e.Message);
			Logger.WriteError($"Endpoint: {ServiceUri}");
			if (!string.IsNullOrWhiteSpace(response)) {
				Logger.WriteError("Full response:");
				Logger.WriteLine(response);
			}
		}
	}

}

#endregion
