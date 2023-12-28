using Creatio.Client;

namespace Clio.Common
{
	public class ApplicationClientFactory : IApplicationClientFactory
	{
		public IApplicationClient CreateClient(EnvironmentSettings settings) {
			if (string.IsNullOrEmpty(settings.ClientId))
			{
				return new CreatioClientAdapter(settings.Uri, settings.Login, settings.Password,
					settings.IsNetCore);
			}
			else
			{
				return new CreatioClientAdapter(settings.Uri, settings.ClientId,
				settings.ClientSecret, settings.AuthAppUri, settings.IsNetCore);
			}
		}

		public IApplicationClient CreateEnvironmentClient(EnvironmentSettings settings) {
			var serviceUrlBuilder = new ServiceUrlBuilder(settings);
			if (string.IsNullOrEmpty(settings.ClientId)) {
				return new CreatioClientAdapter(settings.Uri, settings.Login, settings.Password,
					settings.IsNetCore, serviceUrlBuilder);
			} else {
				return new CreatioClientAdapter(settings.Uri, settings.ClientId,
				settings.ClientSecret, settings.AuthAppUri, settings.IsNetCore, serviceUrlBuilder);
			}
		}
	}
}
