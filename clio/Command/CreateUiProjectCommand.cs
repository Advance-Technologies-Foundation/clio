using System;
using Clio.Common;
using Clio.Package;
using CommandLine;
using FluentValidation;
using FluentValidation.Results;

namespace Clio.Command;

#region Class: CreateUiProjectOptions

[Verb("new-ui-project", Aliases = ["ui", "create-ui-project", "new-ui", "createup", "uiproject"],
	HelpText = "Add new UI project")]
public class CreateUiProjectOptions : EnvironmentOptions {

	#region Properties: Public

	[Option("version", Required = false, Default = "",
		HelpText = "Creatio version")]
	public string CreatioVersion { get; set; }

	[Option("empty", Required = false, Default = false,
		HelpText = "Create empty package")]
	public bool IsEmpty { get; set; }

	[Option("package", Required = true,
		HelpText = "Package name")]
	public string PackageName { get; set; }

	[Value(0, MetaName = "Name", Required = true, HelpText = "Project name")]
	public string ProjectName { get; set; }

	[Option('v', "vendor-prefix", Required = true,
		HelpText = "Vendor prefix")]
	public string VendorPrefix { get; set; }

	#endregion

}

#endregion

#region Class: CreateUiProjectOptionsValidator

public class CreateUiProjectOptionsValidator : AbstractValidator<CreateUiProjectOptions> {

	#region Constructors: Public

	public CreateUiProjectOptionsValidator(){
		RuleFor(x => x.ProjectName).NotEmpty().WithMessage("Project name is required.");
		RuleFor(x => x.VendorPrefix).Matches("^[a-z]{1,50}$")
			.WithMessage(
				"'{PropertyName}' must be between 1 and 50 characters long and contain only lowercase letters and digits. You entered: '{PropertyValue}'."
				+ Environment.NewLine +
				"See more: https://academy.creatio.com/docs/developer/front_end_development_freedom_ui/remote_module/implement_a_remote_module/overview");
		RuleFor(x => x.PackageName).NotEmpty().WithMessage("Package name is required.");
	}

	#endregion

}

#endregion

#region Class: CreateUiProjectCommand

internal class CreateUiProjectCommand {

	#region Fields: Private

	private readonly IUiProjectCreator _uiProjectCreator;
	private readonly IValidator<CreateUiProjectOptions> _optionsValidator;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	public CreateUiProjectCommand(IUiProjectCreator uiProjectCreator,
		IValidator<CreateUiProjectOptions> optionsValidator, ILogger logger){
		uiProjectCreator.CheckArgumentNull(nameof(uiProjectCreator));
		optionsValidator.CheckArgumentNull(nameof(optionsValidator));
		_uiProjectCreator = uiProjectCreator;
		_optionsValidator = optionsValidator;
		_logger = logger;
	}

	#endregion

	#region Methods: Private

	private bool EnableDownloadPackage(string packageName){
		_logger.WriteInfo($"Do you wont download package [{packageName}] ? (y/n):");
		string result;
		do {
			result = Console.ReadLine()?.Trim().ToLower();
		} while (result != "y" && result != "n");
		return result == "y";
	}

	#endregion

	#region Methods: Public

	public int Execute(CreateUiProjectOptions options){
		try {
			ValidationResult result = _optionsValidator.Validate(options);
			if (!result.IsValid) {
				foreach (ValidationFailure error in result.Errors) {
					_logger.WriteError(error.ErrorMessage);
				}
				return 1;
			}
			_uiProjectCreator.Create(options.ProjectName, options.PackageName, options.VendorPrefix,
				options.IsEmpty, options.CreatioVersion, options.IsSilent ? a => false : EnableDownloadPackage);
			_logger.WriteInfo("Done");
			return 0;
		} catch (Exception e) {
			_logger.WriteError(e.Message);
			return 1;
		}
	}

	#endregion

}

#endregion
