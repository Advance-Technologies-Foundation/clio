using System.Collections.Generic;
using System.Linq;
using Clio.Requests;
using Clio.Utilities;

namespace Clio.Command;

public interface IIisEnvironmentDiscoveryService {
	IReadOnlyCollection<IisEnvironmentDescriptor> Discover(string? login, string? password, string? host);
}

internal sealed class IisEnvironmentDiscoveryService(IPowerShellFactory powerShellFactory)
	: IIisEnvironmentDiscoveryService {
	public IReadOnlyCollection<IisEnvironmentDescriptor> Discover(string? login, string? password, string? host) {
		powerShellFactory.Initialize(login, password, host);
		return IisScannerHandler.GetSites(powerShellFactory)
			.Select(site => new IisEnvironmentDescriptor(
				site.Key,
				site.Value.PhysicalPath,
				site.Value.Url.ToString().TrimEnd('/'),
				site.Value.SiteType == SiteType.Core))
			.ToArray();
	}
}

public sealed record IisEnvironmentDescriptor(
	string Name,
	string PhysicalPath,
	string Uri,
	bool IsNetCore);
