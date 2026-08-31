using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Creatio.Client;

namespace Clio.Common.BrowserSession;

/// <inheritdoc cref="ICreatioAuthClient" />
/// <remarks>This compatibility facade now delegates authenticated transport to CreatioClient.</remarks>
[Obsolete("Use IApplicationClientFactory.CreateFormsEnvironmentClient; retained for binary compatibility.")]
public sealed class CreatioAuthClient : ICreatioAuthClient {
	/// <summary>Retains the historical constructor signature for existing consumers.</summary>
	/// <param name="httpClientFactory">Legacy parameter; authenticated transport no longer uses it.</param>
	/// <param name="logger">Optional logger for cookie names.</param>
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Architecture", "CLIO001:Resolve behavior through DI",
		Justification = "The obsolete compatibility constructor cannot accept the modern factory without breaking its public signature.")]
	public CreatioAuthClient(IHttpClientFactory httpClientFactory, ILogger logger = null) {
		ArgumentNullException.ThrowIfNull(httpClientFactory);
		_logger = logger;
		_createClient = env => new CreatioClientAdapter(env.Uri, env.Login, env.Password,
			useUntrustedSsl: false, env.IsNetCore);
	}

	private readonly ILogger _logger;
	private readonly Func<EnvironmentSettings, IOwnedApplicationClient> _createClient;

	internal CreatioAuthClient(Func<EnvironmentSettings, IOwnedApplicationClient> createClient,
		ILogger logger = null) {
		_createClient = createClient ?? throw new ArgumentNullException(nameof(createClient));
		_logger = logger;
	}

	/// <inheritdoc />
	public async Task<StorageStateResult> LoginAsync(EnvironmentSettings env, CancellationToken ct = default) {
		ArgumentNullException.ThrowIfNull(env);
		if (string.IsNullOrEmpty(env.Login) || string.IsNullOrEmpty(env.Password)) {
			throw CreatioAuthenticationException.MissingFormsCredentials(env.Uri);
		}

		using IOwnedApplicationClient client = _createClient(env);
		try {
			using HttpResponseMessage response = await client.LoginAsync(30_000, ct).ConfigureAwait(false);
			if (!response.IsSuccessStatusCode) {
				throw CreatioAuthenticationException.InvalidCredentials(env.Uri);
			}
			IReadOnlyList<CreatioSessionCookie> cookies = client.ExportSessionCookies();
			if (cookies.Count == 0) {
				throw CreatioAuthenticationException.NoCookies(env.Uri);
			}
			_logger?.WriteDebug(
				$"Harvested {cookies.Count} Creatio session cookie(s): {string.Join(", ", cookies.Select(c => c.Name))}.");
			return new StorageStateResult(cookies.Select(ToBrowserCookie).ToList());
		} catch (UnauthorizedAccessException) {
			throw CreatioAuthenticationException.InvalidCredentials(env.Uri);
		} catch (HttpRequestException) {
			throw CreatioAuthenticationException.Connectivity(env.Uri);
		} catch (OperationCanceledException) {
			ct.ThrowIfCancellationRequested();
			throw CreatioAuthenticationException.Connectivity(env.Uri);
		}
	}

	private static BrowserCookie ToBrowserCookie(CreatioSessionCookie cookie) => new(
		cookie.Name,
		cookie.Value,
		cookie.Domain,
		string.IsNullOrEmpty(cookie.Path) ? "/" : cookie.Path,
		cookie.HttpOnly,
		cookie.Secure,
		cookie.SameSite,
		cookie.Expires == DateTime.MinValue
			? -1
			: new DateTimeOffset(cookie.Expires.ToUniversalTime()).ToUnixTimeSeconds());
}
