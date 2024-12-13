namespace Clio.Command
{
	using System;
	using System.Text.Json;
	using Clio.Command.StartProcess;
	using Clio.Common;
	using Clio.Workspaces;
	using CommandLine;

	#region Class: PushWorkspaceCommandOptions

	[Verb("push-workspace", Aliases = new string[] { "pushw" }, HelpText = "Push workspace to selected environment")]
	public class PushWorkspaceCommandOptions : EnvironmentOptions
	{
		[Option("unlock", Required = false, HelpText = "Unlock workspace package after install workspace to the environment")]
		public bool NeedUnlockPackage { get; set; }
	}

	#endregion

	#region Class: PushWorkspaceCommand

	public class PushWorkspaceCommand : Command<PushWorkspaceCommandOptions>
	{

		#region Fields: Private

		private readonly IWorkspace _workspace;
		private UnlockPackageCommand _unlockPackageCommand;
		public IApplicationClientFactory _applicationClientFactory;
		private readonly EnvironmentSettings _environmentSettings;
		private readonly IServiceUrlBuilder _serviceUrlBuilder;

		#endregion

		#region Constructors: Public

		public PushWorkspaceCommand(IWorkspace workspace, UnlockPackageCommand unlockPackageCommand,
			IApplicationClientFactory applicationClientFactory, EnvironmentSettings environmentSettings,
			IServiceUrlBuilder serviceUrlBuilder) {
			workspace.CheckArgumentNull(nameof(workspace));
			_workspace = workspace;
			_unlockPackageCommand = unlockPackageCommand;
			_applicationClientFactory = applicationClientFactory;
			_environmentSettings = environmentSettings;
			_serviceUrlBuilder = serviceUrlBuilder;
		}


		#endregion

		#region Methods: Public

		public override int Execute(PushWorkspaceCommandOptions options) {
			try {
				Console.WriteLine("Push workspace...");
				CallbackInfo(options.CallbackProcess, "Push workspace...");
				_workspace.Install();
				if (options.NeedUnlockPackage) {
					var unlockPackageCommandOptions = new UnlockPackageOptions();
					unlockPackageCommandOptions.CopyFromEnvironmentSettings(options);
					unlockPackageCommandOptions.Name = string.Join(',', _workspace.WorkspaceSettings.Packages);
					Console.WriteLine("Unlock packages...");
					CallbackInfo(options.CallbackProcess, "Unlock packages...");
					_unlockPackageCommand.Execute(unlockPackageCommandOptions);
				}
				Console.WriteLine("Done");
				CallbackInfo(options.CallbackProcess, "Workspace was suvvesfully restored");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				CallbackInfo(options.CallbackProcess, e.Message);
				return 1;
			}
		}

		private void CallbackInfo(string callbackProcess, string message) {
			if (!string.IsNullOrEmpty(callbackProcess)) {
				var applicationClient = _applicationClientFactory.CreateClient(_environmentSettings);
				var runProcessUri = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RunProcess);
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
				Console.WriteLine($"Run callback process {callbackProcess}");
				var processRunResponseJson = applicationClient.ExecutePostRequest(runProcessUri, JsonSerializer.Serialize(runProcessArgs));
				var response = JsonSerializer.Deserialize<ProcessStartResponse>(processRunResponseJson);
				Console.WriteLine($"Run process id {response.ProcessId}");
			}
		}

		#endregion

	}

	#endregion

}