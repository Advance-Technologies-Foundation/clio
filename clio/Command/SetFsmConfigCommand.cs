using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using Clio.Common;
using Clio.Requests;
using Clio.UserEnvironment;
using CommandLine;
using FluentValidation;
using FluentValidation.Results;

namespace Clio.Command;

/// <summary>
/// Validates options for <c>set-fsm-config</c>.
/// </summary>
public class SetFsmConfigOptionsValidator : AbstractValidator<SetFsmConfigOptions>
{

	#region Constructors: Public

	/// <summary>
	/// Initializes validation rules for <see cref="SetFsmConfigOptions"/>.
	/// </summary>
	public SetFsmConfigOptionsValidator() {
		RuleFor(o => o.IsFsm)
			.Cascade(CascadeMode.Stop)
			.NotEmpty()
			.WithErrorCode("ArgumentParse.Error")
			.WithMessage("IsFsm must be 'on' or 'off'.")
			.Must(v => string.Equals(v?.Trim(), "on", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(v?.Trim(), "off", StringComparison.OrdinalIgnoreCase))
			.WithErrorCode("ArgumentParse.Error")
			.WithMessage("IsFsm must be 'on' or 'off'.");

		RuleFor(o => o.EnvironmentName == o.PhysicalPath).Cascade(CascadeMode.Stop)
			.Custom((value, context) => {
				bool isLoaderPathEmpty = string.IsNullOrWhiteSpace(context.InstanceToValidate.PhysicalPath);
				bool isEnvEmpty = string.IsNullOrWhiteSpace(context.InstanceToValidate.Environment);

				if (isEnvEmpty && isLoaderPathEmpty) {
					context.AddFailure(new ValidationFailure {
						ErrorCode = "ArgumentParse.Error",
						ErrorMessage = "Either physicalPath or environment name must be provided",
						Severity = Severity.Error,
						AttemptedValue = value
					});
				}
			});
	}

	#endregion

}

/// <summary>
/// Options for the <c>set-fsm-config</c> command.
/// </summary>
[Verb("set-fsm-config", Aliases = ["fsmc", "sfsmc"], HelpText = "Set file system mode properties in config file")]
public class SetFsmConfigOptions : EnvironmentNameOptions {

	#region Properties: Public

	/// <summary>
	/// Gets or sets the path to the Creatio application root folder.
	/// </summary>
	[Option("physicalPath", Required = false, HelpText = "Path to applications")]
	public string PhysicalPath { get; set; }

	/// <summary>
	/// Gets or sets the target FSM mode.
	/// </summary>
	[Value(0, MetaName = "IsFsm", Required = true, HelpText = "on or off", Default = "on")]
	public string IsFsm { get; set; }

	#endregion

}

/// <summary>
/// Updates FSM-related values in a local Creatio configuration file.
/// </summary>
public class SetFsmConfigCommand : Command<SetFsmConfigOptions>
{

	#region Fields: Private

	private const string NetFrameworkConfigFileName = "Web.config";
	private const string NetCoreConfigFileName = "Terrasoft.WebHost.dll.config";

