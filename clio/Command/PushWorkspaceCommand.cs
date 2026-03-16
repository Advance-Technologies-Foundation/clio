using System;
using System.Text.Json;
using Clio.Command.StartProcess;
using Clio.Command.TIDE;
using Clio.Common;
using Clio.Workspaces;
using CommandLine;

namespace Clio.Command;

#region Class: PushWorkspaceCommandOptions

[Verb("push-workspace", Aliases = ["pushw"], HelpText = "Push workspace to selected environment")]
public class PushWorkspaceCommandOptions : EnvironmentOptions{
	#region Properties: Public

	[Option("unlock", Required = false,
		HelpText = "Unlock workspace package after install workspace to the environment")]
	public bool NeedUnlockPackage { get; set; }

	[Option("TideRepositoryId", Required = false, HelpText = "Update TIde repository by id", Hidden = true)]
	public string TideRepositoryId { get; set; }

	[Option("use-application-installer", Required = false,
		HelpText = "Use ApplicationInstaller instead of PackageInstaller for installation")]
	public bool UseApplicationInstaller { get; set; }

	#endregion
}

#endregion

#region Class: PushWorkspaceCommand

public class PushWorkspaceCommand : Command<PushWorkspaceCommandOptions>{
	#region Fields: Private

	private readonly EnvironmentSettings _environmentSettings;
	private readonly LinkWorkspaceWithTideRepositoryCommand _linkWorkspaceWithTideRepositoryCommand;
	private readonly ILogger _logger;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;

	private readonly IWorkspace _workspace;
	private readonly UnlockPackageCommand _unlockPackageCommand;

	#endregion

	#region Constructors: Public

	public PushWorkspaceCommand(IWorkspace workspace, UnlockPackageCommand unlockPackageCommand,
		IApplicationClientFactory applicationClientFactory, EnvironmentSettings environmentSettings,
		IServiceUrlBuilder serviceUrlBuilder, ILogger logger,
		LinkWorkspaceWithTideRepositoryCommand linkWorkspaceWithTideRepositoryCommand) {
		workspace.CheckArgumentNull(nameof(workspace));
		_workspace = workspace;
		_unlockPackageCommand = unlockPackageCommand;
		_applicationClientFactory = applicationClientFactory;
		_environmentSettings = environmentSettings;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
		_linkWorkspaceWithTideRepositoryCommand = linkWorkspaceWithTideRepositoryCommand;
	}

	#endregion

	private readonly IApplicationClientFactory _applicationClientFactory;

	#region Methods: Private

	private void CallbackInfo(string callbackProcess, string message) {
		if (string.IsNullOrEmpty(callbackProcess)) {
			return;
		}

		IApplicationClient applicationClient = _applicationClientFactory.CreateClient(_environmentSettings);
		string runProcessUri = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RunProcess);
		ProcessStartArgs runProcessArgs = new() {
			SchemaName = callbackProcess,
			Values = [
				new ProcessStartArgs.ParameterValues {
					Name = "Message",
					Value = message
				},
				new ProcessStartArgs.ParameterValues {
					Name = "Title",
					Value = "CLIO"
				}
			]
		};
		_logger.WriteInfo($"Run callback process {callbackProcess}");
		string processRunResponseJson
			= applicationClient.ExecutePostRequest(runProcessUri, JsonSerializer.Serialize(runProcessArgs));
		ProcessStartResponse response = JsonSerializer.Deserialize<ProcessStartResponse>(processRunResponseJson);
		_logger.WriteInfo($"Run process id {response.ProcessId}");
	}

	#endregion

	#region Methods: Public

		public override int Execute(PushWorkspaceCommandOptions options) {
		try {
			_logger.WriteInfo("Push workspace...");
			CallbackInfo(options.CallbackProcess, "Push workspace...");
			_workspace.Install(useApplicationInstaller: options.UseApplicationInstaller);

			if (!string.IsNullOrEmpty(options.TideRepositoryId)) {
				try {
					LinkWorkspaceWithTideRepositoryOptions opt = new() {
						TideRepositoryId = options.TideRepositoryId
					};
					opt.CopyFromEnvironmentSettings(options);
					_linkWorkspaceWithTideRepositoryCommand.Execute(opt);
					CallbackInfo(options.CallbackProcess, "Application linked with repository");
				}
				catch { }
			}

			if (options.NeedUnlockPackage) {
				UnlockPackageOptions unlockPackageCommandOptions = new();
				unlockPackageCommandOptions.CopyFromEnvironmentSettings(options);
				unlockPackageCommandOptions.Name = string.Join(',', _workspace.GetFilteredPackages());
				_logger.WriteInfo("Unlock packages...");
				CallbackInfo(options.CallbackProcess, "Unlock packages...");
				_unlockPackageCommand.Execute(unlockPackageCommandOptions);
			}

			CallbackInfo(options.CallbackProcess, "Workspace was successfully restored");
			_logger.WriteInfo("Done");
			return 0;
		}
		catch (Exception e) {
			_logger.WriteError(e.Message);
			CallbackInfo(options.CallbackProcess, e.Message);
			return 1;
		}
	}

	#endregion
}

#endregion
