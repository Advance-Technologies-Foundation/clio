using System;
using Clio.Common;
using Microsoft.Extensions.Logging;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Builds an <see cref="IPlatformVersionResolver"/> bound to a specific environment, so a
/// short-lived CLI invocation that takes <c>--environment &lt;name&gt;</c> can probe cliogate
/// on that environment without sharing state with the ambient singleton resolver used by
/// the long-lived MCP server.
/// </summary>
public interface IPlatformVersionResolverFactory {
	IPlatformVersionResolver Create(EnvironmentSettings settings);
}

/// <summary>
/// Default implementation: re-uses the same building blocks as the ambient singleton
/// (<see cref="IApplicationClientFactory.CreateEnvironmentClient"/>, <see cref="IServiceUrlBuilderFactory"/>,
/// <see cref="TimeProvider.System"/>) but constructs a fresh <see cref="PlatformVersionResolver"/>
/// per call so the result and its 5-min cache are scoped to that call's environment.
/// </summary>
public sealed class PlatformVersionResolverFactory : IPlatformVersionResolverFactory {
	private readonly IApplicationClientFactory _applicationClientFactory;
	private readonly IServiceUrlBuilderFactory _serviceUrlBuilderFactory;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<PlatformVersionResolver> _resolverLogger;

	public PlatformVersionResolverFactory(
		IApplicationClientFactory applicationClientFactory,
		IServiceUrlBuilderFactory serviceUrlBuilderFactory,
		TimeProvider timeProvider,
		ILogger<PlatformVersionResolver> resolverLogger) {
		_applicationClientFactory = applicationClientFactory ?? throw new ArgumentNullException(nameof(applicationClientFactory));
		_serviceUrlBuilderFactory = serviceUrlBuilderFactory ?? throw new ArgumentNullException(nameof(serviceUrlBuilderFactory));
		_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
		_resolverLogger = resolverLogger ?? throw new ArgumentNullException(nameof(resolverLogger));
	}

	public IPlatformVersionResolver Create(EnvironmentSettings settings) {
		if (settings is null) {
			throw new ArgumentNullException(nameof(settings));
		}
		IApplicationClient applicationClient = _applicationClientFactory.CreateEnvironmentClient(settings);
		return new PlatformVersionResolver(
			applicationClient,
			settings,
			_serviceUrlBuilderFactory,
			_timeProvider,
			_resolverLogger);
	}
}
