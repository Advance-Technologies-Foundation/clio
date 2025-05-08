using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommandLine;

namespace Clio.Command;

[Verb("push-pkg", Aliases = new[] { "install", "push" }, HelpText = "Install package on a web application")]
public class PushPkgOptions : InstallOptions
{
    [Option("InstallSqlScript", Required = false, HelpText = "Install sql script")]
    public bool? InstallSqlScript { get; set; }

    [Option("InstallPackageData", Required = false, HelpText = "Install package data")]
    public bool? InstallPackageData { get; set; }

    [Option("ContinueIfError", Required = false, HelpText = "Continue if error")]
    public bool? ContinueIfError { get; set; }

    [Option("SkipConstraints", Required = false, HelpText = "Skip constraints")]
    public bool? SkipConstraints { get; set; }

    [Option("SkipValidateActions", Required = false, HelpText = "Skip validate actions")]
    public bool? SkipValidateActions { get; set; }

    [Option("ExecuteValidateActions", Required = false, HelpText = "Execute validate actions")]
    public bool? ExecuteValidateActions { get; set; }

    [Option("IsForceUpdateAllColumns", Required = false, HelpText = "Is force update all columns")]
    public bool? IsForceUpdateAllColumns { get; set; }

    [Option("id", Required = false, HelpText = "Marketplace application id")]
    public IEnumerable<int> MarketplaceIds { get; set; }

    [Option("force-compilation", Required = false, HelpText = "Runs compilation after install package")]
    public bool ForceCompilation { get; set; }
}

public class PushPackageCommand : Command<PushPkgOptions>
{
    private readonly ICompileConfigurationCommand _compileConfigurationCommand;
    private readonly EnvironmentSettings _environmentSettings;
    private readonly IMarketplace _marketplace;
    private readonly IPackageInstaller _packageInstaller;
    private readonly PackageInstallOptions _packageInstallOptionsDefault = new();

    public PushPackageCommand()
    {
    } // for tests

    public PushPackageCommand(EnvironmentSettings environmentSettings, IPackageInstaller packageInstaller,
        IMarketplace marketplace, ICompileConfigurationCommand compileConfigurationCommand)
    {
        environmentSettings.CheckArgumentNull(nameof(environmentSettings));
        packageInstaller.CheckArgumentNull(nameof(packageInstaller));
        compileConfigurationCommand.CheckArgumentNull(nameof(compileConfigurationCommand));
        _environmentSettings = environmentSettings;
        _packageInstaller = packageInstaller;
        _marketplace = marketplace;
        _compileConfigurationCommand = compileConfigurationCommand;
    }

    private PackageInstallOptions ExtractPackageInstallOptions(PushPkgOptions options)
    {
        PackageInstallOptions packageInstallOptions = new()
        {
            InstallSqlScript = options.InstallSqlScript ?? true,
            InstallPackageData = options.InstallPackageData ?? true,
            ContinueIfError = options.ContinueIfError ?? true,
            SkipConstraints = options.SkipConstraints ?? false,
            SkipValidateActions = options.SkipValidateActions ?? false,
            ExecuteValidateActions = options.ExecuteValidateActions ?? false,
            IsForceUpdateAllColumns = options.IsForceUpdateAllColumns ?? false
        };
        return packageInstallOptions == _packageInstallOptionsDefault
            ? null
            : packageInstallOptions;
    }


    /// <summary>
    ///     Executes the push package command with the specified options.
    /// </summary>
    /// <param name="options">The options for the push package command.</param>
    /// <returns>Returns 0 if the command executed successfully, otherwise returns 1.</returns>
    /// <remarks>
    ///     This method installs a package on a web application. If `MarketplaceIds` are provided, it installs the package
    ///     for each ID. If `ForceCompilation` is true and the installation is successful, it compiles the configuration.
    /// </remarks>
    public override int Execute(PushPkgOptions options)
    {
        PackageInstallOptions packageInstallOptions = ExtractPackageInstallOptions(options);
        bool success = false;
        try
        {
            if (options.MarketplaceIds != null && options.MarketplaceIds.Any())
            {
                foreach (int marketplaceId in options.MarketplaceIds)
                {
                    string fullPath = string.Empty;
                    Task.Run(async () => { fullPath = await _marketplace.GetFileByIdAsync(marketplaceId); }).Wait();

                    bool _loopSuccess = _packageInstaller.Install(fullPath, _environmentSettings,
                        packageInstallOptions, options.ReportPath);
                    Console.WriteLine(_loopSuccess
                        ? $"Done installing app by id: {marketplaceId}"
                        : $"Error installing app by id: {marketplaceId}");
                }

                success = true;
            }
            else
            {
                success = _packageInstaller.Install(options.Name, _environmentSettings,
                    packageInstallOptions, options.ReportPath);
            }

            if (options.ForceCompilation && success)
            {
                CompileConfigurationOptions compileOptions = CreateFromPushPkgOptions(options);
                success &= _compileConfigurationCommand.Execute(compileOptions) == 0;
            }

            Console.WriteLine(success ? "Done" : "Error");
            return success ? 0 : 1;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.StackTrace);
            return 1;
        }
    }

    private CompileConfigurationOptions CreateFromPushPkgOptions(EnvironmentOptions options) =>
        new()
        {
            Environment = options.Environment,
            Login = options.Login,
            Password = options.Password,
            Uri = options.Uri,
            All = true
        };
}

public class InstallGatePkgCommand(
    EnvironmentSettings environmentSettings,
    IPackageInstaller packageInstaller,
    IMarketplace marketplace,
    ICompileConfigurationCommand compileConfigurationCommand,
    IApplication applicatom,
    ILogger logger)
    : PushPackageCommand(environmentSettings, packageInstaller, marketplace, compileConfigurationCommand)
{
    private readonly IApplication _application = applicatom;
    private readonly ILogger _logger = logger;

    public override int Execute(PushPkgOptions options)
    {
        int result = base.Execute(options);
        if (result == 0)
        {
            _application.Restart();
        }

        return result;
    }
}
