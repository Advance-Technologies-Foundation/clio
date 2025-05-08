using System;
using System.Net.Http;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("show-package-file-content", Aliases = new[] { "show-files", "files" },
    HelpText = "Show package file context")]
public class ShowPackageFileContentOptions : RemoteCommandOptions
{
    [Option("package", Required = true, HelpText = "Package name")]
    public string PackageName { get; internal set; }

    [Option("file", Required = false, HelpText = "file path")]
    public string FilePath { get; internal set; }
}

internal class ShowPackageFileContentCommand(
    IApplicationClient applicationClient,
    EnvironmentSettings environmentSettings)
    : RemoteCommand<ShowPackageFileContentOptions>(applicationClient, environmentSettings)
{
    private string _filePath;

    private object _packageName;
    public override HttpMethod HttpMethod => HttpMethod.Get;

    protected override string ServicePath =>
        IsReadFile
            ? $"/rest/CreatioApiGateway/GetPackageFileContent?packageName={_packageName}&filePath={Uri.EscapeDataString(_filePath)}"
            : $"/rest/CreatioApiGateway/GetPackageFilesDirectoryContent?packageName={_packageName}";

    public bool IsReadFile => !string.IsNullOrEmpty(_filePath);

    public override int Execute(ShowPackageFileContentOptions options)
    {
        _packageName = options.PackageName;
        _filePath = options.FilePath?.Trim('\\', '/');
        return base.Execute(options);
    }

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

    private static void PrintFolderContent(string response)
    {
        Console.WriteLine();
        string trimmedResponse = response.Trim('[', ']');
        string[] files = trimmedResponse.Split([',']);
        foreach (string item in files)
        {
            string prettyFilePath = item.Trim('"').Replace("\\\\", "\\").Replace("//", "/").Trim('\\');
            Console.WriteLine(prettyFilePath);
        }

        Console.WriteLine();
    }

    private static void PrintFileContent(string response)
    {
        Console.WriteLine();
        string prettyFormat = response.Replace("\\r", "\r").Replace("\\t", "\t").Replace("\\n", "\n").Trim('"')
            .Replace("\\\"", "\"").Replace("\\/", "/");
        Console.WriteLine(prettyFormat);
        Console.WriteLine();
    }
}
