using System;
using Clio.Common;
using Clio.WebApplication;
using CommandLine;

namespace Clio.Command;

#region Class: UploadLicenseCommandOptions

[Verb("upload-license", Aliases = new[]
{
    "license", "loadlicense", "load-license"
}, HelpText = "Load license to selected environment")]
public class UploadLicenseCommandOptions : EnvironmentOptions
{

    #region Properties: Public

    [Value(0, MetaName = "FilePath", Required = false, HelpText = "License file path")]
    public string FilePath { get; set; }

    #endregion

}

#endregion

#region Class: UploadLicenseCommand

public class UploadLicenseCommand : Command<UploadLicenseCommandOptions>
{

    #region Fields: Private

    private readonly IApplication _application;

    #endregion

    #region Constructors: Public

    public UploadLicenseCommand(IApplication application)
    {
        application.CheckArgumentNull(nameof(application));
        _application = application;
    }

    #endregion

    #region Methods: Public

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

    #endregion

}

#endregion
