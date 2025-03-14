using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Requests;
using Clio.UserEnvironment;
using CommandLine;
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace Clio.Command;

[Verb("link-from-repository", Aliases = ["l4r", "link4repo"], HelpText = "Link repository package(s) to environment.")]
public class Link4RepoOptions : EnvironmentOptions {

	#region Properties: Public

	[Option("envPkgPath", Required = false,
		HelpText
			= @"Path to environment package folder ({LOCAL_CREATIO_PATH}Terrasoft.WebApp\Terrasoft.Configuration\Pkg)",
		Default = null)]
	public string EnvPkgPath { get; set; }

	[Option("packages", Required = false, HelpText = "Package(s)", Default = null)]
	public string Packages { get; set; }

	[Option("repoPath", Required = true,
		HelpText = "Path to package repository folder", Default = null)]
	public string RepoPath { get; set; }

	#endregion

}

public class Link4RepoOptionsValidator : AbstractValidator<Link4RepoOptions> {

	#region Constructors: Public
	
	public Link4RepoOptionsValidator(){
		RuleFor(o => string.IsNullOrWhiteSpace(o.EnvPkgPath) && string.IsNullOrWhiteSpace(o.Environment))
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
	}

	#endregion

}

public class Link4RepoCommand : Command<Link4RepoOptions> {

	#region Fields: Private

	private readonly ILogger _logger;
	private readonly IMediator _mediator;
	private readonly ISettingsRepository _settingsRepository;
	private readonly IFileSystem _fileSystem;
	private readonly IValidator<Link4RepoOptions> _validator;

	#endregion

	#region Constructors: Public

	public Link4RepoCommand(ILogger logger, IMediator mediator, ISettingsRepository settingsRepository,
		IFileSystem fileSystem, IValidator<Link4RepoOptions> validator){
		_logger = logger;
		_mediator = mediator;
		_settingsRepository = settingsRepository;
		_fileSystem = fileSystem;
		_validator = validator;
	}

	#endregion

	#region Properties: Private

	private IEnumerable<IISScannerHandler.RegisteredSite> AllSites { get; set; }

	/// <summary>
	///  Gets the action to handle the completion of the request for all registered sites.
	/// </summary>
	/// <remarks>
	///  This property performs the following steps:
	///  <list type="bullet">
	///   <item>Assigns the provided sites to the `AllSites` property.</item>
	///  </list>
	/// </remarks>
	private Action<IEnumerable<IISScannerHandler.RegisteredSite>> OnAllSitesRequestCompleted =>
		sites => { AllSites = sites; };

	#endregion

	#region Methods: Private

	/// <summary>
	///  Executes a mediator request to retrieve all registered sites.
	/// </summary>
	/// <param name="callback">The callback action to handle the retrieved sites.</param>
	/// <remarks>
	///  This method performs the following steps:
	///  <list type="bullet">
	///   <item>Creates a new `AllRegisteredSitesRequest` with the provided callback.</item>
	///   <item>Executes the request asynchronously using the mediator.</item>
	///   <item>Configures the task to not capture the current synchronization context.</item>
	///   <item>Waits for the task to complete and retrieves the result.</item>
	///  </list>
	/// </remarks>
	private void ExecuteMediatorRequest(Action<IEnumerable<IISScannerHandler.RegisteredSite>> callback){
		AllRegisteredSitesRequest request = new() {
			Callback = callback
		};

		Task.Run(async () => { await _mediator.Send(request); })
			.ConfigureAwait(false)
			.GetAwaiter()
			.GetResult();
	}

	private int HandleEnvironmentOption(Link4RepoOptions options){
		_ = Uri.TryCreate(options.Environment, UriKind.Absolute, out Uri pathUri);
		return pathUri switch {
					not null when pathUri.IsFile => HandleLinkWithDirPath(options.Environment, options.RepoPath,
						options.Packages),
					var _ => HandleLinkingByEnvName(options.Environment, options.RepoPath, options.Packages)
				};
	}

	private int HandleEnvPkgPathOptions(Link4RepoOptions options){
		_ = Uri.TryCreate(options.EnvPkgPath, UriKind.Absolute, out Uri pathUri);
		return pathUri switch {
					not null when pathUri.IsFile => HandleLinkWithDirPath(options.EnvPkgPath, options.RepoPath,
						options.Packages),
					var _ => HandleLinkingByEnvName(options.EnvPkgPath, options.RepoPath, options.Packages)
				};
	}

