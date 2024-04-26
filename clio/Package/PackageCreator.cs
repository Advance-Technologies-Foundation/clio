using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Clio.Common;
using Clio.Workspaces;
using Terrasoft.Core.Packages;
using JsonConverter = System.Text.Json.Serialization.JsonConverter;

namespace Clio.Package
{

    #region Interface: IPackageCreator

    public interface IPackageCreator
    {

        #region Methods: Public

        void Create(string packageName, bool? asApp);
        void Create(string packagesPath, string packageName);

        #endregion

    }

    #endregion

    #region Class: PackageCreator

    public class PackageCreator : IPackageCreator
    {

        #region Fields: Private

        private readonly EnvironmentSettings _environmentSettings;
        private readonly IWorkspace _workspace;
        private readonly IWorkspaceSolutionCreator _workspaceSolutionCreator;
        private readonly ITemplateProvider _templateProvider;
        private readonly IWorkspacePathBuilder _workspacePathBuilder;
        private readonly IStandalonePackageFileManager _standalonePackageFileManager;
        private readonly IJsonConverter _jsonConverter;
        private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
        private readonly IFileSystem _fileSystem;

        #endregion

        #region Constructors: Public

        public PackageCreator(EnvironmentSettings environmentSettings, IWorkspace workspace,
                IWorkspaceSolutionCreator workspaceSolutionCreator, ITemplateProvider templateProvider,
                IWorkspacePathBuilder workspacePathBuilder, IStandalonePackageFileManager standalonePackageFileManager,
                IJsonConverter jsonConverter, IWorkingDirectoriesProvider workingDirectoriesProvider,
                IFileSystem fileSystem) {
            environmentSettings.CheckArgumentNull(nameof(environmentSettings));
            templateProvider.CheckArgumentNull(nameof(templateProvider));
            workspace.CheckArgumentNull(nameof(workspace));
            workspaceSolutionCreator.CheckArgumentNull(nameof(workspaceSolutionCreator));
            workspacePathBuilder.CheckArgumentNull(nameof(workspacePathBuilder));
            standalonePackageFileManager.CheckArgumentNull(nameof(standalonePackageFileManager));
            jsonConverter.CheckArgumentNull(nameof(jsonConverter));
            workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
            fileSystem.CheckArgumentNull(nameof(fileSystem));
            _environmentSettings = environmentSettings;
            _workspace = workspace;
            _workspaceSolutionCreator = workspaceSolutionCreator;
            _templateProvider = templateProvider;
            _workspacePathBuilder = workspacePathBuilder;
            _standalonePackageFileManager = standalonePackageFileManager;
            _jsonConverter = jsonConverter;
            _workingDirectoriesProvider = workingDirectoriesProvider;
            _fileSystem = fileSystem;
        }
        #endregion

        #region Properties: Private

        private bool IsWorkspace => _workspacePathBuilder.IsWorkspace;

        private string Maintainer => _environmentSettings.Maintainer ?? "Customer";

        #endregion

        #region Methods: Private

        private PackageDescriptorDto CreatePackageDescriptor(string packageName, bool isStandalonePackage = true) =>
            new PackageDescriptorDto {
                Descriptor = new PackageDescriptor {
                    Name = packageName,
                    Maintainer = Maintainer,
                    UId = Guid.NewGuid(),
                    PackageVersion = "0.1.0",
                    ProjectPath = isStandalonePackage ? $"Files/{packageName}.csproj" : string.Empty,
                    Type = isStandalonePackage ? PackageType.Assembly : PackageType.General,
                    ModifiedOnUtc = PackageDescriptor.ConvertToModifiedOnUtc(DateTime.Now),
                    DependsOn = new List<PackageDependency>()
                }
            };

        private void RenameTemplatePackageNameCsproj(string packagesPath, string packageName) {
            string packageFilesPath = _standalonePackageFileManager.BuildFilesPath(packagesPath, packageName);
            string templatePackageNameCsprojPath = Path.Combine(packageFilesPath, "PackageName.csproj");
            string newPackageNameCsprojPath = _standalonePackageFileManager
                .BuildStandaloneProjectPath(packagesPath, packageName);
            _fileSystem.MoveFile(templatePackageNameCsprojPath, newPackageNameCsprojPath);
        }

        private void CreatePackageDescriptorToFileSystem(string packagePath, string packageName) {
            PackageDescriptorDto descriptor = CreatePackageDescriptor(packageName);
            string descriptorPath = Path.Combine(packagePath, "descriptor.json");
            _jsonConverter.SerializeObjectToFile(descriptor, descriptorPath);
        }

        private void CreatePackageIfNotExists(string packagesPath, string packageName) {
            string packagePath = Path.Combine(packagesPath, packageName);
            if (_fileSystem.ExistsDirectory(packagePath)) {
                throw new InvalidOperationException($"Directory '{packagePath}' allready exists");
            }
            _templateProvider.CopyTemplateFolder("package", packagePath);
            CreatePackageDescriptorToFileSystem(packagePath, packageName);
            CreatePackageProj(packagesPath, packageName);

        }

        private void CreatePackageProj(string packagesPath, string packageName) {
            ApplyMacrosToProjectFiles(packagesPath, packageName);
            RenameTemplatePackageNameCsproj(packagesPath, packageName);
        }

