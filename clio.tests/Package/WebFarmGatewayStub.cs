namespace Clio.Tests.Package;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// How the stub routes requests and whether it issues the <c>StickySession</c> cookie.
/// </summary>
internal enum WebFarmGatewayMode {

	/// <summary>
	/// A Control Plane web farm: a request carrying <c>StickySession</c> goes to the design pod, any other
	/// request is balanced round-robin across the common pods.
	/// </summary>
	WebFarm,

	/// <summary>
	/// A web farm whose gateway ignores <c>StickySession</c>, so even a sticky request lands on a common pod.
	/// </summary>
	WebFarmWithoutStickyRoute,

	/// <summary>
	/// A single node outside a web farm: one pod serves everything and <c>StickySession</c> is never issued.
	/// </summary>
	SingleNode

}

/// <summary>
/// In-process stand-in for a Creatio web farm behind the Control Plane runtime gateway.
/// </summary>
/// <remarks>
/// It reproduces only the platform and gateway rules the package installation depends on, each confirmed in
/// the platform sources: the login response does not carry <c>StickySession</c>; the cookie (empty value) is
/// appended to the response of the first authenticated request of a session; the gateway routes on the
/// cookie's presence alone; <c>PackageInstallation</c> temp files are per pod; <c>/api/Probes/DesignReady</c>
/// is anonymous and answers 200 only on the design node.
/// </remarks>
internal sealed class WebFarmGatewayStub : IDisposable {

	#region Constants: Public

	/// <summary>Name of the design pod in <see cref="Requests"/>.</summary>
	public const string DesignPod = "design";

	/// <summary>Name of the only pod in <see cref="WebFarmGatewayMode.SingleNode"/>.</summary>
	public const string SingleNodePod = "node";

	#endregion

	#region Constants: Private

	private const string StickySessionCookieName = "StickySession";
	private const string AuthCookieName = ".ASPXAUTH";
	private const string CsrfCookieName = "BPMCSRF";
	private const string CsrfToken = "stub-csrf-token";
	private const int WorkerCount = 2;

	#endregion

	#region Fields: Private

	private readonly WebFarmGatewayMode _mode;
	private readonly HttpListener _listener;
	private readonly CancellationTokenSource _stopping = new();
	private readonly object _sync = new();
	private readonly Dictionary<string, Dictionary<string, byte[]>> _uploads = new();
	private readonly Dictionary<string, StringBuilder> _logs = new();
	private readonly List<string> _requests = [];
	private int _roundRobin;
	private int _loginCount;

	#endregion

	#region Constructors: Public

	/// <summary>Starts the stub on a free loopback port.</summary>
	/// <param name="mode">Routing and cookie behaviour to emulate.</param>
	public WebFarmGatewayStub(WebFarmGatewayMode mode) {
		_mode = mode;
		int port = FreePort();
		Url = $"http://127.0.0.1:{port}";
		_listener = new HttpListener();
		_listener.Prefixes.Add(Url + "/");
		_listener.Start();
		_ = Task.Run(ServeAsync);
	}

	#endregion

	#region Properties: Public

	/// <summary>Application URL to register the clio environment on.</summary>
	public string Url { get; }

	/// <summary>Every handled request as <c>METHOD /path -&gt; pod</c>, in arrival order.</summary>
	public IReadOnlyList<string> Requests {
		get {
			lock (_sync) {
				return _requests.ToList();
			}
		}
	}

	/// <summary>Number of forms logins the stub answered.</summary>
	public int LoginCount {
		get {
			lock (_sync) {
				return _loginCount;
			}
		}
	}

	#endregion

	#region Methods: Private

	private static int FreePort() {
		System.Net.Sockets.TcpListener probe = new(IPAddress.Loopback, 0);
		probe.Start();
		int port = ((IPEndPoint)probe.LocalEndpoint).Port;
		probe.Stop();
		return port;
	}

	private static Dictionary<string, string> ParseCookies(string header) {
		Dictionary<string, string> cookies = new(StringComparer.Ordinal);
		if (string.IsNullOrEmpty(header)) {
			return cookies;
		}
		foreach (string part in header.Split(';')) {
			string[] pair = part.Split('=', 2);
			string name = pair[0].Trim();
			if (name.Length > 0) {
				cookies[name] = pair.Length > 1 ? pair[1].Trim() : string.Empty;
			}
		}
		return cookies;
	}

