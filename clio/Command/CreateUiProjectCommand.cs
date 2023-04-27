namespace Clio.Command
{
	using Clio.Common;
	using Clio.Package;
	using CommandLine;
	using FluentValidation;
	using System;
	using System.ComponentModel.DataAnnotations;

	#region Class: CreateUiProjectOptions

	[Verb("new-ui-project", Aliases = new string[] { "create-ui-project", "new-ui", "createup", "uiproject", "ui" },
		HelpText = "Add new UI project")]
	public class CreateUiProjectOptions : EnvironmentOptions
	{

		#region Properties: Public

		[Value(0, MetaName = "Name", Required = true, HelpText = "Project name")]
		public string ProjectName {
			get; set;
		}

		[Option('v', "vendor-prefix", Required = true,
			HelpText = "Vendor prefix")]
		public string VendorPrefix {
			get; set;
		}

		[Option("package", Required = true,
			HelpText = "Package name")]
		public string PackageName {
			get; set;
		}

		[Option("empty", Required = false, Default = false,
			HelpText = "Create empty package")]
		public bool IsEmpty {
			get; set;
		}


		#endregion

	}

	#endregion

	#region Class: CreateUiProjectOptionsValidator

	public class CreateUiProjectOptionsValidator : AbstractValidator<CreateUiProjectOptions>
	{
		public CreateUiProjectOptionsValidator() {
			RuleFor(x => x.ProjectName).NotEmpty().WithMessage("Project name is required.");
			RuleFor(x => x.VendorPrefix).Matches("^[a-z]{1,50}$")
				.WithMessage("'{PropertyName}' must be between 1 and 50 characters long and contain only lowercase letters and digits. You entered: '{PropertyValue}'."
				+ Environment.NewLine + "See more: https://academy.creatio.com/docs/developer/front_end_development_freedom_ui/remote_module/implement_a_remote_module/overview");
			RuleFor(x => x.PackageName).NotEmpty().WithMessage("Package name is required.");
		}
	}

	#endregion

	#region Class: CreateUiProjectCommand

	internal class CreateUiProjectCommand
	{

		#region Fields: Private

		private readonly IUiProjectCreator _uiProjectCreator;
		private readonly IValidator<CreateUiProjectOptions> _optionsValidator;

		#endregion

		#region Constructors: Public

		public CreateUiProjectCommand(IUiProjectCreator uiProjectCreator, IValidator<CreateUiProjectOptions> optionsValidator)
		{
			uiProjectCreator.CheckArgumentNull(nameof(uiProjectCreator));
			optionsValidator.CheckArgumentNull(nameof(optionsValidator));
			_uiProjectCreator = uiProjectCreator;
			_optionsValidator = optionsValidator;
		}

		#endregion

		#region Methods: Private

		private static bool EnableDownloadPackage(string packageName)
		{
			Console.WriteLine($"Do you wont download package [{packageName}] ? (y/n):");
			string result;
			do
			{
				result = Console.ReadLine().Trim().ToLower();
			} while (result != "y" && result != "n");
			return result == "y";
		}

		#endregion

		#region Methods: Public

		public int Execute(CreateUiProjectOptions options)
		{
			try
			{
				var result = _optionsValidator.Validate(options);
				if (!result.IsValid) {
					foreach (var error in result.Errors) {
						Console.WriteLine(error.ErrorMessage);
					}
					return 1;
				}
				_uiProjectCreator.Create(options.ProjectName, options.PackageName, options.VendorPrefix,
					options.IsEmpty, (options.IsSilent ? (a) => { return false; }
				: EnableDownloadPackage));
				Console.WriteLine("Done");
				return 0;
			}
			catch (Exception e)
			{
				Console.WriteLine(e.Message);
				return 1;
			}
		}

		#endregion

	}

	#endregion

}
