using System.IO;
using System.Text.Json;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("execute-assembly-code",
    Aliases = new[]
    {
        "exec"
    },
    HelpText = "Execute an assembly code which implements the IExecutor interface",
    Hidden = true)]
public class ExecuteAssemblyOptions : RemoteCommandOptions
{

    #region Properties: Public

    [Option('t', "ExecutorType", Required = true, HelpText = "Assembly type name for proceed")]
    public string ExecutorType { get; set; }

    [Value(0, MetaName = "Name", Required = true, HelpText = "Path to executed assembly")]
    public string Name { get; set; }

    [Option("WriteResponse", Required = false, HelpText = "")]
    public bool WriteResponse { get; set; }

    #endregion

}

internal class ExecuteScriptRequest
{

    #region Properties: Public

    public byte[] Body { get; set; }

    public string LibraryType { get; set; }

    #endregion

}

internal class AssemblyCommand : RemoteCommand<ExecuteAssemblyOptions>
{

    #region Constructors: Public

    public AssemblyCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
        : base(applicationClient, settings)
    { }

    #endregion

    #region Properties: Protected

    protected override string ServicePath => @"/IDE/ExecuteScript";

    #endregion

    #region Methods: Protected

    protected override string GetRequestData(ExecuteAssemblyOptions options)
    {
        string filePath = options.Name;
        string executorType = options.ExecutorType;
        byte[] fileContent = File.ReadAllBytes(filePath);
        return JsonSerializer.Serialize(new ExecuteScriptRequest
        {
            Body = fileContent, LibraryType = executorType
        });
    }

    protected override void ProceedResponse(string response, ExecuteAssemblyOptions options)
    {
        base.ProceedResponse(response, options);
        if (options.WriteResponse)
        {
            Logger.WriteInfo(response);
        }
    }

    #endregion

}
