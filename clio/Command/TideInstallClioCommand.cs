using System.Text.Json;
using Clio.Command.StartProcess;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{
    #region Class: TideInstallClioCommandOptions

    [Verb("tide-install-clio", HelpText = "Install clio to the T.I.D.E. environment")]
    public class TideInstallClioCommandOptions : RemoteCommandOptions
    {
    }

    #endregion

    #region Class: TideInstallClioCommand

    public class TideInstallClioCommand : RemoteCommand<TideInstallClioCommandOptions>
    {
        #region Constructors: Public

        public TideInstallClioCommand(IApplicationClient applicationClient, EnvironmentSettings environmentSettings)
            : base(applicationClient, environmentSettings)
        {
            ServicePath = "/ServiceModel/ProcessEngineService.svc/RunProcess";
        }

        public TideInstallClioCommand(EnvironmentSettings environmentSettings)
            : base(environmentSettings)
        {
            ServicePath = "/ServiceModel/ProcessEngineService.svc/RunProcess";
        }

        public TideInstallClioCommand() : base()
        {
            ServicePath = "/ServiceModel/ProcessEngineService.svc/RunProcess";
        }

        #endregion

        #region Methods: Protected

        protected override string GetRequestData(TideInstallClioCommandOptions options)
        {
            var processArgs = new ProcessStartArgs
            {
                SchemaName = "AtfProcess_TryInstallClio"
            };
            return JsonSerializer.Serialize(processArgs);
        }

        protected override void ProceedResponse(string response, TideInstallClioCommandOptions options)
        {
            if (string.IsNullOrEmpty(response))
            {
                return;
            }

            try
            {
                var processResponse = JsonSerializer.Deserialize<ProcessStartResponse>(response);
                if (processResponse != null)
                {
                    Logger.WriteInfo($"Process started with ID: {processResponse.ProcessId}");
                    if (processResponse.Success)
                    {
                        Logger.WriteInfo("Clio installation process completed successfully");
                    }
                    else
                    {
                        Logger.WriteError($"Clio installation process failed: {processResponse.ErrorInfo}");
                    }
                }
            }
            catch (JsonException)
            {
                // Handle invalid JSON gracefully
                Logger.WriteError("Invalid response received from server");
            }
        }

        #endregion
    }

    #endregion
}