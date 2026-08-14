using System;
using System.Net.Http;
using Clio.Common;

namespace Clio.Utilities;


/// <summary>
/// Opens URLs in the platform browser and checks whether a URL is reachable without following redirects.
/// </summary>
public interface IWebBrowser {
	/// <summary>Gets whether browser launching is supported on the current platform.</summary>
	bool Enabled { get; }

	/// <summary>Checks whether the URL returns a successful response without redirecting.</summary>
	/// <param name="url">URL to check.</param>
	/// <returns><see langword="true"/> when the URL responds successfully without redirecting.</returns>
	bool CheckUrl(string url);

	/// <summary>Opens the URL in the platform browser.</summary>
	/// <param name="url">URL to open.</param>
	void OpenUrl(string url);
}

internal class WebBrowser : IWebBrowser
{
	private static readonly HttpClient HttpClient = new(new HttpClientHandler {
		AllowAutoRedirect = false
	});

	private readonly IProcessExecutor _processExecutor;
	private readonly IOSPlatformChecker _platformChecker;
	private readonly ILogger _logger;
	private readonly HttpClient _httpClient;

	#region Constructors: Public

	public WebBrowser(IProcessExecutor processExecutor, IOSPlatformChecker platformChecker, ILogger logger)
		: this(processExecutor, platformChecker, logger, HttpClient) {
	}

	internal WebBrowser(IProcessExecutor processExecutor, IOSPlatformChecker platformChecker, ILogger logger,
		HttpClient httpClient) {
		_processExecutor = processExecutor;
		_platformChecker = platformChecker;
		_logger = logger;
		_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
	}

	#endregion

	#region Properties: Public

	public bool Enabled => _platformChecker.IsWindowsEnvironment;

	#endregion

	#region Methods: Public

	public bool CheckUrl(string url) {
		try {
			UriBuilder uriBuilder = new(url);
			using HttpResponseMessage response = _httpClient.GetAsync(uriBuilder.Uri).GetAwaiter().GetResult();
			return response.IsSuccessStatusCode && response.RequestMessage?.RequestUri == uriBuilder.Uri;
		}
		catch {
			return false;
		}
	}

	public void OpenUrl(string url) {
		if (_platformChecker.IsWindowsEnvironment) {
			_logger.WriteLine($"Open {url}...");
			_processExecutor.Execute("cmd", $"/c start {url}", waitForExit: false, workingDirectory: null, showOutput: false);
		}
		else {
			throw new NotFiniteNumberException("Command not supported for current platform...");
		}
	}

	#endregion
}
