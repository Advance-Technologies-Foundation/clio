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

		#region Properties: Private

		private string RootPath => _environmentSettings.IsNetCore
			? _environmentSettings.Uri 
			: $@"{_environmentSettings.Uri}/0";

		#endregion
		
		#region Methods: Public

		public string Build(string serviceEndpoint) => $"{RootPath}{serviceEndpoint}";

		#endregion

	}

	#endregion

}