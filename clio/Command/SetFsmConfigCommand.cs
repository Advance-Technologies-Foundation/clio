using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Command;

using Clio.Common;
using Clio.Requests;
using Clio.UserEnvironment;
using FluentValidation;
using FluentValidation.Results;
using System.IO;
using System.Runtime.InteropServices;
using System.Xml;

public class SetFsmConfigOptionsValidator : AbstractValidator<SetFsmConfigOptions>
{

	#region Constructors: Public

	public SetFsmConfigOptionsValidator() {
		RuleFor(o => o.EnvironmentName == o.PhysicalPath).Cascade(CascadeMode.Stop)
			.Custom((value, context) =>
			{
				if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && string.IsNullOrWhiteSpace(context.InstanceToValidate.PhysicalPath))
				{
					context.AddFailure(new ValidationFailure
					{
						ErrorCode = "OS001",
						ErrorMessage = $"Not supported OS Platform - When on {OSPlatform.Windows} user LoaderPath option instead of environment name",
						Severity = Severity.Error,
						AttemptedValue = value,
					});
				}
			})
			.Custom((value, context) => {
				bool isLoaderPathEmpty = string.IsNullOrWhiteSpace(context.InstanceToValidate.PhysicalPath);
				bool isEnvEmpty = string.IsNullOrWhiteSpace(context.InstanceToValidate.Environment);

				if (isEnvEmpty && isLoaderPathEmpty) {
					context.AddFailure(new ValidationFailure {
						ErrorCode = "ArgumentParse.Error",
						ErrorMessage = "Either loacalpath or environment name must be provided",
						Severity = Severity.Error,
						AttemptedValue = value
					});
				}
			});
	}

	#endregion

}

[Verb("set-fsm-config", Aliases = new[] {"fsmc", "sfsmc"}, HelpText = "Set file system mode properties in config file")]
public class SetFsmConfigOptions : EnvironmentNameOptions
{

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
	private readonly IList<string[]> _changedValuesTable;
	private const string WebConfingFileName = "Web.config";

	#endregion

	#region Constructors: Public

	public SetFsmConfigCommand(IValidator<SetFsmConfigOptions> validator, ISettingsRepository settingsRepository) {
		_validator = validator;
		_settingsRepository = settingsRepository;
		_changedValuesTable = new List<string[]> {
			new [] {"Key", "Old Value", "New Value" },
			new [] {"--------------------", "---------", "---------" }
		};
	}

	#endregion

	#region Methods: Private

	private static void PrintErrors(IEnumerable<ValidationFailure> errors) {
		errors.Select(e => new { e.ErrorMessage, e.ErrorCode, e.Severity })
			.ToList().ForEach(e =>
			{
				Console.WriteLine($"{e.Severity.ToString().ToUpper()} ({e.ErrorCode}) - {e.ErrorMessage}");
			});
	}

	#endregion

	#region Methods: Public
	public override int Execute(SetFsmConfigOptions options) {
		ValidationResult validationResult = _validator.Validate(options);
		if (validationResult.Errors.Any()) {
			PrintErrors(validationResult.Errors);
			return 1;
		}
		string webConfigPath = string.IsNullOrWhiteSpace(options.PhysicalPath) ? 
			Path.Join(GetWebConfigPathFromEnvName(options.Environment), WebConfingFileName) //Searches IIS registered sites
			: Path.Join(options.PhysicalPath, WebConfingFileName);
		if (File.Exists(webConfigPath)) {
			ModifyWebConfigFile(webConfigPath, options.IsFsm.ToLower() == "on"); //Happy path
			return 0;
		}
		Console.WriteLine($"Config does not exist in {webConfigPath}");
		return 1;
	}

	private string GetWebConfigPathFromEnvName(string envName) {
		EnvironmentSettings env = _settingsRepository.GetEnvironment(envName);
		if(string.IsNullOrWhiteSpace(env.Uri)) {
			throw new Exception($"Could not find path to environment: '{envName}'");
		}
		IEnumerable<IISScannerHandler.UnregisteredSite> sites = IISScannerHandler._findAllCreatioSites();
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
				_changedValuesTable.Add(new [] {"UseStaticFileContent",useStaticFileContentOldValue,useStaticFileContentNewValue});
			}
		}
		doc.Save(webConfigPath);
		Console.WriteLine(TextUtilities.ConvertTableToString(_changedValuesTable));
		return 0;
	}

	#endregion

}