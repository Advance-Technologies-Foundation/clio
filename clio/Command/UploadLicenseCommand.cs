using System;
using Clio.Common;
using Clio.WebApplication;
using CommandLine;

namespace Clio.Command;

[Verb("upload-license", Aliases = new[] { "license", "loadlicense", "load-license" },
    HelpText = "Load license to selected environment")]
public class UploadLicenseCommandOptions : EnvironmentOptions
{
    [Value(0, MetaName = "FilePath", Required = false, HelpText = "License file path")]
    public string FilePath { get; set; }
}

public class UploadLicenseCommand : Command<UploadLicenseCommandOptions>
{
    private readonly IApplication _application;

    public UploadLicenseCommand(IApplication application)
    {
        application.CheckArgumentNull(nameof(application));
        _application = application;
    }

    public override int Execute(UploadLicenseCommandOptions options)
    {
        try
        {
            _application.LoadLicense(options.FilePath);
            Console.WriteLine("Done");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            return 1;
        }
    }
}
