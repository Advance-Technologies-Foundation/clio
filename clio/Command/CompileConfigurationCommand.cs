using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Common;
using Clio.CreatioModel;
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
	private readonly IDataProvider _dataProvider;
	private readonly ILogger _logger;

	private const string OdataProjName = "Terrasoft.Configuration.ODataEntities.csproj";
	private const string DevProjName = "Terrasoft.Configuration.Dev.csproj";
	private bool _compileAll;

	private bool _isSuccess = false;
	#region Constructors: Public

	public CompileConfigurationCommand(IApplicationClient applicationClient, 
		EnvironmentSettings settings, IServiceUrlBuilder serviceUrlBuilder, IDataProvider dataProvider, ILogger logger)
		: base(applicationClient, settings) {
		_serviceUrlBuilder = serviceUrlBuilder;
		_dataProvider = dataProvider;
		_logger = logger;
	}

	#endregion

	protected override string ServicePath => _compileAll 
		? _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.CompileAll) 
		: _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Compile);


	public override int Execute(CompileConfigurationOptions options) {

		IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(_dataProvider);
		CompilationHistory lastRecord = ctx.Models<CompilationHistory>()
										   .OrderByDescending(x => x.CreatedOn).Take(1).FirstOrDefault();
		
		// This is the first record, we don't need to print it. We will print records created after this record.
		if (lastRecord is not null) {
			_printed.Add(lastRecord.Id);
		}
		_compileAll = options.All;
		options.TimeOut = Timeout.Infinite;
		Stopwatch sw = new ();
		sw.Start();
		_logger.WriteLine("=================================================================================");
		_logger.WriteInfo($"At: {DateTime.Now:HH:mm:ss} Starting compilation...");
		_logger.WriteLine();
		
		using CancellationTokenSource cts = new ();
		Thread thread = new (()=> {
			GetLatestCompilationHistory(lastRecord?.CreatedOn ?? DateTime.MinValue, cts.Token);
		});
		thread.Start();
		
		//This will take a while to return, so we will check compilation history in parallel to get progress of compilation
		int execResult = base.Execute(options);
		sw.Stop();
		cts.Cancel();
		if (CommandSuccess) {
			_logger.WriteLine();
			_logger.WriteInfo($"Compilation finished in {TimeOnly.FromTimeSpan(sw.Elapsed):HH:mm:ss}");
			_logger.WriteLine("=================================================================================");
		}
		return _isSuccess ? execResult : 1;
	}


	private readonly ConcurrentBag<Guid> _printed = [];
	private void GetLatestCompilationHistory(DateTime lastRecord, CancellationToken cancellationToken) {
		IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(_dataProvider);
		while (!cancellationToken.IsCancellationRequested) {
			List<CompilationHistory> records = ctx.Models<CompilationHistory>()
												  .OrderByDescending(x => x.CreatedOn)
												  .Where(x => x.CreatedOn > lastRecord)
												  .ToList();
			
			records.ForEach(x => {
				if (_printed.Contains(x.Id)) {
					return;
				}
				_printed.Add(x.Id);

				string decoratedDuration = x.DurationInSeconds switch {
											   >= 10 => ConsoleLogger.WrapRed(x.DurationInSeconds),
											   >= 5 => ConsoleLogger.WrapYellow(x.DurationInSeconds),
											   var _ => x.DurationInSeconds.ToString("N0", CultureInfo.InvariantCulture)
										   };	
				List<string> specialProj = [OdataProjName, DevProjName];
				string decoratedProjectName = x.ProjectName switch {
												  {} y when specialProj.Contains(y) => ConsoleLogger.WrapBlue(y) + ConsoleLogger.WrapGreen(" <============"),
												  var _ => x.ProjectName
											  };
				if (string.Equals(x.ErrorsWarnings,"[]", StringComparison.OrdinalIgnoreCase)) {
					_logger.WriteInfo($"At: {x.CreatedOn:HH:mm:ss} after: {decoratedDuration} sec. {decoratedProjectName}");
				}
				else {
					_logger.WriteWarning($"At: {x.CreatedOn:HH:mm:ss} after: {decoratedDuration} sec. {decoratedProjectName} with: {ParseErrors(x.ErrorsWarnings)}");
				}
			});
			lastRecord = records.Count > 0 ? records.Max(x => x.CreatedOn) : lastRecord;
			if (cancellationToken.WaitHandle.WaitOne(1_000)) {
				break;
			}
		}
	}

	private static readonly JsonSerializerOptions JsonSerializerOptions = new ()
		{ PropertyNameCaseInsensitive = true };
	private static readonly Func<string, string> ParseErrors = (json) => {
		try {
			List<CompError> errors = JsonSerializer.Deserialize<List<CompError>>(json, JsonSerializerOptions);
			StringBuilder sb = new ();
			int errorNumber = 1;
			foreach (string message in errors.Select(error => error switch {
																  var _ when string.IsNullOrWhiteSpace(error.FileName) && error.IsWarning => $"({ConsoleLogger.WrapYellow(error.ErrorNumber)}): {error.ErrorText}",
																  var _ when string.IsNullOrWhiteSpace(error.FileName) && !error.IsWarning => $"({ConsoleLogger.WrapRed(error.ErrorNumber)}): {error.ErrorText}",
																  var _ when !string.IsNullOrWhiteSpace(error.FileName) && error.IsWarning => $"({ConsoleLogger.WrapYellow(error.ErrorNumber)}) in {ConsoleLogger.WrapYellow(error.FileName)} at ({error.Line},{error.Column}): {error.ErrorText}",
																  var _ when !string.IsNullOrWhiteSpace(error.FileName) && !error.IsWarning => $"({ConsoleLogger.WrapRed(error.ErrorNumber)}) in {ConsoleLogger.WrapYellow(error.FileName)} at ({error.Line},{error.Column}) : {error.ErrorText}",
																  var _ => json //We should never be here, this is to make compiler happy
															  })) {
				sb.AppendLine().Append('\t').Append($"{errorNumber++} of {errors.Count} ").Append(message);
			}
			return sb.ToString();
			
		}
		// Could not parse errors, return original json
		catch {
			return json;
		}
	};

	protected override void ProceedResponse(string response, CompileConfigurationOptions options) {
		base.ProceedResponse(response, options);
		try {
			if (string.IsNullOrWhiteSpace(response)) {
				CommandSuccess = _isSuccess = false;
				Logger.WriteError("Empty response received from server during compilation.");
				Logger.WriteError($"Endpoint: {ServiceUri}");
				return;
			}

			string trimmed = response.TrimStart();
			if (trimmed.StartsWith("<", StringComparison.Ordinal)) {
				CommandSuccess = _isSuccess = false;
				Logger.WriteError("Server returned non-JSON response during compilation (looks like HTML).");
				Logger.WriteError($"Endpoint: {ServiceUri}");
				Logger.WriteError("Full response:");
				Logger.WriteLine(trimmed);
				Logger.WriteError("Check environment URI, IsNetCore flag, and credentials (a login/404 page is often returned as HTML).");
				return;
			}

			CreatioResponse model = JsonSerializer.Deserialize<CreatioResponse>(response);
			CommandSuccess = _isSuccess = model.Success;
			if (!model.Success) {
				Logger.WriteError($"{model.ErrorInfo.ErrorCode}: {model.ErrorInfo.Message}");
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

public class CompError
{
	[JsonPropertyName("Line")]
    public int Line { get; set; }
	
	[JsonPropertyName("Column")]
    public int Column { get; set; }
	
	[JsonPropertyName("ErrorNumber")]
    public string ErrorNumber { get; set; }
	
	[JsonPropertyName("ErrorText")]
    public string ErrorText { get; set; }
	
	[JsonPropertyName("IsWarning")]
    public bool IsWarning { get; set; }
	
	[JsonPropertyName("FileName")]
    public string FileName { get; set; }
}
