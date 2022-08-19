namespace Clio.Common
{

	#region Class: ServiceUrlBuilder

	public class ServiceUrlBuilder : IServiceUrlBuilder
	{

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;

		#endregion


		#region Constructors: Public

		public ServiceUrlBuilder(EnvironmentSettings environmentSettings) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			_environmentSettings = environmentSettings;
		}

		#endregion

		#region Methods: Private

		private string GetRootPath(EnvironmentSettings environmentSettings) => environmentSettings.IsNetCore
			? environmentSettings.Uri
			: $@"{environmentSettings.Uri}/0";

		#endregion

		#region Properties: Public

		public string RootPath => GetRootPath(_environmentSettings);

		#endregion

		#region Methods: Private

		private string Normalize(string serviceEndpoint) =>
			serviceEndpoint.StartsWith('/')
				? serviceEndpoint
				: $"/{serviceEndpoint}";

		#endregion

		#region Methods: Public

		public string Build(string serviceEndpoint) => $"{RootPath}{Normalize(serviceEndpoint)}";
		public string Build(string serviceEndpoint, EnvironmentSettings environmentSettings) =>
			$"{GetRootPath(environmentSettings)}{Normalize(serviceEndpoint)}";

		#endregion

	}

	#endregion

}