        private void ApplyMacrosToProjectFiles(string packagesPath, string packageName) {
            string packageFilesPath = _standalonePackageFileManager.BuildFilesPath(packagesPath, packageName);
            string packageNameTargetPropsPath = Path.Combine(packageFilesPath, "Directory.Build.targets");
            string packageNameTargetPropsContent = _fileSystem.ReadAllText(packageNameTargetPropsPath);
            string newPackageNameCsprojContent = packageNameTargetPropsContent
                .Replace("#PackageName#", packageName);
            _fileSystem.WriteAllTextToFile(packageNameTargetPropsPath, newPackageNameCsprojContent);
        }

        private string GetPackagesPath() =>
            IsWorkspace
                ? _workspacePathBuilder.PackagesFolderPath
                : _workingDirectoriesProvider.CurrentDirectory;

        private void AddPackageToWorkspaceIfNeeded(string packageName) {
            if (!IsWorkspace) {
                return;
            }
            var workspacePackages = _workspace.WorkspaceSettings.Packages;
            if (workspacePackages.Contains(packageName)) {
                return;
            }
            workspacePackages.Add(packageName);
            _workspace.SaveWorkspaceSettings();
            _workspaceSolutionCreator.Create();
        }

        #endregion

        #region Methods: Public

        public void Create(string packageName, bool? asApp) {
            var packagesPath = GetPackagesPath();
            Create(packagesPath, packageName, asApp);
        }

        public void Create(string packagesPath, string packageName) {
            Create(packagesPath, packageName, null);
        }
        public void Create(string packagesPath, string packageName, bool? asApp) {
            CreatePackageIfNotExists(packagesPath, packageName);
            AddPackageToWorkspaceIfNeeded(packageName);
            if ((asApp.HasValue && asApp.Value) 
                    || (asApp.HasValue && asApp.Value && _fileSystem.GetDirectories(packagesPath).Length == 1)) {
                AddAppDescriptor(packagesPath, packageName);
            } else {
                UpdateAppDescriptorIfExists(packagesPath, packageName);
            }
        }

        private void UpdateAppDescriptorIfExists(string packagesPath, string packageName) {
            var appDescriptorFiles = _fileSystem.GetFiles(packagesPath, "app-descriptor.json", SearchOption.AllDirectories);
            if (appDescriptorFiles.Count() != 1) {
                return;
            }
            var appDescriptorFile = appDescriptorFiles[0];
            var appDescriptorContent = _fileSystem.ReadAllText(appDescriptorFile);
            var appDescriptor = JsonSerializer.Deserialize<AppDescriptorJson>(appDescriptorContent);
            var stalePackages = appDescriptor.Packages.FindAll(p => p.Name == packageName);
            foreach ( var package in stalePackages ) {
                appDescriptor.Packages.Remove(package);
            }
            appDescriptor.Packages.Add(GetPackageFromDescriptor(packagesPath, packageName));
            SaveAppDescriptorToFile(appDescriptor, appDescriptorFile);
        }

        internal void SaveAppDescriptorToFile(AppDescriptorJson appDescriptor, string fileName) {
            var options = new JsonSerializerOptions() {
                WriteIndented = true,
            };
            string appDescriptorContent = JsonSerializer.Serialize(appDescriptor, options);
            _fileSystem.WriteAllTextToFile(fileName, appDescriptorContent);
        }

        private Package GetPackageFromDescriptor(string packagePath, string packageName) {
            var descriptorContent = _fileSystem.ReadAllText(Path.Combine(packagePath, packageName, "descriptor.json"));
            var packageDescriptor = JsonSerializer.Deserialize<PackageDescriptorDto>(descriptorContent);
            return new Package() {
                UId = packageDescriptor.Descriptor.UId.ToString(),
                Name = packageDescriptor.Descriptor.Name
            };
        }

        private void AddAppDescriptor(string packagesPath, string packageName) {
            var package = GetPackageFromDescriptor(packagesPath, packageName);
            AppDescriptorJson addDescriptorDto = new() {
                Name = packageName,
                Maintainer = Maintainer,
                Description = "",
                Icon = "",
                IconName = "",
                MarketplaceLink = "",
                OrderLink = "",
                SupportEmail = "",
                HelpLink = "",
                Color = "#FFAC07",
                Version = "0.1.0",
                Code = packageName,
                Packages = new List<Package>{
                    package}
            };
            var appDescriptorPath = Path.Combine(packagesPath, packageName, "Files", "app-descriptor.json");
            SaveAppDescriptorToFile(addDescriptorDto, appDescriptorPath);
        }

        #endregion

    }

    #endregion
    public class AppDescriptorJson
    {
        public string Name
        {
            get; set;
        }
        public string Description
        {
            get; set;
        }
        public string Maintainer
        {
            get; set;
        }
        public string Icon
        {
            get; set;
        }
        public string IconName
        {
            get; set;
        }
        public string Color
        {
            get; set;
        }
        public string Version
        {
            get; set;
        }
        public string MarketplaceLink
        {
            get; set;
        }
        public string HelpLink
        {
            get; set;
        }
        public string OrderLink
        {
            get; set;
        }
        public string SupportEmail
        {
            get; set;
        }
        public string Code
        {
            get; set;
        }
        public List<Package> Packages
        {
            get; set;
        }
    }

    public class Package
    {
        public string UId
        {
            get; set;
        }
        public string Name
        {
            get; set;
        }

    }
}


