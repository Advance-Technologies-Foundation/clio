using System;
using Clio.Common;
using Microsoft.Extensions.Logging;

namespace Clio.Command.EntitySchemaDesigner;

/// <summary>
/// Builds an <see cref="ICurrentUserCultureResolver"/> bound to a specific environment, so a
/// short-lived CLI invocation that takes <c>--environment &lt;name&gt;</c> and a long-lived MCP
/// tool call can both resolve the profile culture for the environment carried by the current
/// request, without sharing the startup-time environment.
/// </summary>
public interface ICurrentUserCultureResolverFactory
{
	/// <summary>Creates a resolver bound to <paramref name="settings"/>.</summary>
	ICurrentUserCultureResolver Create(EnvironmentSettings settings);
}

/// <summary>
/// Default implementation. Mirrors <c>PlatformVersionResolverFactory</c>: it reuses the
/// per-environment <see cref="IApplicationClientFactory.CreateEnvironmentClient"/> and the shared
/// <see cref="IServiceUrlBuilderFactory"/>, but constructs a fresh resolver per call. The cache,
/// in contrast, is the singleton <see cref="ICurrentUserCultureCache"/> injected here, so resolved
/// cultures survive across calls and the <c>GetApplicationInfo</c> round-trip happens at most once
/// per environment per TTL window.
/// </summary>
public sealed class CurrentUserCultureResolverFactory : ICurrentUserCultureResolverFactory
{
	private readonly IApplicationClientFactory _applicationClientFactory;
	private readonly IServiceUrlBuilderFactory _serviceUrlBuilderFactory;
	private readonly ICurrentUserCultureCache _cache;
	private readonly ILoggerFactory _loggerFactory;

	/// <summary>Initializes the factory with the shared building blocks and the singleton cache.</summary>
	public CurrentUserCultureResolverFactory(
		IApplicationClientFactory applicationClientFactory,
		IServiceUrlBuilderFactory serviceUrlBuilderFactory,
		ICurrentUserCultureCache cache,
		ILoggerFactory loggerFactory)
	{
		_applicationClientFactory = applicationClientFactory ?? throw new ArgumentNullException(nameof(applicationClientFactory));
		_serviceUrlBuilderFactory = serviceUrlBuilderFactory ?? throw new ArgumentNullException(nameof(serviceUrlBuilderFactory));
		_cache = cache ?? throw new ArgumentNullException(nameof(cache));
		_loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
	}

	/// <inheritdoc />
	public ICurrentUserCultureResolver Create(EnvironmentSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		IApplicationClient applicationClient = _applicationClientFactory.CreateEnvironmentClient(settings);
		// The logger is created per call rather than stored as a typed instance field — that would
		// mismatch the factory's enclosing type (Sonar S6672), and the factory does no logging itself.
		return new CurrentUserCultureResolver(
			applicationClient,
			settings,
			_serviceUrlBuilderFactory,
			_cache,
			_loggerFactory.CreateLogger<CurrentUserCultureResolver>());
	}
}
