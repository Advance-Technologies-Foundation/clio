using System;
using System.Collections.Generic;
using Clio.Common;
using CommandLine;
using FluentValidation;
using FluentValidation.Results;

namespace Clio.Command;

public class UninstallCreatioCommandOptionsValidator : AbstractValidator<UninstallCreatioCommandOptions>
{

	#region Constructors: Public

	public UninstallCreatioCommandOptionsValidator(IFileSystem fileSystem){
		RuleFor(o => string.IsNullOrWhiteSpace(o.PhysicalPath) && string.IsNullOrWhiteSpace(o.EnvironmentName))
			.Cascade(CascadeMode.Stop)
			.Custom((value, context) => {
				if (value) {
					context.AddFailure(new ValidationFailure {
						ErrorCode = "ArgumentParse.Error",
						ErrorMessage = "Either path to creatio directory or environment name must be provided",
						Severity = Severity.Error
					});
				}
			});

		RuleFor(o => !string.IsNullOrWhiteSpace(o.PhysicalPath) && !string.IsNullOrWhiteSpace(o.EnvironmentName))
			.Cascade(CascadeMode.Stop)
			.Custom((value, context) => {
				if (value) {
					context.AddFailure(new ValidationFailure {
						ErrorCode = "ArgumentParse.Error",
						ErrorMessage
							= "Either environment name or path to creatio directory must be provided, not both",
						Severity = Severity.Error
					});
				}
			});

		RuleFor(o => o.PhysicalPath)
			.Cascade(CascadeMode.Stop)
			.Custom((value, context) => {
				if (string.IsNullOrWhiteSpace(value)) {
					return;
				}
				bool isUri = Uri.TryCreate(value, UriKind.Absolute, out Uri dirPath);
				if (!isUri || dirPath.Scheme != Uri.UriSchemeFile) {
					context.AddFailure(new ValidationFailure {
						ErrorCode = "ArgumentParse.Error",
						ErrorMessage = "PhysicalPath must be a valid directory path",
						Severity = Severity.Error,
						AttemptedValue = value
					});
				}
			})
			.Custom((value, context) => {
				if (string.IsNullOrWhiteSpace(value)) {
					return;
				}
				bool isUri = Uri.TryCreate(value, UriKind.Absolute, out Uri dirPath);
				if (!isUri || dirPath.Scheme != Uri.UriSchemeFile) {
					return;
				}

				bool dirExists = fileSystem.ExistsDirectory(value);
				if (!dirExists) {
					context.AddFailure(new ValidationFailure {
						ErrorCode = "ArgumentParse.Error",
						ErrorMessage = "PhysicalPath must be a valid directory path to an Existing directory",
						Severity = Severity.Error,
						AttemptedValue = value
					});
				}
			});
	}

	#endregion

}

[Verb("uninstall-creatio", Aliases = ["uc"], HelpText = "Uninstall local instance of creatio")]
public class UninstallCreatioCommandOptions : EnvironmentNameOptions
{

	#region Properties: Public

	[Option('d', "physicalPath", Required = false, HelpText = "Path to applications")]
	public string PhysicalPath { get; set; }

	#endregion

}

public class UninstallCreatioCommand : Command<UninstallCreatioCommandOptions>
{

	#region Fields: Private

	private readonly IValidator<UninstallCreatioCommandOptions> _validator;
	private readonly ILogger _logger;
	private readonly ICreatioUninstaller _creatioUninstaller;

	#endregion

	#region Constructors: Public

	public UninstallCreatioCommand(IValidator<UninstallCreatioCommandOptions> validator, 
		ILogger logger, ICreatioUninstaller creatioUninstaller){
		_validator = validator;
		_logger = logger;
		_creatioUninstaller = creatioUninstaller;
	}

	#endregion

	#region Methods: Private

	private int PrintDoneAndExit(UninstallCreatioCommandOptions options){
		if (options.EnvironmentName is not null) {
			_logger.WriteInfo($"Done removing Creatio instance by name: {options.EnvironmentName}");
		}
		if (options.PhysicalPath is not null) {
			_logger.WriteInfo($"Done removing Creatio instance by PhysicalPath: {options.PhysicalPath}");
		}
		return 0;
	}

	private int PrintErrorsAndExit(List<ValidationFailure> errors){
		_logger.PrintValidationFailureErrors(errors);
		return 1;
	}

	#endregion

	#region Methods: Public

	public override int Execute(UninstallCreatioCommandOptions options){
		ValidationResult validationResult = _validator.Validate(options);
		if (!validationResult.IsValid) {
			return PrintErrorsAndExit(validationResult.Errors);
		}
		
		Action act = options switch {
			var _ when options.PhysicalPath is not null => () => _creatioUninstaller.UninstallByPath(options.PhysicalPath),
			var _ when options.EnvironmentName is not null => () => _creatioUninstaller.UninstallByEnvironmentName(options.EnvironmentName),
			var _ => throw new ArgumentOutOfRangeException(nameof(options), "Either PhysicalPath or EnvironmentName must be provided")
		};
		act.Invoke();
		
		return PrintDoneAndExit(options);
	}

	#endregion

}
