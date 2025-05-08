using CommandLine;

namespace Clio.Command.PackageCommand;

[Verb("pull-pkg", Aliases = new[]
{
    "download"
}, HelpText = "Download package from a web application")]
internal class PullPkgOptions : EnvironmentOptions
{

    #region Properties: Public

    [Option('a', "Async", Required = false,
        HelpText = "Async download file.", Default = false)]
    public bool Async { get; set; }

    [Option('d', "DestinationPath", Required = false,
        HelpText = "Path to the directory where Zip created.", Default = null)]
    public string DestPath { get; set; }

    [Value(0, MetaName = "Name", Required = true, HelpText = "Package name")]
    public string Name { get; set; }

    [Option('r', "UnZip", Required = false,
        HelpText = "Unzip archive file.", Default = null)]
    public bool Unzip { get; set; }

    #endregion

}

internal class PullPkgCommand : Command<PullPkgOptions>
{

    #region Methods: Public

    public override int Execute(PullPkgOptions options)
    {
        return Program.DownloadZipPackages(options);
    }

    #endregion

}
