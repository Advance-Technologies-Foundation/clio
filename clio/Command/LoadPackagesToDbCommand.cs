using System;
using CommandLine;

namespace Clio.Command;

[Verb("pkg-to-db", Aliases = new[] { "todb", "2db" },
    HelpText = "Load packages to database on a web application")]
public class LoadPackagesToDbOptions : EnvironmentOptions
{
}

public class LoadPackagesToDbCommand : Command<EnvironmentOptions>
{
    private readonly IFileDesignModePackages _fileDesignModePackages;
    private readonly ILogger _logger;

    public LoadPackagesToDbCommand(IFileDesignModePackages fileDesignModePackages, ILogger logger)
    {
        fileDesignModePackages.CheckArgumentNull(nameof(fileDesignModePackages));
        _fileDesignModePackages = fileDesignModePackages;
        _logger = logger;
    }

    public override int Execute(EnvironmentOptions options)
    {
        try
        {
            _fileDesignModePackages.LoadPackagesToDb();
            _logger.WriteLine();
            return 0;
        }
        catch (Exception e)
        {
            _logger.WriteError(e.ToString());
            return 1;
        }
    }
}
