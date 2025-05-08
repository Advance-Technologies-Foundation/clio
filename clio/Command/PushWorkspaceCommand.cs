using System;
using System.Text.Json;
using Clio.Command.TIDE;
using CommandLine;

namespace Clio.Command;

[Verb("push-workspace", Aliases = new[] { "pushw" }, HelpText = "Push workspace to selected environment")]
public class PushWorkspaceCommandOptions : EnvironmentOptions
{
    [Option("unlock", Required = false,
        HelpText = "Unlock workspace package after install workspace to the environment")]
    public bool NeedUnlockPackage { get; set; }

    [Option("TideRepositoryId", Required = false, HelpText = "Update TIde repository by id", Hidden = true)]
    public string TideRepositoryId { get; set; }
}

public class PushWorkspaceCommand : Command<PushWorkspaceCommandOptions>
{
    private readonly EnvironmentSettings _environmentSettings;
    private readonly LinkWorkspaceWithTideRepositoryCommand _linkWorkspaceWithTideRepositoryCommand;
    private readonly IServiceUrlBuilder _serviceUrlBuilder;
    private readonly UnlockPackageCommand _unlockPackageCommand;
    private readonly IWorkspace _workspace;
    public IApplicationClientFactory _applicationClientFactory;

    public PushWorkspaceCommand(IWorkspace workspace, UnlockPackageCommand unlockPackageCommand,
        IApplicationClientFactory applicationClientFactory, EnvironmentSettings environmentSettings,
        IServiceUrlBuilder serviceUrlBuilder,
        LinkWorkspaceWithTideRepositoryCommand linkWorkspaceWithTideRepositoryCommand)
    {
        workspace.CheckArgumentNull(nameof(workspace));
        _workspace = workspace;
        _unlockPackageCommand = unlockPackageCommand;
        _applicationClientFactory = applicationClientFactory;
        _environmentSettings = environmentSettings;
        _serviceUrlBuilder = serviceUrlBuilder;
        _linkWorkspaceWithTideRepositoryCommand = linkWorkspaceWithTideRepositoryCommand;
    }

    public override int Execute(PushWorkspaceCommandOptions options)
    {
        try
        {
            Console.WriteLine("Push workspace...");
            CallbackInfo(options.CallbackProcess, "Push workspace...");
            _workspace.Install();

            if (!string.IsNullOrEmpty(options.TideRepositoryId))
            {
                try
                {
                    LinkWorkspaceWithTideRepositoryOptions opt = new() { TideRepositoryId = options.TideRepositoryId };
                    opt.CopyFromEnvironmentSettings(options);
                    _linkWorkspaceWithTideRepositoryCommand.Execute(opt);
                    CallbackInfo(options.CallbackProcess, "Application linked with repository");
                }
                catch
                {
                }
            }

            if (options.NeedUnlockPackage)
            {
                UnlockPackageOptions unlockPackageCommandOptions = new();
                unlockPackageCommandOptions.CopyFromEnvironmentSettings(options);
                unlockPackageCommandOptions.Name = string.Join(',', _workspace.WorkspaceSettings.Packages);
                Console.WriteLine("Unlock packages...");
                CallbackInfo(options.CallbackProcess, "Unlock packages...");
                _unlockPackageCommand.Execute(unlockPackageCommandOptions);
            }

            CallbackInfo(options.CallbackProcess, "Workspace was successfully restored");
            Console.WriteLine("Done");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            CallbackInfo(options.CallbackProcess, e.Message);
            return 1;
        }
    }

    private void CallbackInfo(string callbackProcess, string message)
    {
        if (!string.IsNullOrEmpty(callbackProcess))
        {
            IApplicationClient applicationClient = _applicationClientFactory.CreateClient(_environmentSettings);
            string runProcessUri = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.RunProcess);
            ProcessStartArgs runProcessArgs = new()
            {
                SchemaName = callbackProcess,
                Values =
                [
                    new ProcessStartArgs.ParameterValues { Name = "Message", Value = message },
                    new ProcessStartArgs.ParameterValues { Name = "Title", Value = "CLIO" }
                ]
            };
            Console.WriteLine($"Run callback process {callbackProcess}");
            string processRunResponseJson =
                applicationClient.ExecutePostRequest(runProcessUri, JsonSerializer.Serialize(runProcessArgs));
            ProcessStartResponse? response = JsonSerializer.Deserialize<ProcessStartResponse>(processRunResponseJson);
            Console.WriteLine($"Run process id {response.ProcessId}");
        }
    }
}
