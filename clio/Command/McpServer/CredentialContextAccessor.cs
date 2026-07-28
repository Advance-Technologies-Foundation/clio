using Microsoft.AspNetCore.Http;

namespace Clio.Command.McpServer;

/// <summary>
/// Provides per-request access to the current <see cref="CredentialContext"/>.
/// Backed by <see cref="IHttpContextAccessor"/> so each in-flight HTTP request
/// reads its own context on an independent async flow. Returns <see langword="null"/>
/// when there is no active HTTP request (stdio transport, or an HTTP request that
/// carried no credential header).
/// </summary>
public interface ICredentialContextAccessor
{
	/// <summary>
	/// Gets or sets the credential context for the current request. The getter
	/// returns <see langword="null"/> when there is no active <c>HttpContext</c>
	/// (stdio / no request); the setter is a no-op in that case.
	/// </summary>
	CredentialContext Current { get; set; }
}

/// <summary>
/// Default <see cref="ICredentialContextAccessor"/> that stores the context in
/// <see cref="HttpContext.Items"/> under a stable key.
/// </summary>
public sealed class CredentialContextAccessor : ICredentialContextAccessor
{
	internal const string ItemsKey = "clio.mcp.credential-context";

	private readonly IHttpContextAccessor _httpContextAccessor;

	/// <summary>
	/// Initializes a new instance of the <see cref="CredentialContextAccessor"/> class.
	/// </summary>
	/// <param name="httpContextAccessor">Accessor for the ambient <c>HttpContext</c>.</param>
	public CredentialContextAccessor(IHttpContextAccessor httpContextAccessor) {
		_httpContextAccessor = httpContextAccessor;
	}

	/// <inheritdoc />
	public CredentialContext Current {
		get {
			HttpContext httpContext = _httpContextAccessor.HttpContext;
			if (httpContext is null) {
				return null;
			}
			return httpContext.Items.TryGetValue(ItemsKey, out object value)
				? value as CredentialContext
				: null;
		}
		set {
			HttpContext httpContext = _httpContextAccessor.HttpContext;
			if (httpContext is null) {
				return;
			}
			httpContext.Items[ItemsKey] = value;
		}
	}
}
