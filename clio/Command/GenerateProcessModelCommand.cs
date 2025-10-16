using Clio.Command.ProcessModel;
using CommandLine;
using Clio.Common;
using ErrorOr;

namespace Clio.Command;


[Verb("generate-process-model", Aliases = ["gpm"], HelpText = "Generate process model for ATF.Repository")]
public class GenerateProcessModelCommandOptions {

	[Value(0, MetaName = "Code", Required = true, HelpText = "Process code as it appears in the process designer")]
	public string Code { get; set; }
	
	
	[Option('d', "DestinationPath", Required = false, HelpText = "Path to source directory.", Default = ".")]
	public string DestinationPath { get; set; }
	
	
	[Option('n', "Namespace", Required = false, HelpText = "Name space for service classes.", Default = "AtfTIDE.ProcessModels")]
	public string Namespace { get; set; }
	
	
	[Option('x', "Culture", Required = false, HelpText = "Description culture", Default = "en-US")]
	public string Culture { get; set; }
}


public class GenerateProcessModelCommand: Command<GenerateProcessModelCommandOptions>{
	private readonly IProcessModelGenerator _generator;
	private readonly ILogger _logger;
	private readonly IProcessModelWriter _processModelWriter;


	public GenerateProcessModelCommand(IProcessModelGenerator generator, ILogger logger, IProcessModelWriter processModelWriter) {
		_generator = generator;
		_logger = logger;
		_processModelWriter = processModelWriter;
	}
	
	public override int Execute(GenerateProcessModelCommandOptions options) {

		ErrorOr<ProcessModel.ProcessModel> resultOrError = _generator.Generate(options);
		if (resultOrError.IsError) {
			resultOrError.Errors.ForEach(error => {
				_logger.WriteError($"{error.Code} - {error.Description}");
			});
			return 1;
		}
	
		_processModelWriter.WriteFileFromModel(resultOrError.Value, options.Namespace, options.DestinationPath, options.Culture);
		

		return 0;
	}
}
