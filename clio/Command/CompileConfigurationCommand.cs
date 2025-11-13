using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

#region Class: CompileConfigurationOptions

[Verb("compile-configuration", Aliases = ["cc","compile-remote"], HelpText = "Compile configuration")]
public class CompileConfigurationOptions : RemoteCommandOptions
{

	[Option("all", Required = false, HelpText = "Compile configuration all", Default = false)]
	public bool All {
		get; set;
	}
	protected override int DefaultTimeout => Timeout.Infinite;
		
}

#endregion

#region Interface: CompileConfigurationCommand

public interface ICompileConfigurationCommand {
	int Execute(CompileConfigurationOptions options);

}

#endregion

#region Class: CompileConfigurationCommand
	
public class CompileConfigurationCommand : RemoteCommand<CompileConfigurationOptions>, ICompileConfigurationCommand {
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
		
	private bool _compileAll;

	private bool _isSuccess = false;
	#region Constructors: Public

	public CompileConfigurationCommand(IApplicationClient applicationClient, EnvironmentSettings settings, IServiceUrlBuilder serviceUrlBuilder)
		: base(applicationClient, settings)
	{
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	#endregion

	protected override string ServicePath => _compileAll 
		? _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.CompileAll) 
		: _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Compile);


	public override int Execute(CompileConfigurationOptions options) {
		_compileAll = options.All;

		int execResult = base.Execute(options);
		
		return _isSuccess ? execResult : 1;
	}

	protected override void ProceedResponse(string response, CompileConfigurationOptions options) {
		base.ProceedResponse(response, options);
		try {
			var model = JsonSerializer.Deserialize<CreatioResponse>(response);
			_isSuccess = model.Success;
			if (!model.Success) {
				Logger.WriteError($"{model.ErrorInfo.ErrorCode}: {model.ErrorInfo.Message}");
			}
		}
		catch (Exception e) {
			Logger.WriteError(e.Message);
		}
		
	}
}

#endregion


public class CreatioResponse
{
	[JsonPropertyName("errorInfo")]
    public ErrorInfo ErrorInfo { get; set; }
	
	[JsonPropertyName("success")]
	public bool Success { get; set; }
	
	[JsonPropertyName("buildResult")]
    public int BuildResult { get; set; }
	
	[JsonPropertyName("errors")]
    public object Errors { get; set; }
	
	[JsonPropertyName("message")]
    public object Message { get; set; }
}

public class ErrorInfo
{
	[JsonPropertyName("errorCode")]
    public string ErrorCode { get; set; }
	
	[JsonPropertyName("message")]
    public string Message { get; set; }
	
	[JsonPropertyName("stackTrace")]
    public object StackTrace { get; set; }
}

