using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using Clio.Common;
using Clio.UserEnvironment;
using CommandLine;
using FluentValidation;
using FluentValidation.Results;

namespace Clio.Command
{
	using Clio.Requests;

	[Verb("turn-farm-mode", Aliases = new[] { "tfm", "farm-mode" }, HelpText = "Configure IIS site for Creatio web farm deployment")]
    public class TurnFarmModeOptions : EnvironmentNameOptions
    {
        public TurnFarmModeOptions()
        {
            TenantId = "1";
            InstanceId = "AUTO";
        }

        [Option("tenant-id", Required = false, HelpText = "Tenant ID for web farm (same for all nodes)", Default = "1")]
        public string TenantId { get; set; }

        [Option("instance-id", Required = false, HelpText = "Unique instance ID for this node (use AUTO for automatic generation)", Default = "AUTO")]
        public string InstanceId { get; set; }

        [Option("site-path", Required = false, HelpText = "Physical path to IIS site (if not using environment name)")]
        public string SitePath { get; set; }
    }

    public class TurnFarmModeOptionsValidator : AbstractValidator<TurnFarmModeOptions>
    {
        public TurnFarmModeOptionsValidator()
        {
            RuleFor(o => o.TenantId)
                .NotEmpty()
                .WithMessage("Tenant ID is required");

            RuleFor(o => o.InstanceId)
                .NotEmpty()
                .WithMessage("Instance ID is required");

            RuleFor(o => o)
                .Must(o => !string.IsNullOrEmpty(o.Environment) || !string.IsNullOrEmpty(o.SitePath))
                .WithMessage("Either environment name or site path must be provided");
        }
    }

    public class TurnFarmModeCommand : Command<TurnFarmModeOptions>
    {
        private readonly IValidator<TurnFarmModeOptions> _validator;
        private readonly ISettingsRepository _settingsRepository;
        private readonly ILogger _logger;

        public TurnFarmModeCommand(IValidator<TurnFarmModeOptions> validator, ISettingsRepository settingsRepository, ILogger logger)
        {
            _validator = validator;
            _settingsRepository = settingsRepository;
            _logger = logger;
        }

        public override int Execute(TurnFarmModeOptions options)
        {
            try
            {
                ValidationResult validationResult = _validator.Validate(options);
                if (validationResult.Errors.Any())
                {
                    PrintErrors(validationResult.Errors);
                    return 1;
                }

                string sitePath = GetSitePath(options);
                if (string.IsNullOrEmpty(sitePath) || !Directory.Exists(sitePath))
                {
                    _logger.WriteError($"Site path not found: {sitePath}");
                    return 1;
                }

                _logger.WriteInfo($"Configuring web farm mode for site: {sitePath}");

                // Configure Web.config files
                ConfigureWebConfig(sitePath, options);
                ConfigureInternalWebConfig(sitePath, options);

                _logger.WriteInfo("Web farm mode configuration completed successfully");
                return 0;
            }
            catch (Exception e)
            {
                _logger.WriteError($"Error configuring web farm mode: {e.Message}");
                return 1;
            }
        }

