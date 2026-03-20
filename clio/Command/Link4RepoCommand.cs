using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Requests;
using Clio.UserEnvironment;
using CommandLine;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using static Clio.Common.OperationSystem;

namespace Clio.Command;

/// <summary>
/// Options for the <c>link-from-repository</c> command.
/// </summary>
[Verb("link-from-repository", Aliases = ["l4r", "link4repo"], HelpText = "Link repository package(s) to environment.")]
public class Link4RepoOptions : EnvironmentOptions {

	#region Properties: Public

	/// <summary>
	/// Gets or sets the direct path to the environment package folder.
	/// </summary>
	[Option("envPkgPath", Required = false,
		HelpText
			= @"Path to environment package folder ({LOCAL_CREATIO_PATH}Terrasoft.WebApp\Terrasoft.Configuration\Pkg)",
		Default = null)]
	public string EnvPkgPath { get; set; }

	/// <summary>
	/// Gets or sets the package selector.
	/// </summary>
	[Option("packages", Required = false, HelpText = "Package(s)", Default = null)]
	public string Packages { get; set; }

	/// <summary>
	/// Gets or sets the repository path that contains workspace packages.
	/// </summary>
	[Option("repoPath", Required = true,
		HelpText = "Path to package repository folder", Default = null)]
	public string RepoPath { get; set; }

	#endregion

}

/// <summary>
/// Validates options for <see cref="Link4RepoOptions"/>.
/// </summary>
public class Link4RepoOptionsValidator : AbstractValidator<Link4RepoOptions> {

	#region Constructors: Public
	
	/// <summary>
	/// Initializes validation rules for <see cref="Link4RepoOptions"/>.
	/// </summary>
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

/// <summary>
/// Links workspace packages into a local Creatio environment package directory.
/// </summary>
public class Link4RepoCommand : Command<Link4RepoOptions> {

	#region Fields: Private

	private readonly ILogger _logger;
	private readonly IMediator _mediator;
	private readonly ISettingsRepository _settingsRepository;
	private readonly IFileSystem _fileSystem;
	private readonly RfsEnvironment _rfsEnvironment;
	private readonly IValidator<Link4RepoOptions> _validator;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="Link4RepoCommand"/> class.
	/// </summary>
	/// <param name="logger">Logger used for command output.</param>
	/// <param name="mediator">Mediator used for IIS site discovery.</param>
	/// <param name="settingsRepository">Environment settings repository.</param>
	/// <param name="fileSystem">Filesystem abstraction used for path resolution.</param>
	/// <param name="rfsEnvironment">Repository-to-environment linker.</param>
	/// <param name="validator">Command options validator.</param>
	public Link4RepoCommand(ILogger logger, IMediator mediator, ISettingsRepository settingsRepository,
		IFileSystem fileSystem, RfsEnvironment rfsEnvironment, IValidator<Link4RepoOptions> validator){
		_logger = logger;
		_mediator = mediator;
		_settingsRepository = settingsRepository;
		_fileSystem = fileSystem;
		_rfsEnvironment = rfsEnvironment;
		_validator = validator;
	}

	#endregion

	#region Properties: Private

	private IEnumerable<IISScannerHandler.RegisteredSite> AllSites { get; set; }

	/// <summary>
	/// Gets the action that stores all discovered registered IIS sites.
	/// </summary>
	private Action<IEnumerable<IISScannerHandler.RegisteredSite>> OnAllSitesRequestCompleted =>
		sites => { AllSites = sites; };

	#endregion

	#region Methods: Private

	private void ExecuteMediatorRequest(Action<IEnumerable<IISScannerHandler.RegisteredSite>> callback){
		AllRegisteredSitesRequest request = new() {
			Callback = callback
		};

		Task.Run(async () => { await _mediator.Send(request); })
			.ConfigureAwait(false)
			.GetAwaiter()
			.GetResult();
	}

