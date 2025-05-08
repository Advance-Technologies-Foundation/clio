using System;
using System.IO;
using System.Linq;

using Clio.Project;
using CommandLine;

namespace Clio.Command;

[Verb("ref-to", HelpText = "Change creatio package project core paths", Hidden = true)]
public class ReferenceOptions
{
    [Option('r', "ReferencePattern", Required = false, HelpText = "Pattern for reference path",
        Default = null)]
    public string RefPattern { get; set; }

    [Option('p', "Path", Required = false, HelpText = "Path to the project file",
        Default = null)]
    public string Path { get; set; }

    [Value(0, MetaName = "ReferenceType", Required = false, HelpText = "Indicates what the project will refer to." +
                                                                       " Can be 'bin' or 'src'", Default = "src")]
    public string ReferenceType { get; set; }
}

public class ReferenceCommand(ICreatioPkgProjectCreator projectCreator): Command<ReferenceOptions>
{
    private readonly ICreatioPkgProjectCreator _projectCreator = projectCreator;

    private static string CurrentProj =>
        new DirectoryInfo(Environment.CurrentDirectory).GetFiles("*.csproj").FirstOrDefault()?.FullName;

    public override int Execute(ReferenceOptions options)
    {
        options.Path ??= CurrentProj;
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
            project = options.ReferenceType switch
            {
                "bin" => project.RefToBin(),
                "src" => project.RefToCoreSrc(),
                "custom" => project.RefToCustomPath(options.RefPattern),
                "unit-bin" => project.RefToUnitBin(),
                "unit-src" => project.RefToUnitCoreSrc(),
                _ => throw new NotSupportedException($"You use not supported option type {options.ReferenceType}"),
            };
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
}
