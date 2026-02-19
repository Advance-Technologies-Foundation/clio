using CommandLine;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Clio.Command;

using Clio.Common;
using Clio.Requests;
using Clio.UserEnvironment;
using FluentValidation;
using FluentValidation.Results;
using System.IO;
using System.Xml;

public class SetFsmConfigOptionsValidator : AbstractValidator<SetFsmConfigOptions>
{

	#region Constructors: Public

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

[Verb("set-fsm-config", Aliases = ["fsmc", "sfsmc"], HelpText = "Set file system mode properties in config file")]
public class SetFsmConfigOptions : EnvironmentNameOptions {

	#region Properties: Public

	[Option("physicalPath", Required = false, HelpText = "Path to applications")]
	public string PhysicalPath { get; set; }

	[Value(0, MetaName="IsFsm", Required = true, HelpText = "on or off", Default = "on")]
	public string IsFsm { get; set; }


	#endregion

}

public class SetFsmConfigCommand : Command<SetFsmConfigOptions>
{

	#region Fields: Private

	private readonly IValidator<SetFsmConfigOptions> _validator;
	private readonly ISettingsRepository _settingsRepository;
	private readonly List<string[]> _changedValuesTable = [
		["Key", "Old Value", "New Value"],
		["--------------------", "---------", "---------"]
	];
	private string _webConfigFileName = "Web.config";
	
	#endregion

	#region Constructors: Public

	public SetFsmConfigCommand(IValidator<SetFsmConfigOptions> validator, ISettingsRepository settingsRepository) {
		_validator = validator;
		_settingsRepository = settingsRepository;
	}

	#endregion
	
	#region Methods: Public
	public override int Execute(SetFsmConfigOptions options) {
		bool isWindows = OperatingSystem.IsWindows();
		bool isMacOs = OperatingSystem.IsMacOS();
		if (!isWindows && !isMacOs) {
			throw new Exception("This command is only supported on Windows and macOS.");
		}

		EnvironmentSettings environmentSettings = _settingsRepository.GetEnvironment(options);
		if (environmentSettings == null) {
			throw new Exception("Could not resolve environment settings.");
		}
		if (isMacOs && !environmentSettings.IsNetCore) {
			throw new Exception("On macOS this command is supported only for NET8 (IsNetCore=true) environments.");
		}

		ValidationResult validationResult = _validator.Validate(options);
		if (validationResult.Errors.Count != 0) {
			PrintErrors(validationResult.Errors);
			return 1;
		}
		
		_ = environmentSettings.IsNetCore switch {
			true => _webConfigFileName = "Terrasoft.WebHost.dll.config",
			var _ => _webConfigFileName = "Web.config"
 		};
		
		string environmentPath = string.IsNullOrWhiteSpace(options.PhysicalPath)
			? GetWebConfigPathFromEnvName(options.Environment)
			: options.PhysicalPath;
		string webConfigPath = Path.Join(environmentPath, _webConfigFileName);
		if (File.Exists(webConfigPath)) {
			ModifyWebConfigFile(webConfigPath, options.IsFsm.ToLower(CultureInfo.InvariantCulture) == "on"); //Happy path
			return 0;
		}
		Console.WriteLine($"Config does not exist in {webConfigPath}");
		return 1;
	}

	private string GetWebConfigPathFromEnvName(string envName) {
		EnvironmentSettings env = _settingsRepository.GetEnvironment(envName);
		if (env == null) {
			throw new Exception($"Could not find path to environment: '{envName}'");
		}
		if(string.IsNullOrWhiteSpace(env.Uri)) {
			if (!OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(env.EnvironmentPath)) {
				return env.EnvironmentPath;
			}
			throw new Exception($"Could not find path to environment: '{envName}'");
		}
		if (!OperatingSystem.IsWindows()) {
			if (!string.IsNullOrWhiteSpace(env.EnvironmentPath)) {
				return env.EnvironmentPath;
			}
			throw new Exception($"Could not find EnvironmentPath to environment: '{envName}'");
		}
		IEnumerable<IISScannerHandler.UnregisteredSite> sites = IISScannerHandler.FindAllCreatioSites();
		foreach (IISScannerHandler.UnregisteredSite site in sites) {
			foreach (Uri unregisteredSiteUri in site.Uris) {
				if(unregisteredSiteUri.ToString() == new Uri(env.Uri).ToString()) {
					return site.siteBinding.path;
				}
			}
		}
		throw new Exception($"Could not find path to environment: '{envName}'");
	}

	private int ModifyWebConfigFile(string webConfigPath, bool isFsm) {
		string webConfigContent = File.ReadAllText(webConfigPath);
		XmlDocument doc = new ();
		doc.LoadXml(webConfigContent);
		XmlNode root = doc.DocumentElement;  
		XmlNode fileDesignModeNode = root
			.SelectSingleNode("descendant::terrasoft")
			.SelectSingleNode("descendant::fileDesignMode");
		string fileDesignModeNodeOldValue = fileDesignModeNode.Attributes["enabled"].Value;
		string fileDesignModeNodeNewValue = isFsm.ToString().ToLower();
		fileDesignModeNode.Attributes["enabled"].Value = fileDesignModeNodeNewValue;
		_changedValuesTable.Add(new [] {"fileDesignMode",fileDesignModeNodeOldValue,fileDesignModeNodeNewValue});
		XmlNodeList cNodes = root.SelectSingleNode("descendant::appSettings").ChildNodes;
		foreach (XmlNode cNode in cNodes) {
			if(cNode.Attributes is not null && cNode.Attributes["key"].Value == "UseStaticFileContent") {
				string useStaticFileContentOldValue = cNode.Attributes["value"].Value;
				string useStaticFileContentNewValue = (!isFsm).ToString().ToLower();
				cNode.Attributes["value"].Value  = useStaticFileContentNewValue;
				_changedValuesTable.Add(["UseStaticFileContent",useStaticFileContentOldValue,useStaticFileContentNewValue]);
			}
		}
		doc.Save(webConfigPath);
		Console.WriteLine(TextUtilities.ConvertTableToString(_changedValuesTable));
		return 0;
	}

	#endregion

}
