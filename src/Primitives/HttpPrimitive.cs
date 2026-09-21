using Clio10.PrimitiveContracts;
using Clio10.Contracts;
using Creatio.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Clio10.Primitives;

/// <summary>Async SDK adapter bound to one identity and application.</summary>
public sealed class HttpPrimitive(Func<IAsyncCreatioClient> client, PrimitiveSessionOptions options) : IClioPrimitive {
    private PrimitiveResponse? _authenticated;
    /// <inheritdoc />
    public async Task<PrimitiveResponse> LoginAsync(CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (_authenticated is not null) return _authenticated;
        var response = await ReceiveAsync(() => client().LoginAsync(cancellationToken: cancellationToken), cancellationToken);
        if (response.StatusCode is >= 200 and < 300) _authenticated = response;
        return response;
    }

    /// <inheritdoc />
    public Task<PrimitiveResponse> ExecuteAsync(PrimitiveRequest request, CancellationToken cancellationToken) {
        if (request.TimeoutMilliseconds <= 0) throw new ArgumentException("A positive request timeout is required.", nameof(request));
        if (options.BaseUri is null) throw new CapabilityUnavailableException("http-target");
        string relative = Uri.UnescapeDataString(request.RelativePath);
        if (relative.StartsWith('/') || relative.Contains('\\') || relative.Split('/').Contains("..") ||
            Uri.TryCreate(relative, UriKind.Absolute, out _))
            throw new ArgumentException("Only application-relative paths are supported.", nameof(request));
        var target = new Uri(options.BaseUri, request.RelativePath);
        if (!options.BaseUri.IsBaseOf(target))
            throw new ArgumentException("The operation must stay within the configured application.", nameof(request));
        string url = target.AbsoluteUri;
        return ReceiveAsync(() => request.Method.ToUpperInvariant() switch {
            "GET" => client().ExecuteGetRequestAsync(url, requestTimeout: request.TimeoutMilliseconds, maxAttempts: 1, cancellationToken: cancellationToken),
            "POST" => client().ExecutePostRequestAsync(url, request.Body ?? "", requestTimeout: request.TimeoutMilliseconds, maxAttempts: 1, cancellationToken: cancellationToken),
            "PUT" => client().ExecutePutRequestAsync(url, request.Body ?? "", requestTimeout: request.TimeoutMilliseconds, maxAttempts: 1, cancellationToken: cancellationToken),
            "PATCH" => client().ExecutePatchRequestAsync(url, request.Body ?? "", requestTimeout: request.TimeoutMilliseconds, maxAttempts: 1, cancellationToken: cancellationToken),
            "DELETE" => client().ExecuteDeleteRequestAsync(url, request.Body ?? "", requestTimeout: request.TimeoutMilliseconds, maxAttempts: 1, cancellationToken: cancellationToken),
            _ => throw new ArgumentException("Unsupported HTTP method.", nameof(request))
        }, cancellationToken);
    }

    private static async Task<PrimitiveResponse> ReceiveAsync(Func<Task<HttpResponseMessage>> send, CancellationToken cancellationToken) {
        try {
            using var response = await send();
            return new((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken),
                ProviderVersion: typeof(HttpPrimitive).Assembly.GetName().Version!.ToString());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(null, "", "timeout"); }
        catch (UnauthorizedAccessException) { return new(null, "", "authentication-rejected"); }
        catch (HttpRequestException error) when (error.StatusCode is not null) { return new((int)error.StatusCode, ""); }
        catch (HttpRequestException) { return new(null, "", "transport-failure"); }
    }
}

/// <summary>Default SDK wiring; overrides remain possible without exposing SDK types in contracts.</summary>
public static class PrimitiveDefaults {
    /// <summary>Registers isolated authentication state for a primitive session.</summary>
    public static IServiceCollection AddClioPrimitives(this IServiceCollection services, PrimitiveSessionOptions options) {
        if (options.BaseUri is not null && (!options.BaseUri.IsAbsoluteUri || options.BaseUri.Scheme is not ("http" or "https") ||
            !options.BaseUri.AbsolutePath.EndsWith('/') || options.BaseUri.Query.Length != 0 || options.BaseUri.Fragment.Length != 0 || options.BaseUri.UserInfo.Length != 0))
            throw new ArgumentException("Supply an HTTP(S) application URL ending in '/'.", nameof(options));
        services.AddSingleton(options);
        services.TryAddScoped<IAsyncCreatioClient>(_ => new CreatioClient((options.BaseUri ?? throw new CapabilityUnavailableException("http-target")).AbsoluteUri,
            options.UserName, options.Password, options.AllowUntrustedCertificate, options.IsNetCore) { SkipPing = true });
        services.TryAddScoped<Func<IAsyncCreatioClient>>(provider => () => provider.GetRequiredService<IAsyncCreatioClient>());
        services.TryAddScoped<IClioPrimitive, HttpPrimitive>();
        return services;
    }
}