	/// <summary>
	///  Handles the linking of repository packages to the environment by environment name.
	/// </summary>
	/// <param name="envName">The name of the environment.</param>
	/// <param name="repoPath">The path to the package repository folder.</param>
	/// <param name="packages">The packages to link.</param>
	/// <returns>
	///  <list type="bullet">
	///   <item>0 if the linking is successful.</item>
	///   <item>1 if the environment is not registered or the URL is missing.</item>
	///  </list>
	/// </returns>
	private int HandleLinkingByEnvName(string envName, string repoPath, string packages){
		ExecuteMediatorRequest(OnAllSitesRequestCompleted); //This fills AllSites property with all registered sites.
		if (!_settingsRepository.IsEnvironmentExists(envName)) {
			_logger.WriteError(
				$"Environment {envName} is not a registered environment. Please correct environment name an try again.");
			return 1;
		}

		bool isEnvUri = Uri.TryCreate(_settingsRepository.GetEnvironment(envName).Uri ?? string.Empty, UriKind.Absolute,
			out Uri envUri);
		if (!isEnvUri) {
			_logger.WriteError($"Environment {envName} missing Url. Please correct environment name an try again.");
			return 1;
		}

		List<IISScannerHandler.RegisteredSite> sites = AllSites
														.Where(s => s.Uris.Any(iisRegisteredUrl =>
															Uri.Compare(envUri,
																iisRegisteredUrl,
																UriComponents.StrongAuthority,
																UriFormat.SafeUnescaped,
																StringComparison.InvariantCulture) == 0)).ToList();
		if (sites.Count == 1) {
			IISScannerHandler.RegisteredSite site = sites[0];
			string tSitePath = site.siteType switch {
									IISScannerHandler.SiteType.NetFramework => Path.Join(site.siteBinding.path,
										"Terrasoft.WebApp", "Terrasoft.Configuration", "Pkg"),
									var _ => Path.Join(site.siteBinding.path, "Terrasoft.Configuration", "Pkg")
								};

			if (_fileSystem.ExistsDirectory(tSitePath)) {
				return HandleLinkWithDirPath(tSitePath, repoPath, packages);
			}
		}
		_logger.WriteError($"Environment {envName} not found. Please check the environment name and try again.");
		return 1;
	}

	/// <summary>
	///  Handles the linking of repository packages to the environment by directory path.
	/// </summary>
	/// <param name="sitePath">The path to the site directory.</param>
	/// <param name="repoPath">The path to the package repository folder.</param>
	/// <param name="packages">The packages to link.</param>
	/// <returns>
	///  <list type="bullet">
	///   <item>0 if the linking is successful.</item>
	///   <item>1 if the linking fails.</item>
	///  </list>
	/// </returns>
	private int HandleLinkWithDirPath(string sitePath, string repoPath, string packages){
		RfsEnvironment.Link4Repo(sitePath, repoPath, packages);
		_logger.WriteInfo($"Linking repository package(s) to environment {sitePath} from {repoPath}");
		return 0;
	}

	private int PrintErrorsAndExit(IEnumerable<ValidationFailure> errors){
		_logger.PrintValidationFailureErrors(errors);
		return 1;
	}

	#endregion

	#region Methods: Public

	/// <summary>
	///  Executes the command to link repository packages to the environment.
	/// </summary>
	/// <param name="options">The options for the link-from-repository command.</param>
	/// <returns>
	///  <list type="bullet">
	///   <item>0 if the linking is successful.</item>
	///   <item>1 if the linking fails.</item>
	///  </list>
	/// </returns>
	/// <remarks>
	///  This method performs the following steps:
	///  <list type="bullet">
	///   <item>Checks if the current operating system is Windows.</item>
	///   <item>Attempts to create a URI from the provided environment package path.</item>
	///   <item>
	///    Calls the appropriate method to handle the linking based on whether the path is a valid URI or an environment
	///    name.
	///   </item>
	///  </list>
	/// </remarks>
	public override int Execute(Link4RepoOptions options){
		if (!OperationSystem.Current.IsWindows) {
			_logger.WriteError("Clio mklink command is only supported on: 'windows'.");
			return 1;
		}

		ValidationResult validationResult = _validator.Validate(options);
		if (!validationResult.IsValid) {
			return PrintErrorsAndExit(validationResult.Errors);
		}

		return options switch {
					not null when !string.IsNullOrWhiteSpace(options.Environment) => HandleEnvironmentOption(options),
					not null when !string.IsNullOrWhiteSpace(options.EnvPkgPath) => HandleEnvPkgPathOptions(options),
					var _ => 1
				};
	}

	#endregion

}