        private string GetSitePath(TurnFarmModeOptions options)
        {
            if (!string.IsNullOrEmpty(options.SitePath))
            {
                return options.SitePath;
            }

            // Get path from environment settings
            try
            {
                EnvironmentSettings env = _settingsRepository.GetEnvironment(options.Environment);
                if (string.IsNullOrEmpty(env.Uri))
                {
                    throw new Exception($"Could not find environment: '{options.Environment}'");
                }

                IEnumerable<IISScannerHandler.UnregisteredSite> sites = IISScannerHandler.FindAllCreatioSites();
                foreach (IISScannerHandler.UnregisteredSite site in sites)
                {
                    foreach (Uri siteUri in site.Uris)
                    {
                        if (siteUri.ToString().Equals(new Uri(env.Uri).ToString(), StringComparison.OrdinalIgnoreCase))
                        {
                            return site.siteBinding.path;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.WriteError($"Error finding site path: {e.Message}");
            }

            return null;
        }

        private void ConfigureWebConfig(string sitePath, TurnFarmModeOptions options)
        {
            string webConfigPath = Path.Combine(sitePath, "Web.config");
            if (!File.Exists(webConfigPath))
            {
                _logger.WriteWarning($"Web.config not found at: {webConfigPath}");
                return;
            }

            _logger.WriteInfo($"Configuring {webConfigPath}");

            // Create backup before modifying
            string backupPath = CreateBackup(webConfigPath);
            if (!string.IsNullOrEmpty(backupPath))
            {
                _logger.WriteInfo($"Backup created: {backupPath}");
            }

            XmlDocument doc = new XmlDocument();
            doc.Load(webConfigPath);

            // Add TenantId to main Web.config as well
            ConfigureTenantId(doc, options);

            // Configure Quartz scheduler clustering
            ConfigureQuartzClustering(doc, options);

            doc.Save(webConfigPath);
            _logger.WriteInfo($"Successfully configured {webConfigPath}");
        }

        private void ConfigureInternalWebConfig(string sitePath, TurnFarmModeOptions options)
        {
            string internalWebConfigPath = Path.Combine(sitePath, "Terrasoft.WebApp", "Web.config");
            if (!File.Exists(internalWebConfigPath))
            {
                _logger.WriteWarning($"Internal Web.config not found at: {internalWebConfigPath}");
                return;
            }

            _logger.WriteInfo($"Configuring {internalWebConfigPath}");

            // Create backup before modifying
            string backupPath = CreateBackup(internalWebConfigPath);
            if (!string.IsNullOrEmpty(backupPath))
            {
                _logger.WriteInfo($"Backup created: {backupPath}");
            }

            XmlDocument doc = new XmlDocument();
            doc.Load(internalWebConfigPath);

            // Add TenantId to internal Web.config
            ConfigureTenantId(doc, options);

            doc.Save(internalWebConfigPath);
            _logger.WriteInfo($"Successfully configured {internalWebConfigPath}");
        }

        private string CreateBackup(string originalFilePath)
        {
            try
            {
                string directory = Path.GetDirectoryName(originalFilePath);
                string fileName = Path.GetFileName(originalFilePath);
                string dateStamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string backupFileName = $"before-tfm-{dateStamp}-{fileName}";
                string backupPath = Path.Combine(directory, backupFileName);

                File.Copy(originalFilePath, backupPath, true);
                return backupPath;
            }
            catch (Exception ex)
            {
                _logger.WriteWarning($"Failed to create backup for {originalFilePath}: {ex.Message}");
                return null;
            }
        }

        private void ConfigureTenantId(XmlDocument doc, TurnFarmModeOptions options)
        {
            XmlNode appSettingsNode = doc.SelectSingleNode("//appSettings");
            if (appSettingsNode == null)
            {
                // Create appSettings node if it doesn't exist
                XmlNode configNode = doc.SelectSingleNode("//configuration");
                if (configNode == null)
                {
                    _logger.WriteError("Could not find configuration node in Web.config");
                    return;
                }
                appSettingsNode = doc.CreateElement("appSettings");
                configNode.AppendChild(appSettingsNode);
                _logger.WriteInfo("Created appSettings section");
            }

            // Check if TenantId already exists
            XmlNode tenantIdNode = appSettingsNode.SelectSingleNode("add[@key='TenantId']");
            if (tenantIdNode != null)
            {
                // Update existing TenantId
                string oldValue = tenantIdNode.Attributes["value"]?.Value ?? "not set";
                tenantIdNode.Attributes["value"].Value = options.TenantId;
                _logger.WriteInfo($"Updated TenantId from '{oldValue}' to '{options.TenantId}'");
            }
            else
            {
                // Create new TenantId node
                XmlElement newTenantIdNode = doc.CreateElement("add");
                newTenantIdNode.SetAttribute("key", "TenantId");
                newTenantIdNode.SetAttribute("value", options.TenantId);
                appSettingsNode.AppendChild(newTenantIdNode);
                _logger.WriteInfo($"Added TenantId: {options.TenantId}");
            }
        }

        private void ConfigureQuartzClustering(XmlDocument doc, TurnFarmModeOptions options)
        {
            XmlNode quartzConfigNode = doc.SelectSingleNode("//quartzConfig");
            if (quartzConfigNode == null)
            {
                _logger.WriteWarning("quartzConfig section not found in Web.config");
                return;
            }

            // Find all quartz child elements within quartzConfig
            XmlNodeList quartzNodes = quartzConfigNode.SelectNodes("quartz");
            if (quartzNodes == null || quartzNodes.Count == 0)
            {
                _logger.WriteWarning("No quartz scheduler configurations found in quartzConfig section");
                return;
            }

            _logger.WriteInfo($"Found {quartzNodes.Count} Quartz scheduler configurations to update");

            // Configure clustering settings for each quartz scheduler
            foreach (XmlNode quartzNode in quartzNodes)
            {
                string schedulerName = GetSchedulerName(quartzNode);
                _logger.WriteInfo($"Configuring clustering for scheduler: {schedulerName}");

                ConfigureQuartzSetting(doc, quartzNode, "quartz.jobStore.clustered", "true");
                ConfigureQuartzSetting(doc, quartzNode, "quartz.jobStore.acquireTriggersWithinLock", "true");
                ConfigureQuartzSetting(doc, quartzNode, "quartz.scheduler.instanceId", options.InstanceId);
            }

            _logger.WriteInfo("Configured Quartz clustering settings for all schedulers");
        }

        private string GetSchedulerName(XmlNode quartzNode)
        {
            // Try to get the scheduler name from the instanceName setting
            XmlNode instanceNameNode = quartzNode.SelectSingleNode("add[@key='quartz.scheduler.instanceName']");
            if (instanceNameNode != null)
            {
                return instanceNameNode.Attributes["value"]?.Value ?? "Unknown";
            }
            return "Unknown";
        }

        private void ConfigureQuartzSetting(XmlDocument doc, XmlNode quartzNode, string key, string value)
        {
            XmlNode settingNode = quartzNode.SelectSingleNode($"add[@key='{key}']");
            if (settingNode != null)
            {
                // Update existing setting
                string oldValue = settingNode.Attributes["value"]?.Value ?? "not set";
                settingNode.Attributes["value"].Value = value;
                _logger.WriteInfo($"Updated {key} from '{oldValue}' to '{value}'");
            }
            else
            {
                // Create new setting
                XmlElement newSettingNode = doc.CreateElement("add");
                newSettingNode.SetAttribute("key", key);
                newSettingNode.SetAttribute("value", value);
                quartzNode.AppendChild(newSettingNode);
                _logger.WriteInfo($"Added {key} = {value}");
            }
        }

        private void PrintErrors(IEnumerable<ValidationFailure> errors)
        {
            errors.Select(e => new { e.ErrorMessage, e.ErrorCode, e.Severity })
                .ToList().ForEach(e =>
                {
                    _logger.WriteError($"{e.Severity.ToString().ToUpper(CultureInfo.InvariantCulture)} ({e.ErrorCode}) - {e.ErrorMessage}");
                });
        }
    }
}