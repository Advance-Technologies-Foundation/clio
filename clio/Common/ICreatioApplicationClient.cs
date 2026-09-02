using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Creatio.Client;

namespace Clio.Common;

/// <summary>
/// Extends the stable application-client contract with CreatioClient transport capabilities.
/// </summary>
public interface ICreatioApplicationClient : IApplicationClient {
	/// <summary>Executes a cancellation-aware GET and transfers response ownership to the caller.</summary>
	Task<HttpResponseMessage> ExecuteGetRequestAsync(string url, int requestTimeout = 100_000,
		int maxAttempts = 1, int delaySec = 1, CancellationToken cancellationToken = default);

	/// <summary>Executes a cancellation-aware POST and transfers response ownership to the caller.</summary>
	Task<HttpResponseMessage> ExecutePostRequestAsync(string url, string requestData,
		int requestTimeout = 100_000, int maxAttempts = 1, int delaySec = 1,
		CancellationToken cancellationToken = default);

	/// <summary>Logs in with cancellation support and transfers response ownership to the caller.</summary>
	Task<HttpResponseMessage> LoginAsync(int requestTimeout = 100_000,
		CancellationToken cancellationToken = default);

	/// <summary>Returns detached copies of the current Creatio session cookies.</summary>
	IReadOnlyList<CreatioSessionCookie> ExportSessionCookies();

	/// <summary>Imports cookies for the configured Creatio application.</summary>
	void ImportSessionCookies(IEnumerable<CreatioSessionCookie> cookies);

	/// <summary>Uploads an image and transfers response ownership to the caller.</summary>
	Task<HttpResponseMessage> UploadImageAsync(string url, byte[] data, string fileName, string mimeType,
		int requestTimeout = 100_000, CancellationToken cancellationToken = default);
}

/// <summary>An application client whose creator transfers ownership to the caller.</summary>
/// <remarks>Dispose the client after the operation completes.</remarks>
public interface IOwnedApplicationClient : ICreatioApplicationClient, IDisposable { }
