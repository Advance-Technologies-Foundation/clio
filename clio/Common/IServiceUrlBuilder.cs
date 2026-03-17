namespace Clio.Common;

public interface IServiceUrlBuilder
{

	#region Methods: Public

	string Build(string serviceEndpoint);

	string Build(ServiceUrlBuilder.KnownRoute knownRoute);

	string Build(string serviceEndpoint, EnvironmentSettings environmentSettings);

	string Build(ServiceUrlBuilder.KnownRoute knownRoute, EnvironmentSettings environmentSettings);

	#endregion

}