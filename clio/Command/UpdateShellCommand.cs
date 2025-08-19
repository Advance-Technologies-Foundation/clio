using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using CommandLine;
using Clio.Common;
using CreatioModel;

namespace Clio.Command
{
    #region Class: UpdateShellOptions

    [Verb("update-shell", HelpText = "Update shell application by packaging and deploying to Creatio environment")]
    public class UpdateShellOptions : RemoteCommandOptions
    {
        #region Properties: Public

        [Option("build", Required = false, HelpText = "Execute npm run build:shell before packaging", Default = false)]
        public bool Build { get; set; }

        [Option("force", Required = false, HelpText = "Skip confirmations and force deployment", Default = false)]
        public bool Force { get; set; }

        [Option("verbose", Required = false, HelpText = "Enable detailed logging output", Default = false)]
        public bool Verbose { get; set; }

        [Option("dry-run", Required = false, HelpText = "Simulate deployment without actual upload", Default = false)]
        public bool DryRun { get; set; }

        #endregion
    }

    #endregion

    #region Class: UpdateShellCommand

    public class UpdateShellCommand : RemoteCommand<UpdateShellOptions>
    {
        #region Fields: Private

        private readonly IFileSystem _fileSystem;
        private readonly ICompressionUtilities _compressionUtilities;
        private readonly IProcessExecutor _processExecutor;
        private readonly ISysSettingsManager _sysSettingsManager;
        private readonly IServiceUrlBuilder _serviceUrlBuilder;

        #endregion

        #region Properties: Protected

        protected override string ClioGateMinVersion { get; } = "2.0.0.36";

        #endregion

        #region Constructors: Public

        public UpdateShellCommand(IApplicationClient applicationClient,
            EnvironmentSettings environmentSettings,
            IFileSystem fileSystem,
            ICompressionUtilities compressionUtilities,
            IProcessExecutor processExecutor,
            ISysSettingsManager sysSettingsManager,
            IServiceUrlBuilder serviceUrlBuilder, IClioGateway clioGateway)
            : base(applicationClient, environmentSettings)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _compressionUtilities = compressionUtilities ?? throw new ArgumentNullException(nameof(compressionUtilities));
            _processExecutor = processExecutor ?? throw new ArgumentNullException(nameof(processExecutor));
            _sysSettingsManager = sysSettingsManager ?? throw new ArgumentNullException(nameof(sysSettingsManager));
            _serviceUrlBuilder = serviceUrlBuilder ?? throw new ArgumentNullException(nameof(serviceUrlBuilder));
            ClioGateWay = clioGateway;
        }

        public UpdateShellCommand() // for tests
        {
        }

        #endregion

        #region Methods: Protected

        protected override void ExecuteRemoteCommand(UpdateShellOptions options)
        {
            try
            {
                var currentDirectory = Directory.GetCurrentDirectory();
                var repositoryRoot = FindRepositoryRoot(currentDirectory);

                if (options.Build)
                {
                    ExecuteBuildProcess(repositoryRoot, options);
                }

                var shellDirectory = Path.Combine(repositoryRoot, "dist", "apps", "studio-enterprise", "shell");
                ValidateShellDirectory(shellDirectory);

                var archiveFilePath = CreateArchive(shellDirectory, options);
                var archiveSizeMb = GetFileSizeInMb(archiveFilePath);

                if (!options.DryRun)
                {
                    ValidateAndUpdateMaxFileSize(archiveSizeMb, options);
                    UploadArchive(archiveFilePath, options);
                }

                Logger.WriteInfo($"Shell deployment {(options.DryRun ? "simulated" : "completed")} successfully!");
                Logger.WriteInfo($"Archive size: {archiveSizeMb:F1} MB");
            }
            catch (Exception ex)
            {
                Logger.WriteError($"Shell deployment failed: {ex.Message}");
                if (options.Verbose)
                {
                    Logger.WriteError($"Stack trace: {ex.StackTrace}");
                }
                throw;
            }
        }

        protected override string GetRequestData(UpdateShellOptions options)
        {
            // For file upload, we need to handle this differently in the upload method
            return "{}";
        }

        #endregion

        #region Methods: Private

        private string FindRepositoryRoot(string currentDirectory)
        {
            var directory = new DirectoryInfo(currentDirectory);
            while (directory != null)
            {
                if (_fileSystem.ExistsFile(Path.Combine(directory.FullName, "package.json")))
                {
                    return directory.FullName;
                }
                directory = directory.Parent;
            }
            throw new InvalidOperationException("Could not find repository root (package.json not found)");
        }

        private void ExecuteBuildProcess(string repositoryRoot, UpdateShellOptions options)
        {
            Logger.WriteInfo("Building shell application...");
            
            try
            {
                var result = _processExecutor.Execute("npm", "run build:shell", true, repositoryRoot, options.Verbose);
                
                if (options.Verbose)
                {
                    Logger.WriteInfo($"Build output: {result}");
                }

                Logger.WriteInfo("✓ Build completed successfully");
            }
            catch (Exception ex)
            {
                Logger.WriteError($"Build process failed: {ex.Message}");
                throw new InvalidOperationException("Build process failed", ex);
            }
        }

