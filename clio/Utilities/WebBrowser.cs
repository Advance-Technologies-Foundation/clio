using System;
using System.Net.Http;
using Clio.Common;

namespace Clio.Utilities;

public interface IWebBrowser
{
	bool Enabled { get; }
	bool CheckUrl(string url);
	void OpenUrl(string url);
}

internal class WebBrowser : IWebBrowser
{
	private static readonly HttpClient HttpClient = new(new HttpClientHandler {
		AllowAutoRedirect = false
	});

	private readonly IProcessExecutor _processExecutor;
	private readonly IOSPlatformChecker _platformChecker;

	#region Constructors: Public

	public WebBrowser(IProcessExecutor processExecutor, IOSPlatformChecker platformChecker) {
		_processExecutor = processExecutor;
		_platformChecker = platformChecker;
	}

	#endregion

	#region Properties: Public

	public bool Enabled => _platformChecker.IsWindowsEnvironment;

	#endregion

	#region Methods: Public

	public bool CheckUrl(string url) {
		try {
			UriBuilder uriBuilder = new(url);
			HttpResponseMessage response = HttpClient.GetAsync(uriBuilder.Uri).GetAwaiter().GetResult();
			return response.IsSuccessStatusCode && response.RequestMessage?.RequestUri == uriBuilder.Uri;
		}
		catch {
			return false;
		}
	}

	public void OpenUrl(string url) {
		if (_platformChecker.IsWindowsEnvironment) {
			Console.WriteLine($"Open {url}...");
			_processExecutor.Execute("cmd", $"/c start {url}", waitForExit: false, workingDirectory: null, showOutput: false);
		}
		else {
			throw new NotFiniteNumberException("Command not supported for current platform...");
		}
	}

	#endregion
}
