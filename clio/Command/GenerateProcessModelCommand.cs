using Clio.Command.ProcessModel;
using Clio.Common;
using CommandLine;
using ErrorOr;

namespace Clio.Command;

/// <summary>
/// Options for generating a strongly typed ATF process model from a Creatio process schema.
/// </summary>
[Verb("generate-process-model", Aliases = ["gpm"], HelpText = "Generate process model for ATF.Repository")]
public class GenerateProcessModelCommandOptions : EnvironmentOptions{
	#region Properties: Public

	/// <summary>
	/// Process code as it appears in the Creatio process designer.
	/// </summary>
	[Value(0, MetaName = "Code", Required = true, HelpText = "Process code as it appears in the process designer")]
	public string Code { get; set; } = string.Empty;


	/// <summary>
	/// Relative or absolute destination path used by the current command writer.
	/// </summary>
	[Option('d', "DestinationPath", Required = false,
		HelpText = "Destination folder or explicit .cs file path for the generated process model.",
		Default = ".")]
	public string DestinationPath { get; set; } = ".";


	/// <summary>
	/// Namespace for generated process model classes.
	/// </summary>
	[Option('n', "Namespace", Required = false, HelpText = "Namespace for generated process model classes.",
		Default = "AtfTIDE.ProcessModels")]
	public string Namespace { get; set; } = "AtfTIDE.ProcessModels";


	/// <summary>
	/// Culture name used to select localized process captions and descriptions.
	/// </summary>
	[Option('x', "Culture", Required = false, HelpText = "Description culture", Default = "en-US")]
	public string Culture { get; set; } = "en-US";

	#endregion
}

/// <summary>
/// Generates a C# process model file for an existing Creatio process.
/// </summary>
public class GenerateProcessModelCommand(IProcessModelGenerator generator,
	ILogger logger, IProcessModelWriter processModelWriter) : Command<GenerateProcessModelCommandOptions>{
	#region Methods: Public

	/// <summary>
	/// Generates and writes the requested process model file.
	/// </summary>
	/// <param name="options">Command options that identify the source process and output settings.</param>
	/// <returns><c>0</c> on success; otherwise <c>1</c>.</returns>
	public override int Execute(GenerateProcessModelCommandOptions options) {
		ErrorOr<ProcessModel.ProcessModel> resultOrError = generator.Generate(options);
		if (resultOrError.IsError) {
			resultOrError.Errors.ForEach(error => { logger.WriteError($"{error.Code} - {error.Description}"); });
			return 1;
		}

		processModelWriter.WriteFileFromModel(resultOrError.Value, options.Namespace, options.DestinationPath,
			options.Culture);
		return 0;
	}

	#endregion
}