	private readonly IValidator<SetFsmConfigOptions> _validator;
	private readonly ISettingsRepository _settingsRepository;
	private readonly IFileSystem _fileSystem;
	private readonly ILogger _logger;
	private readonly IIISScanner _iisScanner;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="SetFsmConfigCommand"/> class.
	/// </summary>
	/// <param name="validator">Options validator.</param>
	/// <param name="settingsRepository">Environment settings repository.</param>
	/// <param name="fileSystem">Filesystem abstraction used to resolve and update config files.</param>
	/// <param name="logger">Logger used for command output.</param>
	/// <param name="iisScanner">IIS scanner for discovering Creatio sites.</param>
	public SetFsmConfigCommand(IValidator<SetFsmConfigOptions> validator, ISettingsRepository settingsRepository,
		IFileSystem fileSystem, ILogger logger, IIISScanner iisScanner) {
		_validator = validator;
		_settingsRepository = settingsRepository;
		_fileSystem = fileSystem;
		_logger = logger;
		_iisScanner = iisScanner;
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc />
	public override int Execute(SetFsmConfigOptions options) {
		ValidationResult validationResult = _validator.Validate(options);
		if (validationResult.Errors.Count != 0) {
			PrintErrors(validationResult.Errors);
			return 1;
		}

		EnvironmentSettings environmentSettings = TryGetEnvironmentSettings(options);
		ValidatePlatformSupport(environmentSettings);

		string environmentPath = ResolveEnvironmentPath(options, environmentSettings);
		bool isFsm = string.Equals(options.IsFsm?.Trim(), "on", StringComparison.OrdinalIgnoreCase);
		string webConfigPath = ResolveExistingConfigPath(environmentPath, environmentSettings);
		if (!string.IsNullOrWhiteSpace(webConfigPath)) {
			ModifyWebConfigFile(webConfigPath, isFsm);
			return 0;
		}

		string searchedConfigs = string.Join(", ", GetConfigCandidates(environmentSettings));
		_logger.WriteLine($"Config does not exist in {environmentPath}. Checked: {searchedConfigs}");
		return 1;
	}

	#endregion

	#region Methods: Private

	private static List<string[]> CreateChangedValuesTable() {
		return [
			["Key", "Old Value", "New Value"],
			["--------------------", "---------", "---------"]
		];
	}

	private IEnumerable<string> GetConfigCandidates(EnvironmentSettings environmentSettings) {
		if (environmentSettings?.IsNetCore == true) {
			return [NetCoreConfigFileName, NetFrameworkConfigFileName];
		}

		if (environmentSettings?.IsNetCore == false) {
			return [NetFrameworkConfigFileName, NetCoreConfigFileName];
		}

		return OperatingSystem.IsWindows()
			? [NetFrameworkConfigFileName, NetCoreConfigFileName]
			: [NetCoreConfigFileName, NetFrameworkConfigFileName];
	}

	private string GetWebConfigPathFromEnvName(string envName) {
		EnvironmentSettings env = _settingsRepository.GetEnvironment(envName);
		if (env == null) {
			throw new Exception($"Could not find path to environment: '{envName}'");
		}

		if (!string.IsNullOrWhiteSpace(env.EnvironmentPath)) {
			return env.EnvironmentPath;
		}

		if (string.IsNullOrWhiteSpace(env.Uri)) {
			throw new Exception($"Could not find path to environment: '{envName}'");
		}

		IEnumerable<UnregisteredSite> sites = _iisScanner.FindAllCreatioSites();
		foreach (UnregisteredSite site in sites) {
			foreach (Uri unregisteredSiteUri in site.Uris) {
				if (unregisteredSiteUri.ToString() == new Uri(env.Uri).ToString()) {
					return site.siteBinding.path;
				}
			}
		}

		throw new Exception($"Could not find path to environment: '{envName}'");
	}

	private int ModifyWebConfigFile(string webConfigPath, bool isFsm) {
		string webConfigContent = _fileSystem.ReadAllText(webConfigPath);
		XmlDocument doc = new();
		doc.LoadXml(webConfigContent);
		XmlNode root = doc.DocumentElement;
		List<string[]> changedValuesTable = CreateChangedValuesTable();
		XmlNode fileDesignModeNode = root
			.SelectSingleNode("descendant::terrasoft")
			.SelectSingleNode("descendant::fileDesignMode");
		string fileDesignModeNodeOldValue = fileDesignModeNode.Attributes["enabled"].Value;
		string fileDesignModeNodeNewValue = isFsm.ToString().ToLower(CultureInfo.InvariantCulture);
		fileDesignModeNode.Attributes["enabled"].Value = fileDesignModeNodeNewValue;
		changedValuesTable.Add(["fileDesignMode", fileDesignModeNodeOldValue, fileDesignModeNodeNewValue]);
		XmlNodeList cNodes = root.SelectSingleNode("descendant::appSettings").ChildNodes;
		foreach (XmlNode cNode in cNodes) {
			if (cNode.Attributes is not null && cNode.Attributes["key"].Value == "UseStaticFileContent") {
				string useStaticFileContentOldValue = cNode.Attributes["value"].Value;
				string useStaticFileContentNewValue = (!isFsm).ToString().ToLower(CultureInfo.InvariantCulture);
				cNode.Attributes["value"].Value = useStaticFileContentNewValue;
				changedValuesTable.Add(["UseStaticFileContent", useStaticFileContentOldValue, useStaticFileContentNewValue]);
			}
		}

		_fileSystem.WriteAllTextToFile(webConfigPath, doc.OuterXml);
		_logger.WriteLine(TextUtilities.ConvertTableToString(changedValuesTable));
		return 0;
	}

	private string ResolveEnvironmentPath(SetFsmConfigOptions options, EnvironmentSettings environmentSettings) {
		if (!string.IsNullOrWhiteSpace(options.PhysicalPath)) {
			return options.PhysicalPath;
		}

		if (string.IsNullOrWhiteSpace(options.Environment)) {
			throw new Exception("Either physicalPath or environment name must be provided");
		}

		if (!string.IsNullOrWhiteSpace(environmentSettings?.EnvironmentPath)) {
			return environmentSettings.EnvironmentPath;
		}

		return GetWebConfigPathFromEnvName(options.Environment);
	}

	private string ResolveExistingConfigPath(string environmentPath, EnvironmentSettings environmentSettings) {
		foreach (string configFileName in GetConfigCandidates(environmentSettings)) {
			string candidatePath = _fileSystem.Combine(environmentPath, configFileName);
			if (_fileSystem.ExistsFile(candidatePath)) {
				return candidatePath;
			}
		}

		return null;
	}

	private EnvironmentSettings TryGetEnvironmentSettings(SetFsmConfigOptions options) {
		return _settingsRepository.GetEnvironment(options);
	}

	private static void ValidatePlatformSupport(EnvironmentSettings environmentSettings) {
		if (OperatingSystem.IsWindows()) {
			return;
		}

		if (environmentSettings is not null && !environmentSettings.IsNetCore) {
			throw new Exception("On macOS and Linux this command is supported only for NET8 (IsNetCore=true) environments.");
		}
	}

	#endregion

}
