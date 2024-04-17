using clio.ApiTest.Common;
using FluentAssertions;

namespace clio.ApiTest.Steps;

[Binding]
public class PublishWorkspaceCommandStepDefinitions
{

	#region Fields: Private

	private readonly ICLioRunner _clio;
	private string _clioWorkingDirectory = string.Empty;
	private PublishAppArgs _args;
	private readonly List<string> _tempDirItems = new();
	private readonly List<string> _workingDirItems = new();
	private FileSystemWatcher _watcher;
	private FileSystemWatcher _tempWatcher;

	#endregion

	#region Constructors: Public

	public PublishWorkspaceCommandStepDefinitions(ICLioRunner clio){
		_clio = clio;
	}

	#endregion

	#region Methods: Private

	private static PublishAppArgs GetPublishAppArgs(Table table){
		Dictionary<string, string> tbl = TableExtensions.ToDictionary(table);
		return new PublishAppArgs(tbl["-h"], tbl["-r"], tbl["-v"], tbl["-a"]);
	}

	private void CreateDirIfNotExists(string path){
		if (!Directory.Exists(path)) {
			Directory.CreateDirectory(path);
		}
	}

	private void CreateDummyPackage(string packageName, string workSpacePath){
		_clio.RunClioCommand("add-package", packageName, workSpacePath);
	}

	private void CreateWorkspaceIfNotExists(string path, string name){
		if (!Directory.Exists(path)) {
			Directory.CreateDirectory(path);
		}
		_clio.RunClioCommand("createw", "", _args.WorkspaceFolderPath);
	}

	private void OnTempCreated(object sender, FileSystemEventArgs e){
		_tempDirItems.Add(e.FullPath);
	}

	private void OnWorkDirCreated(object sender, FileSystemEventArgs e){
		_workingDirItems.Add(e.FullPath);
	}

	#endregion

	#region Methods: Public

	[Given(@"I do not set env variable CLIO_WORKING_DIRECTORY")]
	public void GivenIDoNotSetEnvVariableClioWorkingDirectory(){
		
		string tempDir = Path.GetTempPath();
		string clioInTempDir = Path.Combine(tempDir,"clio");
		if(Directory.Exists(clioInTempDir)) {
			Directory.Delete(clioInTempDir, true);
		}

		_tempWatcher = new FileSystemWatcher {
			Path = tempDir
		};
		_tempWatcher.Created += OnTempCreated;
		_tempWatcher.EnableRaisingEvents = true;
	}

	[Given(@"I set env variable CLIO_WORKING_DIRECTORY to ""(.*)""")]
	public void GivenISetEnvVariableClioWorkingDirectoryTo(string clioWorkingDirectory){
		_clioWorkingDirectory = clioWorkingDirectory;
		CreateDirIfNotExists(_clioWorkingDirectory);
		CreateDirIfNotExists(Path.Combine(_clioWorkingDirectory, "clio"));

		_watcher = new FileSystemWatcher {
			Path = Path.Combine(_clioWorkingDirectory, "clio")
		};
		_watcher.Created += OnWorkDirCreated;
		_watcher.EnableRaisingEvents = true;

		_tempWatcher = new FileSystemWatcher {
			Path = Path.Combine(Path.GetTempPath(), "clio")
		};
		_tempWatcher.Created += OnTempCreated;
		_tempWatcher.EnableRaisingEvents = true;
	}

	[Then(@"I assert that clio uses CLIO_WORKING_DIRECTORY directory")]
	public void ThenClioUsesTempDirectory(){
		//Unsubscribe
		_watcher.Created -= OnWorkDirCreated;
		_tempWatcher.Created -= OnTempCreated;

		//Delete Dirs
		Directory.Delete(_args.WorkspaceFolderPath, true);
		Directory.Delete(_args.AppHubPath, true);
		Directory.Delete(_clioWorkingDirectory, true);

		_tempDirItems.Any(i => i.EndsWith("clio")).Should().BeFalse();

		//This is really unnecessary. We only want to assert that tempDir is empty
		_workingDirItems.Should().HaveCount(c => c > 0);
	}

	[Then(@"I assert that clio uses default directory")]
	public void ThenIAssertThatClioUsesDefaultDirectory(){
		string tempDir = Path.Combine(Path.GetTempPath(), "clio");
		_tempDirItems.Where(i => i.StartsWith(tempDir)).Should().HaveCount(c => c > 0);
	}

	[When(@"I execute publish-app with args:")]
	public void WhenIExecutePublishAppWithArgs(Table table){
		_args = GetPublishAppArgs(table);
		CreateDirIfNotExists(_args.WorkspaceFolderPath);
		CreateDirIfNotExists(_args.AppHubPath);
		CreateWorkspaceIfNotExists(_args.WorkspaceFolderPath, _args.AppName);
		CreateDummyPackage(_args.AppName, _args.WorkspaceFolderPath);

		string cmdArgs
			= $"-h {_args.AppHubPath} -r {_args.WorkspaceFolderPath} -v {_args.AppVersion} -a {_args.AppName}";
		_clio.RunClioCommand("publish-app", cmdArgs, _clioWorkingDirectory, new Dictionary<string, string> {
			{"CLIO_WORKING_DIRECTORY", _clioWorkingDirectory}
		});
	}

	#endregion

}

public record PublishAppArgs(string AppHubPath, string WorkspaceFolderPath, string AppVersion, string AppName);