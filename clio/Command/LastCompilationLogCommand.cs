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

public class LastCompilationLogCommand : RemoteCommand<LastCompilationLogOptions> {

	#region Fields: Private

	private readonly ICompilationLogParser _compilationLogParser;

	#endregion

	#region Constructors: Public

	public LastCompilationLogCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		ICompilationLogParser compilationLogParser)
		: base(applicationClient, settings){
		_compilationLogParser = compilationLogParser;
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
			ServicePath = "/api/ConfigurationStatus/GetLastCompilationResult";
			string result = ApplicationClient.ExecuteGetRequest(ServiceUri);
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

	#endregion

}