	private static bool IsCompleteGzip(byte[] data) {
		try {
			using MemoryStream source = new(data);
			using GZipStream gzip = new(source, CompressionMode.Decompress);
			gzip.CopyTo(Stream.Null);
			return true;
		}
		catch (InvalidDataException) {
			return false;
		}
		catch (EndOfStreamException) {
			return false;
		}
	}

	private async Task ServeAsync() {
		while (!_stopping.IsCancellationRequested) {
			HttpListenerContext context;
			try {
				context = await _listener.GetContextAsync().ConfigureAwait(false);
			}
			catch (HttpListenerException) {
				return;
			}
			catch (ObjectDisposedException) {
				return;
			}
			_ = Task.Run(() => Handle(context));
		}
	}

	private string RoutePod(IReadOnlyDictionary<string, string> cookies) {
		if (_mode == WebFarmGatewayMode.SingleNode) {
			return SingleNodePod;
		}
		if (_mode == WebFarmGatewayMode.WebFarm && cookies.ContainsKey(StickySessionCookieName)) {
			return DesignPod;
		}
		lock (_sync) {
			_roundRobin++;
			return $"worker-{(_roundRobin % WorkerCount) + 1}";
		}
	}

	private bool IsDesignNode(string pod) =>
		_mode != WebFarmGatewayMode.SingleNode && pod == DesignPod;

	private void Handle(HttpListenerContext context) {
		HttpListenerRequest request = context.Request;
		using HttpListenerResponse response = context.Response;
		Dictionary<string, string> cookies = ParseCookies(request.Headers["Cookie"]);
		string pod = RoutePod(cookies);
		string path = request.Url!.AbsolutePath;
		byte[] body = ReadBody(request);
		lock (_sync) {
			_requests.Add($"{request.HttpMethod} {path} -> {pod}");
		}
		if (path.EndsWith("/AuthService.svc/Login", StringComparison.OrdinalIgnoreCase)) {
			lock (_sync) {
				_loginCount++;
			}
			response.Headers.Add("Set-Cookie", $"{AuthCookieName}={Guid.NewGuid():N}; path=/; httponly");
			response.Headers.Add("Set-Cookie", $"{CsrfCookieName}={CsrfToken}; path=/");
			Write(response, 200, "{\"Code\":0,\"Message\":\"\"}");
			return;
		}
		if (path.EndsWith("/api/Probes/DesignReady", StringComparison.OrdinalIgnoreCase)) {
			bool isDesign = IsDesignNode(pod);
			Write(response, isDesign ? 200 : 503, $"{{\"isDesignNode\":{isDesign.ToString().ToLowerInvariant()}}}");
			return;
		}
		if (!cookies.ContainsKey(AuthCookieName)) {
			Write(response, 401, "unauthorized");
			return;
		}
		if (_mode != WebFarmGatewayMode.SingleNode && !cookies.ContainsKey(StickySessionCookieName)) {
			// Session start: Creatio appends the empty-valued cookie for a CanManageSolution user.
			response.Headers.Add("Set-Cookie", $"{StickySessionCookieName}=; path=/; samesite=none; httponly");
		}
		Dispatch(request, response, pod, path, body);
	}

	private void Dispatch(HttpListenerRequest request, HttpListenerResponse response, string pod, string path,
		byte[] body) {
		if (path.EndsWith("/UserInfoService.svc/GetCurrentUserInfo", StringComparison.OrdinalIgnoreCase)) {
			Write(response, 200, "{\"success\":true,\"userInfo\":{}}");
		} else if (path.EndsWith("/PackageInstallerService.svc/UploadPackage", StringComparison.OrdinalIgnoreCase)) {
			StoreChunk(request, pod, body);
			Write(response, 200, "{\"success\":true}");
		} else if (path.EndsWith("/PackageInstallerService.svc/CreateBackup", StringComparison.OrdinalIgnoreCase)) {
			Write(response, 200, "{\"success\":true}");
		} else if (path.EndsWith("/PackageInstallerService.svc/InstallPackage", StringComparison.OrdinalIgnoreCase)) {
			Write(response, 200, Install(pod, Encoding.UTF8.GetString(body).Trim('"')));
		} else if (path.EndsWith("/PackageInstallerService.svc/GetLogFile", StringComparison.OrdinalIgnoreCase)) {
			string log;
			lock (_sync) {
				log = _logs.TryGetValue(pod, out StringBuilder builder) ? builder.ToString() : string.Empty;
			}
			Write(response, 200, log, "text/plain");
		} else if (path.EndsWith("/AppInstallerService.svc/RestartApp", StringComparison.OrdinalIgnoreCase)) {
			Write(response, 200, "{\"success\":true}");
		} else {
			Write(response, 404, "not found");
		}
	}