	private IEnumerable<string> GetEnvironmentPackagePathCandidates(EnvironmentSettings environment) {
		if (environment is null || string.IsNullOrWhiteSpace(environment.EnvironmentPath)) {
			return [];
		}

		string environmentPath = environment.EnvironmentPath;
		return environment.IsNetCore
			? [
				_fileSystem.Combine(environmentPath, "Terrasoft.Configuration", "Pkg"),
				_fileSystem.Combine(environmentPath, "Terrasoft.WebApp", "Terrasoft.Configuration", "Pkg")
			]
			: [
				_fileSystem.Combine(environmentPath, "Terrasoft.WebApp", "Terrasoft.Configuration", "Pkg"),
				_fileSystem.Combine(environmentPath, "Terrasoft.Configuration", "Pkg")
			];
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

	private int HandleLinkingByEnvNameOnWindows(string envName, string repoPath, string packages){
		ExecuteMediatorRequest(OnAllSitesRequestCompleted);
		EnvironmentSettings environment = _settingsRepository.GetEnvironment(envName);
		if (environment is null) {
			_logger.WriteError(
				$"Environment {envName} is not a registered environment. Please correct environment name an try again.");
			return 1;
		}

		bool isEnvUri = Uri.TryCreate(environment.Uri ?? string.Empty, UriKind.Absolute, out Uri envUri);
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
			string sitePath = site.siteType switch {
				IISScannerHandler.SiteType.NetFramework => _fileSystem.Combine(site.siteBinding.path,
					"Terrasoft.WebApp", "Terrasoft.Configuration", "Pkg"),
				var _ => _fileSystem.Combine(site.siteBinding.path, "Terrasoft.Configuration", "Pkg")
			};

			if (_fileSystem.ExistsDirectory(sitePath)) {
				return HandleLinkWithDirPath(sitePath, repoPath, packages);
			}
		}

		_logger.WriteError($"Environment {envName} not found. Please check the environment name and try again.");
		return 1;
	}

	/// <summary>
	/// Handles repository linking by registered environment name.
	/// </summary>
	/// <param name="envName">Registered environment name.</param>
	/// <param name="repoPath">Repository path.</param>
	/// <param name="packages">Package selector.</param>
	/// <returns><c>0</c> on success; otherwise <c>1</c>.</returns>
	private int HandleLinkingByEnvName(string envName, string repoPath, string packages){
		if (!_settingsRepository.IsEnvironmentExists(envName)) {
			_logger.WriteError(
				$"Environment {envName} is not a registered environment. Please correct environment name an try again.");
			return 1;
		}

		EnvironmentSettings environment = _settingsRepository.GetEnvironment(envName);
		string environmentPackagePath = ResolveEnvironmentPackagePath(environment);
		if (!string.IsNullOrWhiteSpace(environmentPackagePath)) {
			return HandleLinkWithDirPath(environmentPackagePath, repoPath, packages);
		}

		if (!Current.IsWindows) {
			_logger.WriteError(
				$"Environment {envName} does not have a valid local package path. Configure EnvironmentPath or use --envPkgPath with the direct package folder path.");
			return 1;
		}

		return HandleLinkingByEnvNameOnWindows(envName, repoPath, packages);
	}

	/// <summary>
	/// Handles repository linking by explicit environment package path.
	/// </summary>
	/// <param name="sitePath">Target package folder path.</param>
	/// <param name="repoPath">Repository path.</param>
	/// <param name="packages">Package selector.</param>
	/// <returns><c>0</c> on success; otherwise <c>1</c>.</returns>
	protected virtual int HandleLinkWithDirPath(string sitePath, string repoPath, string packages){
		_rfsEnvironment.Link4Repo(sitePath, repoPath, packages);
		_logger.WriteInfo($"Linking repository package(s) to environment {sitePath} from {repoPath}");
		return 0;
	}

	private int PrintErrorsAndExit(IEnumerable<ValidationFailure> errors){
		_logger.PrintValidationFailureErrors(errors);
		return 1;
	}

	private string ResolveEnvironmentPackagePath(EnvironmentSettings environment) {
		return GetEnvironmentPackagePathCandidates(environment)
			.FirstOrDefault(_fileSystem.ExistsDirectory);
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc />
	public override int Execute(Link4RepoOptions options){
		ValidationResult validationResult = _validator.Validate(options);
		if (!validationResult.IsValid) {
			return PrintErrorsAndExit(validationResult.Errors);
		}

		try {
			return options switch {
						not null when !string.IsNullOrWhiteSpace(options.Environment) => HandleEnvironmentOption(options),
						not null when !string.IsNullOrWhiteSpace(options.EnvPkgPath) => HandleEnvPkgPathOptions(options),
						var _ => 1
					};
		} catch (Exception ex) {
			_logger.WriteError($"Error during linking: {ex.Message}");
			return 1;
		}
	}

	#endregion

}
