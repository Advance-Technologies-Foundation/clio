using Creatio.Client;

namespace Clio.Common
{
	public class ApplicationClientFactory : IApplicationClientFactory
	{
		public IApplicationClient CreateClient(EnvironmentSettings environment) {
			var creatioClient = new CreatioClient(environment.Uri, environment.Login, environment.Password,
				true, environment.IsNetCore);
			return new CreatioClientAdapter(creatioClient);
		}
	}
}
