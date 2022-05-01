namespace Clio.Common
{
	public interface IServiceUrlBuilder
	{
		string Build(string serviceEndpoint);
		string Build(string serviceEndpoint, EnvironmentSettings environmentSettings);
	}
}