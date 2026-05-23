namespace Clio.Common;

public interface IServiceUrlBuilderFactory
{
	IServiceUrlBuilder Create(EnvironmentSettings environmentSettings);
}