	private static byte[] ReadBody(HttpListenerRequest request) {
		if (!request.HasEntityBody) {
			return [];
		}
		using MemoryStream buffer = new();
		request.InputStream.CopyTo(buffer);
		return buffer.ToArray();
	}

	private void StoreChunk(HttpListenerRequest request, string pod, byte[] chunk) {
		string fileName = request.QueryString["fileName"];
		int totalLength = int.Parse(request.QueryString["totalFileLength"]!);
		Match range = Regex.Match(request.Headers["Content-Range"] ?? string.Empty, @"bytes (\d+)-(\d+)/(\d+)");
		int start = int.Parse(range.Groups[1].Value);
		lock (_sync) {
			if (!_uploads.TryGetValue(pod, out Dictionary<string, byte[]> files)) {
				files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
				_uploads[pod] = files;
			}
			if (!files.TryGetValue(fileName!, out byte[] file)) {
				file = new byte[totalLength];
				files[fileName!] = file;
			}
			Buffer.BlockCopy(chunk, 0, file, start, chunk.Length);
		}
	}

	private string Install(string pod, string fileName) {
		byte[] data;
		lock (_sync) {
			data = _uploads.TryGetValue(pod, out Dictionary<string, byte[]> files)
				&& files.TryGetValue(fileName, out byte[] file)
					? file
					: null;
		}
		string logLine;
		string answer;
		if (data == null) {
			logLine = $"System.IO.DirectoryNotFoundException: {fileName} was not uploaded to {pod}";
			answer = "{\"success\":false,\"errorInfo\":{\"message\":\"DirectoryNotFoundException\"}}";
		} else if (!IsCompleteGzip(data)) {
			logLine = $"Terrasoft.Common.InvalidGZipArchiveException: Unable to open \"{fileName}\" on {pod}";
			answer = "{\"success\":false,\"errorInfo\":{\"message\":\"InvalidGZipArchiveException\"}}";
		} else {
			logLine = $"Packages installation completed: {fileName} on {pod}";
			answer = "{\"success\":true,\"errorInfo\":null}";
		}
		lock (_sync) {
			if (!_logs.TryGetValue(pod, out StringBuilder log)) {
				log = new StringBuilder();
				_logs[pod] = log;
			}
			log.AppendLine(logLine);
		}
		return answer;
	}

	private static void Write(HttpListenerResponse response, int status, string body,
		string contentType = "application/json") {
		byte[] bytes = Encoding.UTF8.GetBytes(body);
		response.StatusCode = status;
		response.ContentType = contentType;
		response.ContentLength64 = bytes.Length;
		response.OutputStream.Write(bytes, 0, bytes.Length);
	}

	#endregion

	#region Methods: Public

	/// <summary>Pods that served requests whose path ends with <paramref name="pathSuffix"/>.</summary>
	/// <param name="pathSuffix">Path suffix such as <c>/InstallPackage</c>.</param>
	/// <returns>Distinct pod names, in arrival order.</returns>
	public IReadOnlyList<string> PodsServing(string pathSuffix) =>
		Requests
			.Where(entry => entry.Split(" -> ")[0].EndsWith(pathSuffix, StringComparison.OrdinalIgnoreCase))
			.Select(entry => entry.Split(" -> ")[1])
			.Distinct()
			.ToList();

	/// <inheritdoc />
	public void Dispose() {
		_stopping.Cancel();
		try {
			_listener.Stop();
		}
		catch (ObjectDisposedException) {
			// Already torn down; a listener that is gone must not fail the test.
		}
		((IDisposable)_listener).Dispose();
		_stopping.Dispose();
	}

	#endregion

}
