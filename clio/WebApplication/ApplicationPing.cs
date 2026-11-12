namespace Clio.WebApplication
{
	using System;
	using Clio.Common;

	#region Class: ApplicationPing

	public interface IApplicationPing
	{

		#region Methods: Public

		bool Ping();
		bool Ping(EnvironmentSettings environmentSettings);

		/// <summary>
		/// Pings the environment and, when it does not answer, says why.
		/// </summary>
		/// <param name="environmentSettings">The environment to ping.</param>
		/// <param name="failureReason">
		/// The message of the failure that stopped the ping - for a rejected login, for example,
		/// <c>Unauthorized &lt;user&gt; for &lt;url&gt;</c>; <see langword="null"/> when the environment answered.
		/// </param>
		/// <returns><see langword="true"/> when the environment answered.</returns>
		/// <remarks>
		/// <see cref="Ping(EnvironmentSettings)"/> answers only true or false, so a caller that stops on a false
		/// cannot tell a wrong password from a stopped site - and a caller that stops SILENTLY reports success for
		/// work it never did.
		/// </remarks>
		bool TryPing(EnvironmentSettings environmentSettings, out string failureReason);

		#endregion

	}

	public class ApplicationPing : IApplicationPing
	{

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IApplicationClientFactory _applicationClientFactory;
		private readonly IServiceUrlBuilder _serviceUrlBuilder;

		#endregion
		#region Constructors: Public

		public ApplicationPing(EnvironmentSettings environmentSettings,
			IApplicationClientFactory applicationClientFactory, IServiceUrlBuilder serviceUrlBuilder) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			applicationClientFactory.CheckArgumentNull(nameof(applicationClientFactory));
			serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
			_environmentSettings = environmentSettings;
			_applicationClientFactory = applicationClientFactory;
			_serviceUrlBuilder = serviceUrlBuilder;
		}

		#endregion

		#region Properties: Private

		// The ping URL comes from the environment being pinged, not from the one this instance was built for: the
		// public TryPing(environment, ...) contract allows pinging another environment, and a failed ping now
		// stops a restore instead of being ignored.
		private string PingUriFor(EnvironmentSettings environmentSettings) => environmentSettings.IsNetCore
			? environmentSettings.Uri
			: _serviceUrlBuilder.Build("ping", environmentSettings);

		#endregion

		#region Methods: Private

		private IOwnedApplicationClient CreateApplicationClient(EnvironmentSettings environmentSettings) =>
			_applicationClientFactory.CreateOwnedClient(environmentSettings);

		#endregion

		#region Methods: Public

		public bool Ping(EnvironmentSettings environmentSettings) => TryPing(environmentSettings, out _);

		public bool Ping() => Ping(_environmentSettings);

		public bool TryPing(EnvironmentSettings environmentSettings, out string failureReason) {
			try {
				using IOwnedApplicationClient client = CreateApplicationClient(environmentSettings);
				client.ExecuteGetRequest(PingUriFor(environmentSettings));
				failureReason = null;
				return true;
			} catch (Exception exception) {
				// The CLI's readable rendering: it scrubs credentials and words a refused connection as such.
				failureReason = exception.GetReadableMessageException();
				return false;
			}
		}

		#endregion

	}

	#endregion


	#region Class: ApplicationPingExtensions

	/// <summary>
	/// The one wording for "the environment must answer before this command writes anything", shared by
	/// restore-workspace (through <see cref="Clio.Package.IPackageDownloader.EnsureEnvironmentAnswers"/>) and
	/// create-workspace, so the two messages cannot drift apart.
	/// </summary>
	public static class ApplicationPingExtensions
	{

		/// <summary>Throws when the environment has no URL or does not answer the ping.</summary>
		/// <param name="applicationPing">The ping.</param>
		/// <param name="environmentSettings">The environment to check.</param>
		/// <param name="notDone">What the failure prevented, e.g. "Packages were not downloaded".</param>
		/// <param name="environmentName">The name the caller used, for an environment with no URL; optional.</param>
		/// <exception cref="InvalidOperationException">
		/// <c>"&lt;notDone&gt;: environment '&lt;name&gt;' is not registered. ..."</c> for an environment without a URL,
		/// or <c>"&lt;notDone&gt;: the environment did not answer (&lt;reason&gt;). Check it with ping-app ..."</c>.
		/// </exception>
		public static void EnsureAnswers(this IApplicationPing applicationPing, EnvironmentSettings environmentSettings,
			string notDone, string environmentName = null) {
			// An unregistered name resolves to settings without a URL; pinging them fails on client construction
			// with "Value cannot be null (uriString)", which reads like a stopped site.
			if (string.IsNullOrWhiteSpace(environmentSettings?.Uri)) {
				string what = string.IsNullOrWhiteSpace(environmentName)
					? "the environment has no URL"
					: $"environment '{environmentName}' is not registered";
				throw new InvalidOperationException(
					$"{notDone}: {what}. Register it with reg-web-app, or pass its URL with --uri.");
			}
			if (!applicationPing.TryPing(environmentSettings, out string failureReason)) {
				// The readable reason of a refused connection already ends with its own advice ("Make sure the
				// site is running and accessible."), so the tail adds only what it lacks: ping-app and credentials.
				string reason = (failureReason ?? "no reason given").TrimEnd().TrimEnd('.');
				throw new InvalidOperationException(
					$"{notDone}: the environment did not answer ({reason}). Check it with ping-app, and correct its "
					+ "registered credentials with reg-web-app if the login is rejected.");
			}
		}

	}

	#endregion

}
