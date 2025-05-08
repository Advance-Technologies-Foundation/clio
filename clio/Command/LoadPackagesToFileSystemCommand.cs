using System;
using Clio.Common;
using Clio.Package;
using CommandLine;

namespace Clio.Command;

[Verb("pkg-to-file-system", Aliases = new[] { "tofs", "2fs" },
    HelpText = "Load packages to file system on a web application")]
public class LoadPackagesToFileSystemOptions : EnvironmentOptions
{
}

public class LoadPackagesToFileSystemCommand : Command<EnvironmentOptions>
{
    private readonly IFileDesignModePackages _fileDesignModePackages;

    public LoadPackagesToFileSystemCommand(IFileDesignModePackages fileDesignModePackages)
    {
        fileDesignModePackages.CheckArgumentNull(nameof(fileDesignModePackages));
        _fileDesignModePackages = fileDesignModePackages;
    }

    public override int Execute(EnvironmentOptions options)
    {
        try
        {
            _fileDesignModePackages.LoadPackagesToFileSystem();
            Console.WriteLine();
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return 1;
        }
    }
}
