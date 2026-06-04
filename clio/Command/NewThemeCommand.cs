using System;
using Clio.Common;
using Clio.Package;
using CommandLine;
using FluentValidation;
using FluentValidation.Results;

namespace Clio.Command;

#region Class: NewThemeOptions

[Verb("new-theme", HelpText = "Add a new theme")]
public class NewThemeOptions : EnvironmentOptions {

	#region Properties: Public

	[Value(0, MetaName = "CssClassName", Required = true,
		HelpText = "Theme CSS class name (e.g. acme-dark-theme)")]
	public string CssClassName { get; set; }

	[Option("package", Required = true, HelpText = "Package name that will host the theme")]
	public string PackageName { get; set; }

	[Option("caption", Required = false,
		HelpText = "Theme caption (defaults to Title Case of the CSS class name)")]
	public string Caption { get; set; }

	[Option("id", Required = false, HelpText = "Theme id (defaults to a generated UUID)")]
	public string Id { get; set; }

	#endregion

}

#endregion

#region Class: NewThemeOptionsValidator

public class NewThemeOptionsValidator : AbstractValidator<NewThemeOptions> {

	#region Constants: Private

	private const string CssClassNamePattern = "^[A-Za-z][A-Za-z0-9_-]*$";
	private const string IdPattern = "^[A-Za-z0-9_-]+$";
	private const string PackageNamePattern = "^[A-Za-z0-9_]+$";
	private const int MaxCssClassNameLength = 100;
	private const int MaxIdLength = 100;
	private const int MaxCaptionLength = 250;

	#endregion

	#region Constructors: Public

	public NewThemeOptionsValidator() {
		RuleFor(x => x.CssClassName).NotEmpty().WithMessage("CSS class name is required.");
		RuleFor(x => x.CssClassName).Matches(CssClassNamePattern)
			.When(x => !string.IsNullOrEmpty(x.CssClassName))
			.WithMessage(
				"'{PropertyName}' must start with a letter and contain only letters, digits, '-' and '_'. "
				+ "You entered: '{PropertyValue}'.");
		RuleFor(x => x.CssClassName).MaximumLength(MaxCssClassNameLength)
			.When(x => !string.IsNullOrEmpty(x.CssClassName))
			.WithMessage("'{PropertyName}' must be at most {MaxLength} characters. You entered {TotalLength}.");

		RuleFor(x => x.PackageName).NotEmpty().WithMessage("Package name is required.");
		RuleFor(x => x.PackageName).Matches(PackageNamePattern)
			.When(x => !string.IsNullOrEmpty(x.PackageName))
			.WithMessage(
				"'{PropertyName}' must contain only letters, digits and underscores. You entered: '{PropertyValue}'. "
				+ "The package name is used in folder paths, so path separators, quotes and other special characters are rejected.");

		RuleFor(x => x.Id).Matches(IdPattern)
			.When(x => !string.IsNullOrWhiteSpace(x.Id))
			.WithMessage(
				"'{PropertyName}' must contain only letters, digits, '-' and '_'. You entered: '{PropertyValue}'.");
		RuleFor(x => x.Id).MaximumLength(MaxIdLength)
			.When(x => !string.IsNullOrWhiteSpace(x.Id))
			.WithMessage("'{PropertyName}' must be at most {MaxLength} characters. You entered {TotalLength}.");

		RuleFor(x => x.Caption).MaximumLength(MaxCaptionLength)
			.When(x => !string.IsNullOrWhiteSpace(x.Caption));
	}

	#endregion

}

#endregion

#region Class: NewThemeCommand

internal class NewThemeCommand : Command<NewThemeOptions> {

	#region Fields: Private

	private readonly IThemeCreator _themeCreator;
	private readonly IValidator<NewThemeOptions> _optionsValidator;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	public NewThemeCommand(IThemeCreator themeCreator, IValidator<NewThemeOptions> optionsValidator,
		ILogger logger) {
		themeCreator.CheckArgumentNull(nameof(themeCreator));
		optionsValidator.CheckArgumentNull(nameof(optionsValidator));
		_themeCreator = themeCreator;
		_optionsValidator = optionsValidator;
		_logger = logger;
	}

	#endregion

	#region Methods: Private

	private bool EnableDownloadPackage(string packageName) {
		_logger.WriteInfo($"Do you want to download package [{packageName}]? (y/n):");
		string result;
		do {
			result = Console.ReadLine()?.Trim().ToLower();
		} while (result != "y" && result != "n");
		return result == "y";
	}

	#endregion

	#region Methods: Public

	public override int Execute(NewThemeOptions options) {
		try {
			ValidationResult validationResult = _optionsValidator.Validate(options);
			if (!validationResult.IsValid) {
				foreach (ValidationFailure error in validationResult.Errors) {
					_logger.WriteError(error.ErrorMessage);
				}
				return 1;
			}
			ThemeIdentifiers identifiers = _themeCreator.CreateTheme(options.CssClassName, options.PackageName,
				options.Caption, options.Id, options.IsSilent ? _ => false : EnableDownloadPackage);
			_logger.WriteInfo(
				$"Theme '{identifiers.Caption}' (id: {identifiers.Id}) created in package '{options.PackageName}'.");
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
