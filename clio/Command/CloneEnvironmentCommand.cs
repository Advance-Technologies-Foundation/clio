using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using ATF.Repository;
using ATF.Repository.Providers;
using Autofac;
using Clio.Command.PackageCommand;
using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;
using CreatioModel;
using k8s.Models;
using YamlDotNet.Serialization;

namespace Clio.Command;

[Verb("clone-env", Aliases = new[] { "clone", "clone-environment" },
    HelpText = "Clone one environment to another")]
internal class CloneEnvironmentOptions : ShowDiffEnvironmentsOptions
{
}

internal class CloneEnvironmentCommand : BaseDataContextCommand<CloneEnvironmentOptions>
{
    private ShowDiffEnvironmentsCommand showDiffEnvironmentsCommand;
    private ApplyEnvironmentManifestCommand applyEnvironmentManifestCommand;
    private PullPkgCommand pullPkgCommand;
    private PushPackageCommand pushPackageCommand;
    private PingAppCommand pingAppCommand;
    private readonly IEnvironmentManager environmentManager;
    private readonly ICompressionUtilities _compressionUtilities;
    private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
    private readonly IFileSystem _fileSystem;
    private readonly ISettingsRepository settingsRepository;

    public CloneEnvironmentCommand(
        ShowDiffEnvironmentsCommand showDiffEnvironmentsCommand,
        ApplyEnvironmentManifestCommand applyEnvironmentManifestCommand, PullPkgCommand pullPkgCommand,
        PushPackageCommand pushPackageCommand, IEnvironmentManager environmentManager, ILogger logger,
        IDataProvider provider, ICompressionUtilities compressionUtilities,
        IWorkingDirectoriesProvider workingDirectoriesProvider, IFileSystem fileSystem,
        ISettingsRepository settingsRepository, PingAppCommand pingAppCommand)
        : base(provider, logger)
    {
        this.showDiffEnvironmentsCommand = showDiffEnvironmentsCommand;
        this.applyEnvironmentManifestCommand = applyEnvironmentManifestCommand;
        this.pullPkgCommand = pullPkgCommand;
        this.environmentManager = environmentManager;
        _compressionUtilities = compressionUtilities;
        _workingDirectoriesProvider = workingDirectoriesProvider;
        _fileSystem = fileSystem;
        this.settingsRepository = settingsRepository;
        if (this.settingsRepository == null)
        {
            this.pushPackageCommand = pushPackageCommand;
            this.pullPkgCommand = pullPkgCommand;
            this.applyEnvironmentManifestCommand = applyEnvironmentManifestCommand;
            this.showDiffEnvironmentsCommand = showDiffEnvironmentsCommand;
            this.pingAppCommand = pingAppCommand;
        }
    }

    public override int Execute(CloneEnvironmentOptions options)
    {
        bool useTempDirectory = string.IsNullOrEmpty(options.WorkingDirectory);
        string workingDirectoryPath = useTempDirectory
            ? _workingDirectoriesProvider.CreateTempDirectory()
            : options.WorkingDirectory;
        if (settingsRepository != null)
        {
            IContainer sourceBindingModule =
                new BindingsModule().Register(settingsRepository.GetEnvironment(options.Source));
            showDiffEnvironmentsCommand = sourceBindingModule.Resolve<ShowDiffEnvironmentsCommand>();
            pullPkgCommand = sourceBindingModule.Resolve<PullPkgCommand>();
            IContainer targetBindingModule =
                new BindingsModule().Register(settingsRepository.GetEnvironment(options.Target));
            pushPackageCommand = targetBindingModule.Resolve<PushPackageCommand>();
            applyEnvironmentManifestCommand = targetBindingModule.Resolve<ApplyEnvironmentManifestCommand>();
            pingAppCommand = targetBindingModule.Resolve<PingAppCommand>();
        }

        string[] ? selectedMaintainers = string.IsNullOrWhiteSpace(options.Maintainer)
            ? null
            : options.Maintainer.Split(',', StringSplitOptions.TrimEntries);
        string[] ? excludedMaintainers = string.IsNullOrWhiteSpace(options.ExcludeMaintainer)
            ? null
            : options.ExcludeMaintainer.Split(',', StringSplitOptions.TrimEntries);
        if (selectedMaintainers != null && excludedMaintainers != null)
        {
            throw new ArgumentException("Argument 'Maintainer' cannot be specified with argument 'ExcludeMaintainer'.");
        }

        try
        {
            options.FileName = Path.Combine(workingDirectoryPath, $"from_{options.Source}_to_{options.Target}.yaml");
            showDiffEnvironmentsCommand.Execute(options);
            EnvironmentManifest diffManifest = environmentManager.LoadEnvironmentManifestFromFile(options.FileName);
            string sourceZipPackagePath = Path.Combine(workingDirectoryPath, "SourceZipPackages");
            _fileSystem.CreateDirectory(sourceZipPackagePath);
            int number = 1;
            int packagesCount = diffManifest.Packages.Count;
            IEnumerable<CreatioManifestPackage> diffPackages = diffManifest.Packages
                .Where(p => selectedMaintainers == null || selectedMaintainers.Contains(p.Maintainer))
                .Where(p => excludedMaintainers == null || !excludedMaintainers.Contains(p.Maintainer));
            foreach (CreatioManifestPackage package in diffPackages)
            {
                PullPkgOptions pullPkgOptions = new ()
                {
                    Environment = options.Source,
                    Name = package.Name,
                    DestPath = sourceZipPackagePath
                };
                string progress = $"({number++} from {packagesCount})";
                _logger.WriteInfo($"Start pull package: {package.Name} {progress}");
                pullPkgCommand.Execute(pullPkgOptions);
                _logger.WriteInfo($"Done pull package: {package.Name} {progress}");
            }

            string sourceGzPackages = Path.Combine(workingDirectoryPath, "SourceGzPackages");
            number = 1;
            foreach (CreatioManifestPackage package in diffPackages)
            {
                string packageZipPath = Path.Combine(sourceZipPackagePath, $"{package.Name}.zip");
                string progress = $"({number++} from {packagesCount})";
                _logger.WriteInfo($"Start unzip package: {package.Name} {progress}");
                _compressionUtilities.Unzip(packageZipPath, sourceGzPackages);
                _logger.WriteInfo($"Done unzip package: {package.Name} {progress}");
            }

            _fileSystem.CreateDirectory(sourceGzPackages);
            _logger.WriteInfo($"Start zip packages");
            string commonPackagesZipPath = Path.Combine(
                workingDirectoryPath,
                $"from_{options.Source}_to_{options.Target}.zip");
            _compressionUtilities.Zip(sourceGzPackages, commonPackagesZipPath);
            _logger.WriteInfo($"Done zip packages");
            PushPkgOptions pushPackageOptions = new () { Environment = options.Target, Name = commonPackagesZipPath };
            pushPackageCommand.Execute(pushPackageOptions);
            PingAppOptions pingCommandOptions = new () { Environment = options.Target };

            // pingAppCommand.Execute(pingCommandOptions);
            // var applyEnvironmentManifestOptions = new ApplyEnvironmentManifestOptions() {
            //  Environment = options.Target,
            //  ManifestFilePath = options.FileName
            // };
            // applyEnvironmentManifestCommand.Execute(applyEnvironmentManifestOptions);
        }
        finally
        {
            if (useTempDirectory)
            {
                _workingDirectoriesProvider.DeleteDirectoryIfExists(workingDirectoryPath);
            }
        }

        _logger.WriteInfo("Done");
        return 0;
    }
}
