using System;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("last-compilation-log", Aliases = ["lcl"], HelpText = "Get last compilation log")]
public class LastCompilationLogOptions : RemoteCommandOptions {

	#region Properties: Public

	[Option("raw", Required = false, HelpText = "Display raw output (json)", Default = false)]
	public bool IsRaw { get; set; }

	#endregion

}

/// <summary>
/// Retrieves and displays Creatio's most recently persisted compilation result.
/// </summary>
public class LastCompilationLogCommand : RemoteCommand<LastCompilationLogOptions> {

	#region Fields: Private

	private readonly ICompilationLogParser _compilationLogParser;
	private readonly ICompilationResultReader _compilationResultReader;

	#endregion

	#region Constructors: Public

	public LastCompilationLogCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		ICompilationLogParser compilationLogParser, ICompilationResultReader compilationResultReader)
		: base(applicationClient, settings){
		_compilationLogParser = compilationLogParser;
		_compilationResultReader = compilationResultReader;
		EnvironmentSettings = settings;
	}

	#endregion

	#region Methods: Public

	/// <summary>
	/// Executes the command to get the last compilation log.
	/// </summary>
	/// <param name="opts">Options for the command execution.</param>
	/// <returns>Returns 0 if successful, otherwise returns 1.</returns>
	public override int Execute(LastCompilationLogOptions opts){
		try {
			string result = GetLastCompilationResultJson();
			if (opts.IsRaw) {
				Logger.WriteLine(result);
			} else {
				string output = _compilationLogParser.ParseCreatioCompilationLog(result);
				Logger.WriteLine(output);
			}
			return 0;
		} catch (Exception e) {
			Logger.WriteError(e.Message);
			return 1;
		}
	}

	/// <summary>
	/// Retrieves Creatio's most recently persisted compilation result as structured data.
	/// </summary>
	/// <returns>The typed result returned by Creatio.</returns>
	public CreatioCompilationLogResponse GetLastCompilationResult(){
		return _compilationLogParser.DeserializeCreatioCompilationLog(GetLastCompilationResultJson());
	}

	#endregion

	#region Methods: Private

	// The endpoint moved into ICompilationResultReader when CompileConfigurationCommand became its second
	// caller: a configuration build reads its verdict from here whenever the compile request produced no
	// response, which on current platforms is the normal case.
	private string GetLastCompilationResultJson() => _compilationResultReader.ReadRaw();

	#endregion

}