        private void ValidateShellDirectory(string shellDirectory)
        {
            if (!_fileSystem.ExistsDirectory(shellDirectory))
            {
                throw new DirectoryNotFoundException($"Shell directory not found: {shellDirectory}");
            }

            var files = _fileSystem.GetFiles(shellDirectory, "*", SearchOption.AllDirectories);
            if (files.Length == 0)
            {
                throw new InvalidOperationException($"Shell directory is empty: {shellDirectory}");
            }

            Logger.WriteInfo($"Found {files.Length} files in shell directory");
        }

        private string CreateArchive(string shellDirectory, UpdateShellOptions options)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var tempDirectory = Path.GetTempPath();
            var archiveFilePath = Path.Combine(tempDirectory, $"shell-{timestamp}.gz");

            Logger.WriteInfo($"Packaging files from {shellDirectory}...");

            _compressionUtilities.ZipDirectory(shellDirectory, archiveFilePath);

            var archiveSizeMb = GetFileSizeInMb(archiveFilePath);
            Logger.WriteInfo($"✓ Created archive: {Path.GetFileName(archiveFilePath)} ({archiveSizeMb:F1} MB)");

            return archiveFilePath;
        }

        private double GetFileSizeInMb(string filePath)
        {
            var fileInfo = new FileInfo(filePath);
            return fileInfo.Length / (1024.0 * 1024.0);
        }

        private void ValidateAndUpdateMaxFileSize(double archiveSizeMb, UpdateShellOptions options)
        {
            Logger.WriteInfo("Validating Creatio system settings...");

            try
            {
                var currentMaxFileSizeStr = _sysSettingsManager.GetSysSettingValueByCode("MaxFileSize");
                var currentMaxFileSizeMb = !string.IsNullOrEmpty(currentMaxFileSizeStr) ? Convert.ToInt32(currentMaxFileSizeStr.Replace("\"","")) : 0;

                if (currentMaxFileSizeMb < archiveSizeMb)
                {
                    var newMaxFileSize = Math.Ceiling(archiveSizeMb) + 5;
                    
                    if (!options.Force && !options.IsSilent)
                    {
                        Logger.WriteWarning($"Current MaxFileSize setting ({currentMaxFileSizeMb} MB) is insufficient for archive ({archiveSizeMb:F1} MB)");
                        Logger.WriteInfo($"Would you like to update MaxFileSize to {newMaxFileSize} MB? (y/n)");
                        
                        var response = Console.ReadLine();
                        if (!string.Equals(response, "y", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException("Deployment cancelled by user");
                        }
                    }

                    _sysSettingsManager.SetSysSettingByCode("MaxFileSize", newMaxFileSize.ToString());
                    Logger.WriteInfo($"✓ Updated MaxFileSize setting to {newMaxFileSize} MB");
                }
                else
                {
                    Logger.WriteInfo($"✓ MaxFileSize setting: {currentMaxFileSizeMb} MB (sufficient)");
                }
            }
            catch (Exception ex)
            {
                Logger.WriteWarning($"Could not validate MaxFileSize setting: {ex.Message}");
                Logger.WriteInfo("Proceeding with upload without MaxFileSize validation...");
            }
        }

        private void UploadArchive(string archiveFilePath, UpdateShellOptions options)
        {
            Logger.WriteInfo($"Uploading to environment '{options.Environment}'...");

            try
            {
                byte[] archiveBytes = _fileSystem.ReadAllBytes(archiveFilePath);

                // Use multipart form data for file upload
                MultipartFormDataContent formData = new();
                ByteArrayContent fileContent = new(archiveBytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/gzip");
                formData.Add(fileContent, "file", Path.GetFileName(archiveFilePath));

                // Upload via the CreatioApiGateway/UploadStaticFile endpoint
                string uploadUrl = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.UploadStaticFile);
                uploadUrl +=$"?fileName={Path.GetFileName(archiveFilePath)}&folderName=Shell";
                var response = ApplicationClient.UploadStaticFile(uploadUrl, archiveFilePath, "Shell");
                // string response = ApplicationClient.ExecutePostRequest(uploadUrl, formData.ToString(), RequestTimeout,
                //     RetryCount, DelaySec);

                
                // Parse response to check for success
                if (string.IsNullOrEmpty(response) || response.Contains("error", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Upload failed: {response}");
                }
                
                Logger.WriteInfo("✓ Upload completed successfully");
                
                if (options.Verbose)
                {
                    Logger.WriteInfo($"Upload response: {response}");
                }
            }
            finally
            {
                // Clean up temporary file
                if (_fileSystem.ExistsFile(archiveFilePath))
                {
                    _fileSystem.DeleteFile(archiveFilePath);
                    if (options.Verbose)
                    {
                        Logger.WriteInfo($"Cleaned up temporary file: {archiveFilePath}");
                    }
                }
            }
        }

        #endregion
    }

    #endregion
}