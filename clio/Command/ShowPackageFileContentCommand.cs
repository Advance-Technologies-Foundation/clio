using System;
using System.Net.Http;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("show-package-file-content", Aliases = new[]
{
    "show-files", "files"
}, HelpText = "Show package file context")]
public class ShowPackageFileContentOptions : RemoteCommandOptions
{

    #region Properties: Public

    [Option("file", Required = false, HelpText = "file path")]
    public string FilePath { get; internal set; }

    [Option("package", Required = true, HelpText = "Package name")]
    public string PackageName { get; internal set; }

    #endregion

}

internal class ShowPackageFileContentCommand : RemoteCommand<ShowPackageFileContentOptions>
{

    #region Fields: Private

    private object _packageName;
    private string _filePath;

    #endregion

    #region Constructors: Public

    public ShowPackageFileContentCommand(IApplicationClient applicationClient, EnvironmentSettings environmentSettings)
        : base(applicationClient, environmentSettings)
    { }

    #endregion

    #region Properties: Protected

    protected override string ServicePath
    {
        get
        {
            return IsReadFile ?
                $"/rest/CreatioApiGateway/GetPackageFileContent?packageName={_packageName}&filePath={Uri.EscapeDataString(_filePath)}"
                : $"/rest/CreatioApiGateway/GetPackageFilesDirectoryContent?packageName={_packageName}";
        }
    }

    #endregion

    #region Properties: Public

    public override HttpMethod HttpMethod => HttpMethod.Get;

    public bool IsReadFile
    {
        get { return !string.IsNullOrEmpty(_filePath); }
    }

    #endregion

    #region Methods: Private

    private static void PrintFileContent(string response)
    {
        Console.WriteLine();
        string prettyFormat = response.Replace("\\r", "\r").Replace("\\t", "\t").Replace("\\n", "\n").Trim('"')
                                      .Replace("\\\"", "\"").Replace("\\/", "/");
        Console.WriteLine(prettyFormat);
        Console.WriteLine();
    }

    private static void PrintFolderContent(string response)
    {
        Console.WriteLine();
        string trimmedResponse = response.Trim('[', ']');
        string[] files = trimmedResponse.Split(new[]
        {
            ','
        });
        foreach (string item in files)
        {
            string prettyFilePath = item.Trim('"').Replace("\\\\", "\\").Replace("//", "/").Trim('\\');
            Console.WriteLine(prettyFilePath);
        }
        Console.WriteLine();
    }

    #endregion

    #region Methods: Protected

    protected override void ProceedResponse(string response, ShowPackageFileContentOptions options)
    {
        base.ProceedResponse(response, options);
        if (IsReadFile)
        {
            PrintFileContent(response);
        }
        else
        {
            PrintFolderContent(response);
        }
    }

    #endregion

    #region Methods: Public

    public override int Execute(ShowPackageFileContentOptions options)
    {
        _packageName = options.PackageName;
        _filePath = options.FilePath?.Trim('\\', '/');
        return base.Execute(options);
    }

    #endregion

}
