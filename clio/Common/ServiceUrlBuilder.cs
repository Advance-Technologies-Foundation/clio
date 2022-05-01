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

		#region Methods: Public

		public string Build(string serviceEndpoint) => $"{GetRootPath(_environmentSettings)}{serviceEndpoint}";
		public string Build(string serviceEndpoint, EnvironmentSettings environmentSettings) => 
			$"{GetRootPath(environmentSettings)}{serviceEndpoint}";

		#endregion

	}

	#endregion

}