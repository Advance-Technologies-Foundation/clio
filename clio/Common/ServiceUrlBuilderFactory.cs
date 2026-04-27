namespace Clio.Common;

public sealed class ServiceUrlBuilderFactory : IServiceUrlBuilderFactory
{
	public IServiceUrlBuilder Create(EnvironmentSettings environmentSettings) =>
		new ServiceUrlBuilder(environmentSettings);
}
