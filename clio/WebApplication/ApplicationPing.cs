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

		private string PingUri => _environmentSettings.IsNetCore
			? _environmentSettings.Uri
			: _serviceUrlBuilder.Build("ping");

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
				client.ExecuteGetRequest(PingUri);
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

}
