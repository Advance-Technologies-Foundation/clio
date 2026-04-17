using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Common.DataForge;

/// <summary>
/// Temporarily suppresses poisoned proxy environment variables for Data Forge HTTP calls so that
/// stale <c>HTTP_PROXY</c> / <c>HTTPS_PROXY</c> / <c>ALL_PROXY</c> values (e.g.
/// <c>http://127.0.0.1:9</c>) do not redirect traffic to a non-existent local proxy, while
/// preserving valid corporate proxy settings.
/// <para>
/// The suppression is scoped to the duration of a single <see cref="ExecuteAsync{T}"/> call
/// and serialised via a semaphore to prevent races when multiple Data Forge tools execute in parallel.
/// </para>
/// </summary>
public interface IDataForgeProxySafeExecutor {
	/// <summary>
	/// Runs <paramref name="action"/> with poisoned proxy environment variables temporarily suppressed
	/// for the <paramref name="targetHost"/> (added to <c>NO_PROXY</c>).
	/// </summary>
	Task<T> ExecuteAsync<T>(Func<Task<T>> action, string? targetHost = null);

	/// <summary>
	/// Synchronous overload for non-async Data Forge operations.
	/// </summary>
	T Execute<T>(Func<T> action, string? targetHost = null);
}

public sealed class DataForgeProxySafeExecutor : IDataForgeProxySafeExecutor {
	private static readonly string[] ProxyVarNames =
		OperatingSystem.IsWindows()
			? ["HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY"]
			: ["HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy"];
	private const string NoProxyKey = "NO_PROXY";
	private static readonly string? NoProxyKeyLower = OperatingSystem.IsWindows() ? null : "no_proxy";

	private static readonly SemaphoreSlim Semaphore = new(1, 1);

	public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, string? targetHost = null) {
		await Semaphore.WaitAsync().ConfigureAwait(false);
		string?[] savedProxy = Array.Empty<string?>();
		string? savedNoProxy = null;
		string? savedNoProxyLower = null;
		try {
			(savedProxy, savedNoProxy, savedNoProxyLower) = SuppressProxy(targetHost);
			return await action().ConfigureAwait(false);
		} finally {
			RestoreProxy(savedProxy, savedNoProxy, savedNoProxyLower);
			Semaphore.Release();
		}
	}

	public T Execute<T>(Func<T> action, string? targetHost = null) {
		Semaphore.Wait();
		string?[] savedProxy = Array.Empty<string?>();
		string? savedNoProxy = null;
		string? savedNoProxyLower = null;
		try {
			(savedProxy, savedNoProxy, savedNoProxyLower) = SuppressProxy(targetHost);
			return action();
		} finally {
			RestoreProxy(savedProxy, savedNoProxy, savedNoProxyLower);
			Semaphore.Release();
		}
	}

	private static (string?[] proxyValues, string? noProxy, string? noProxyLower) SuppressProxy(string? targetHost) {
		string?[] savedValues = new string?[ProxyVarNames.Length];
		for (int i = 0; i < ProxyVarNames.Length; i++) {
			savedValues[i] = Environment.GetEnvironmentVariable(ProxyVarNames[i]);
			if (ShouldSuppressProxy(savedValues[i])) {
				Environment.SetEnvironmentVariable(ProxyVarNames[i], null);
			}
		}

		string? savedNoProxy = Environment.GetEnvironmentVariable(NoProxyKey);
		string? savedNoProxyLower = NoProxyKeyLower is not null
			? Environment.GetEnvironmentVariable(NoProxyKeyLower) : null;

		if (!string.IsNullOrWhiteSpace(targetHost)) {
			AppendToNoProxy(NoProxyKey, savedNoProxy, targetHost);
			if (NoProxyKeyLower is not null) {
				AppendToNoProxy(NoProxyKeyLower, savedNoProxyLower, targetHost);
			}
		}

		return (savedValues, savedNoProxy, savedNoProxyLower);
	}

	private static void RestoreProxy(string?[] proxyValues, string? noProxy, string? noProxyLower) {
		for (int i = 0; i < proxyValues.Length; i++) {
			Environment.SetEnvironmentVariable(ProxyVarNames[i], proxyValues[i]);
		}
		Environment.SetEnvironmentVariable(NoProxyKey, noProxy);
		if (NoProxyKeyLower is not null) {
			Environment.SetEnvironmentVariable(NoProxyKeyLower, noProxyLower);
		}
	}

	private static void AppendToNoProxy(string envKey, string? currentValue, string host) {
		if (string.IsNullOrWhiteSpace(currentValue)) {
			Environment.SetEnvironmentVariable(envKey, host);
		} else if (!currentValue.Contains(host, StringComparison.OrdinalIgnoreCase)) {
			Environment.SetEnvironmentVariable(envKey, $"{currentValue},{host}");
		}
	}

	private static bool ShouldSuppressProxy(string? proxyValue) {
		if (string.IsNullOrWhiteSpace(proxyValue)
			|| !Uri.TryCreate(proxyValue, UriKind.Absolute, out Uri? proxyUri)) {
			return false;
		}

		return proxyUri.Port == 9 && IsLoopbackHost(proxyUri.Host);
	}

	private static bool IsLoopbackHost(string host) {
		if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(host, "0.0.0.0", StringComparison.OrdinalIgnoreCase)) {
			return true;
		}

		return IPAddress.TryParse(host, out IPAddress? ipAddress) && IPAddress.IsLoopback(ipAddress);
	}
}
