using Clio.Common;
using CommandLine;

namespace Clio.Command;

#region Class: GetAppHashCommandOptions

[Verb("get-app-hash", Aliases = [], HelpText = "Calculate hash for the app")]
public class GetAppHashCommandOptions {

	[Value(0, MetaName = "Directory", Required = false, HelpText = "Directory to calculate hash for")]
	public string Directory { get; set; }

}

#endregion

#region Class: GetAppHashCommand

public class GetAppHashCommand : Command<GetAppHashCommandOptions> {

	private readonly ILogger _logger;
	private readonly IFileSystem _fileSystem;
	private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;

	#region Constructors: Public

	public GetAppHashCommand(ILogger logger, IFileSystem fileSystem, IWorkingDirectoriesProvider workingDirectoriesProvider) {
		_logger = logger;
		_fileSystem = fileSystem;
		_workingDirectoriesProvider = workingDirectoriesProvider;
	}

	#endregion

	#region Methods: Public

	public override int Execute(GetAppHashCommandOptions options) {
		
		string directory  = _workingDirectoriesProvider.CurrentDirectory;
		if(!string.IsNullOrWhiteSpace(options.Directory) && _fileSystem.ExistsDirectory(options.Directory)) {
			directory = options.Directory;
		}
		
		string hash = _fileSystem.GetDirectoryHash(directory);
		_logger.Write(hash);
		return 0;
	}

	#endregion

}

#endregion

