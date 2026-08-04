using System;
using System.Text.Json;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

#region Class: ReloadWorkplacesOptions

/// <summary>
///     Options for <see cref="ReloadWorkplacesCommand" />.
/// </summary>
[Verb("reload-workplaces", Aliases = ["reload-navigation", "rlwp"],
	HelpText = "Publish navigation changes to signed-in users without a re-login")]
[RequiresPackage("cliogate",
	Hint = "Run 'clio install-gate -e <environment>' (or call the install-gate MCP tool) to install/update cliogate.")]
public class ReloadWorkplacesOptions : EnvironmentOptions {

}

#endregion

#region Interface: IWorkplaceCacheReloader

/// <summary>
///     Publishes navigation changes to users who are already signed in.
/// </summary>
public interface IWorkplaceCacheReloader {

	#region Methods: Public

	/// <summary>
	///     Reloads the platform navigation caches on the target environment.
	/// </summary>
	/// <remarks>
	///     Workplace, section, and edit-page lists are cached per SESSION, so a browser refresh alone does not surface
	///     a workplace that was created, granted, or re-pointed after the user signed in. The platform invalidates
	///     those caches from an entity event listener on <c>SysUserInRole</c> / <c>SysAdminUnitInWorkplace</c> insert
	///     and delete only — nothing fires when just the workplace row, a section placement, or a home-page binding
	///     changed, nor when the rows were written straight through the database engine. This call reaches the same
	///     platform contract directly.
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	///     The environment answered with an empty body, a non-JSON body (typically an HTTP error page), or an explicit
	///     failure. The message states which, so the caller can fall back to telling users to log out and back in.
	/// </exception>
	void Reload();

	#endregion

}

#endregion

#region Class: WorkplaceCacheReloader

/// <inheritdoc cref="IWorkplaceCacheReloader" />
public class WorkplaceCacheReloader : IWorkplaceCacheReloader {

	#region Fields: Private

	private static readonly JsonSerializerOptions JsonOptions = new() {
		PropertyNameCaseInsensitive = true
	};

	private readonly IApplicationClientFactory _applicationClientFactory;
	private readonly EnvironmentSettings _environmentSettings;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;

	#endregion

	#region Constructors: Public

	public WorkplaceCacheReloader(EnvironmentSettings environmentSettings,
		IApplicationClientFactory applicationClientFactory, IServiceUrlBuilder serviceUrlBuilder){
		environmentSettings.CheckArgumentNull(nameof(environmentSettings));
		applicationClientFactory.CheckArgumentNull(nameof(applicationClientFactory));
		serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
		_environmentSettings = environmentSettings;
		_applicationClientFactory = applicationClientFactory;
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc />
	public void Reload(){
		string url = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ReloadWorkplaces);
		string response = _applicationClientFactory.CreateClient(_environmentSettings)
			.ExecutePostRequest(url, "{}");
		if (string.IsNullOrWhiteSpace(response)) {
			throw new InvalidOperationException(
				"ClioGate ReloadWorkplaces returned an empty response. "
				+ "Verify the installed cliogate version is current (clio install-gate) and check Error.log.");
		}
		BaseGateResponse parsed;
		try {
			parsed = JsonSerializer.Deserialize<BaseGateResponse>(response, JsonOptions);
		} catch (JsonException ex) {
			throw new InvalidOperationException(
				"ClioGate ReloadWorkplaces returned a non-JSON response (likely an HTTP error page). "
				+ "Verify the installed cliogate version is current (clio install-gate) and check Error.log.", ex);
		}
		if (parsed is not {Success: true}) {
			string detail = parsed?.ErrorInfo?.Message;
			throw new InvalidOperationException(
				"ClioGate ReloadWorkplaces did not reload the navigation caches"
				+ (string.IsNullOrWhiteSpace(detail) ? "." : $": {detail}")
				+ " Users must log out and back in to see the change.");
		}
	}

	#endregion

	#region Class: BaseGateResponse

	/// <summary>
	///     Wire shape of the cliogate <c>BaseResponse</c> envelope.
	/// </summary>
	private sealed record BaseGateResponse {

		public bool Success { get; init; }

		public GateErrorInfo ErrorInfo { get; init; }

	}

	/// <summary>
	///     Wire shape of the cliogate <c>ErrorInfo</c> block.
	/// </summary>
	private sealed record GateErrorInfo {

		public string Message { get; init; }

	}

	#endregion

}

#endregion

#region Class: ReloadWorkplacesCommand

/// <summary>
///     Publishes navigation changes to signed-in users so a new or changed workplace appears without a re-login.
/// </summary>
public class ReloadWorkplacesCommand : Command<ReloadWorkplacesOptions> {

	#region Fields: Private

	private readonly ILogger _logger;
	private readonly IWorkplaceCacheReloader _reloader;

	#endregion

	#region Constructors: Public

	public ReloadWorkplacesCommand(IWorkplaceCacheReloader reloader, ILogger logger){
		reloader.CheckArgumentNull(nameof(reloader));
		logger.CheckArgumentNull(nameof(logger));
		_reloader = reloader;
		_logger = logger;
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc />
	public override int Execute(ReloadWorkplacesOptions options){
		try {
			_reloader.Reload();
			_logger.WriteInfo("Navigation caches reloaded. Signed-in users see the change after a page refresh; "
				+ "no re-login is required.");
			return 0;
		} catch (Exception e) {
			_logger.WriteError(e.Message);
			return 1;
		}
	}

	#endregion

}

#endregion
