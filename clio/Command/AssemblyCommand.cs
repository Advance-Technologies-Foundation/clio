using System.IO;
using System.Text.Json;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb(
    "execute-assembly-code",
    Aliases = new[] { "exec" },
    HelpText = "Execute an assembly code which implements the IExecutor interface",
    Hidden = true)]
public class ExecuteAssemblyOptions : RemoteCommandOptions
{
    [Value(0, MetaName = "Name", Required = true, HelpText = "Path to executed assembly")]
    public string Name { get; set; }

    [Option('t', "ExecutorType", Required = true, HelpText = "Assembly type name for proceed")]
    public string ExecutorType { get; set; }

    [Option("WriteResponse", Required = false, HelpText = "")]
    public bool WriteResponse { get; set; }
}

internal class ExecuteScriptRequest
{
    public byte[] Body { get; set; }

    public string LibraryType { get; set; }
}

internal class AssemblyCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
    : RemoteCommand<ExecuteAssemblyOptions>(applicationClient, settings)
{
    protected override string ServicePath => @"/IDE/ExecuteScript";

    protected override string GetRequestData(ExecuteAssemblyOptions options)
    {
        string filePath = options.Name;
        string executorType = options.ExecutorType;
        byte[] fileContent = File.ReadAllBytes(filePath);
        return JsonSerializer.Serialize(new ExecuteScriptRequest { Body = fileContent, LibraryType = executorType });
    }

    protected override void ProceedResponse(string response, ExecuteAssemblyOptions options)
    {
        base.ProceedResponse(response, options);
        if (options.WriteResponse)
        {
            Logger.WriteInfo(response);
        }
    }
}
