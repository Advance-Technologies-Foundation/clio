using Clio.Common;
using CommandLine;
using System;

namespace Clio.Command
{
    [Verb("last-compilation-log", Aliases = ["lcl"], HelpText = "Get last compilation log")]
    public class LastCompilationLogOptions : RemoteCommandOptions { }

    public class LastCompilationLogCommand : RemoteCommand<LastCompilationLogOptions>
    {
        public LastCompilationLogCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
            : base(applicationClient, settings){
            EnvironmentSettings = settings;
        }

        public override int Execute(LastCompilationLogOptions opts)
        {
            try
            {
                ServicePath = "/api/ConfigurationStatus/GetLastCompilationResult";
                string result = ApplicationClient.ExecuteGetRequest(ServiceUri);
                Logger.WriteLine(result);
                return 0;
            }
            catch (Exception e)
            {
                Logger.WriteError(e.Message);
                return 1;
            }
        }
    }
}
