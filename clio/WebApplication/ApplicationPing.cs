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

		private IApplicationClient CreateApplicationClient(EnvironmentSettings environmentSettings) =>
			_applicationClientFactory.CreateClient(environmentSettings);

		#endregion

		#region Methods: Public

		public bool Ping(EnvironmentSettings environmentSettings) {
			try {
				IApplicationClient client = CreateApplicationClient(environmentSettings);
				client.ExecuteGetRequest(PingUri);
				return true;
			} catch (Exception) {
				return false;
			}
		}

		public bool Ping() => Ping(_environmentSettings);

		#endregion

	}

	#endregion

}