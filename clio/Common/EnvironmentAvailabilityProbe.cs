using System;
using System.Net.Http;
using System.Threading;

namespace Clio.Common;

/// <summary>
/// Answers one question about an environment: is its web application responding right now.
/// </summary>
/// <remarks>
/// <para>
/// <b>It deliberately does NOT go through <see cref="IApplicationClient"/>.</b> That path does not enforce
/// the request timeout it is given — measured on this repository's own client: a read asked to bound
/// itself at 60 seconds took 100 seconds against a server that accepts the connection and never answers,
/// matching the ~104 s measured under ENG-94417 for <c>healthcheck --timeout 3000</c>. A caller that
/// needs to SAMPLE availability every few seconds cannot use a probe whose real granularity is a minute
/// and a half, and a caller that needs to stop promptly cannot use one that is not cancellable.
/// </para>
/// <para>
/// <b>Any HTTP response counts as reachable, including 401 and a login redirect.</b> The question is
/// whether the application is serving requests, not whether this caller is authenticated — so the probe
/// needs no credentials, no session, and no parsing, which is also what makes it cheap enough to repeat.
/// </para>
/// </remarks>
public interface IEnvironmentAvailabilityProbe {

	/// <summary>
	/// Probes the environment once.
	/// </summary>
	/// <param name="timeout">How long to wait before calling the environment unreachable.</param>
	/// <param name="cancellationToken">Abandons the probe.</param>
	/// <returns><see langword="true"/> when the application answered with any HTTP status.</returns>
	bool IsReachable(TimeSpan timeout, CancellationToken cancellationToken);

}

/// <inheritdoc cref="IEnvironmentAvailabilityProbe"/>
public class EnvironmentAvailabilityProbe : IEnvironmentAvailabilityProbe {

	#region Fields: Private

	private readonly IHttpClientFactory _httpClientFactory;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="EnvironmentAvailabilityProbe"/> class.
	/// </summary>
	/// <param name="httpClientFactory">Supplies a client whose timeout is actually honoured.</param>
	/// <param name="serviceUrlBuilder">Builds the probed URL for the target environment.</param>
	public EnvironmentAvailabilityProbe(IHttpClientFactory httpClientFactory,
		IServiceUrlBuilder serviceUrlBuilder) {
		httpClientFactory.CheckArgumentNull(nameof(httpClientFactory));
		serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
		_httpClientFactory = httpClientFactory;
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc/>
	public bool IsReachable(TimeSpan timeout, CancellationToken cancellationToken) {
		try {
			using HttpClient client = _httpClientFactory.CreateClient();
			client.Timeout = timeout;
			using HttpRequestMessage request = new(HttpMethod.Get,
				_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.LastCompilationResult));
			// HttpCompletionOption.ResponseHeadersRead: the status line is the whole answer, and not reading
			// the body keeps a probe cheap enough to repeat every few seconds.
			using HttpResponseMessage response = client
				.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
				.GetAwaiter().GetResult();
			// No status check on purpose: 200, 401 and a 302 to the login page all mean the application is
			// up, which is the only thing being asked.
			return true;
		} catch (Exception) {
			// Timeout, connection refused, DNS failure, cancellation - all mean "not answering right now",
			// which is what the caller acts on. Distinguishing them would not change any caller's behaviour.
			return false;
		}
	}

	#endregion

}
