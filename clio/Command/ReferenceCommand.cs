using System;
using System.IO;
using System.Linq;
using Clio.Project;
using CommandLine;

namespace Clio.Command;

[Verb("ref-to", HelpText = "Change creatio package project core paths", Hidden = true)]
public class ReferenceOptions
{

    #region Properties: Public

    [Option('p', "Path", Required = false, HelpText = "Path to the project file",
        Default = null)]
    public string Path { get; set; }

    [Value(0, MetaName = "ReferenceType", Required = false, HelpText = "Indicates what the project will refer to." +
        " Can be 'bin' or 'src'", Default = "src")]
    public string ReferenceType { get; set; }

    [Option('r', "ReferencePattern", Required = false, HelpText = "Pattern for reference path",
        Default = null)]
    public string RefPattern { get; set; }

    #endregion

}

public class ReferenceCommand : Command<ReferenceOptions>
{

    #region Fields: Private

    private readonly ICreatioPkgProjectCreator _projectCreator;

    #endregion

    #region Constructors: Public

    public ReferenceCommand(ICreatioPkgProjectCreator projectCreator)
    {
        _projectCreator = projectCreator;
    }

    #endregion

    #region Properties: Private

    private static string CurrentProj =>
        new DirectoryInfo(Environment.CurrentDirectory).GetFiles("*.csproj").FirstOrDefault()?.FullName;

    #endregion

    #region Methods: Public

    public override int Execute(ReferenceOptions options)
    {
        options.Path = options.Path ?? CurrentProj;
        if (string.IsNullOrEmpty(options.Path))
        {
            throw new ArgumentNullException(nameof(options.Path));
        }
        if (!string.IsNullOrEmpty(options.RefPattern))
        {
            options.ReferenceType = "custom";
        }
        ICreatioPkgProject project = _projectCreator.CreateFromFile(options.Path);
        try
        {
            switch (options.ReferenceType)
            {
                case "bin":
                    project = project.RefToBin();
                    break;
                case "src":
                    project = project.RefToCoreSrc();
                    break;
                case "custom":
                    project = project.RefToCustomPath(options.RefPattern);
                    break;
                case "unit-bin":
                    project = project.RefToUnitBin();
                    break;
                case "unit-src":
                    project = project.RefToUnitCoreSrc();
                    break;
                default:
                    throw new NotSupportedException($"You use not supported option type {options.ReferenceType}");
            }
            project.SaveChanges();
            Console.WriteLine("Done");
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return 1;
        }
    }

    #endregion

}
