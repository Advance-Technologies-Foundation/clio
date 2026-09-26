namespace Clio.Command.Localization;

using System;
using Clio.Common;

/// <summary>
/// Builds an <see cref="ICreatioCultureCatalog"/> bound to one environment's client. A service that already
/// holds a client for the environment it writes to uses this instead of injecting the catalog directly: the
/// injected catalog is bound to the process-active environment, which is absent in a process started without
/// one and is not the tenant an MCP call targets.
/// </summary>
public interface ICreatioCultureCatalogFactory {

	/// <summary>
	/// Creates a catalog that reads <c>SysCulture</c> through <paramref name="client"/>.
	/// </summary>
	/// <param name="client">Authenticated client of the environment the caller writes to. The caller keeps
	/// ownership of it; the catalog does not dispose it.</param>
	/// <param name="settings">The same environment's settings, used to build the DataService route.</param>
	/// <returns>A catalog bound to that environment.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="client"/> or <paramref name="settings"/> is
	/// <see langword="null"/>.</exception>
	ICreatioCultureCatalog Create(IApplicationClient client, EnvironmentSettings settings);
}

/// <inheritdoc />
public sealed class CreatioCultureCatalogFactory : ICreatioCultureCatalogFactory {

	private readonly IServiceUrlBuilderFactory _serviceUrlBuilderFactory;

	/// <summary>
	/// Initializes a new instance of the <see cref="CreatioCultureCatalogFactory"/> class.
	/// </summary>
	/// <param name="serviceUrlBuilderFactory">Builds the per-environment URL builder the catalog needs.</param>
	public CreatioCultureCatalogFactory(IServiceUrlBuilderFactory serviceUrlBuilderFactory) {
		_serviceUrlBuilderFactory = serviceUrlBuilderFactory
			?? throw new ArgumentNullException(nameof(serviceUrlBuilderFactory));
	}

	/// <inheritdoc />
	public ICreatioCultureCatalog Create(IApplicationClient client, EnvironmentSettings settings) {
		ArgumentNullException.ThrowIfNull(client);
		ArgumentNullException.ThrowIfNull(settings);
		return new CreatioCultureCatalog(client, _serviceUrlBuilderFactory.Create(settings));
	}
}
