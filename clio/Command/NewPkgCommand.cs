﻿using System;
using System.Collections.Generic;
using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;
using CommandLine.Text;

namespace Clio.Command;

[Verb("new-pkg", Aliases = new[]
{
    "init"
}, HelpText = "Create a new creatio package in local file system")]
public class NewPkgOptions
{

    #region Properties: Public

    [Usage(ApplicationAlias = "clio")]
    public static IEnumerable<Example> Examples =>
        new List<Example>
        {
            new("Create new package with name 'ATF'",
                new NewPkgOptions
                {
                    Name = "ATF"
                }),
            new("Create new package with name 'ATF' and with links on local installation creatio with file design mode",
                new NewPkgOptions
                {
                    Name = "ATF", Rebase = "bin"
                })
        };

    [Value(0, MetaName = "Name", Required = true, HelpText = "Name of the created instance")]
    public string Name { get; set; }

    [Option('r', "References", Required = false, HelpText = "Set references to local bin assemblies for development")]
    public string Rebase { get; set; }

    #endregion

}

public class NewPkgCommand : Command<NewPkgOptions>
{

    #region Fields: Private

    private readonly ISettingsRepository _settingsRepository;
    private readonly Command<ReferenceOptions> _referenceCommand;
    private readonly ILogger _logger;

    #endregion

    #region Constructors: Public

    public NewPkgCommand(ISettingsRepository settingsRepository, Command<ReferenceOptions> referenceCommand,
        ILogger logger)
    {
        _settingsRepository = settingsRepository;
        _referenceCommand = referenceCommand;
        _logger = logger;
    }

    #endregion

    #region Methods: Public

    public override int Execute(NewPkgOptions options)
    {
        EnvironmentSettings settings = _settingsRepository.GetEnvironment();
        try
        {
            CreatioPackage package = CreatioPackage.CreatePackage(options.Name, settings.Maintainer);
            package.Create();
            if (!string.IsNullOrEmpty(options.Rebase) && options.Rebase != "nuget")
            {
                _referenceCommand.Execute(new ReferenceOptions
                {
                    Path = package.FullPath, ReferenceType = options.Rebase
                });
                package.RemovePackageConfig();
            }
            _logger.WriteInfo("Done");
            return 0;
        }
        catch (Exception e)
        {
            _logger.WriteError(e.ToString());
            return 1;
        }
    }

    #endregion

}
