namespace Clio.Common
{
	public interface IApplicationClientFactory
	{
		IApplicationClient CreateClient(EnvironmentSettings environment);

		IApplicationClient CreateEnvironmentClient(EnvironmentSettings environment);
	}
}
