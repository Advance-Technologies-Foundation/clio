using Clio.Command;
using MediatR;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Requests;

/// <summary>
/// Registers a clio environment from a deep link.
/// </summary>
public class RegisterEnvironment : IExternalLink {
	/// <summary>
	/// Raw deep-link content.
	/// </summary>
	public string Content { get; set; }
}

/// <summary>
/// Handles deep links in the form of:
/// <code>clio://RegisterEnvironment?uri=http://localhost:5000&amp;login=Supervisor&amp;password=Supervisor&amp;isnetcore=true&amp;safe=false&amp;environmentpath=/app</code>
/// </summary>
internal class RegisterEnvironmentHandler(RegAppCommand regCommand)
	: BaseExternalLinkHandler, IRequestHandler<RegisterEnvironment> {
	private readonly RegAppCommand _regCommand = regCommand;

	public Task Handle(RegisterEnvironment request, CancellationToken cancellationToken) {
		Uri.TryCreate(request.Content, UriKind.Absolute, out _clioUri);

		string uri = ClioParams["uri"]?.Trim();
		ArgumentException.ThrowIfNullOrWhiteSpace(uri);
		Uri parsedUri = string.IsNullOrWhiteSpace(uri) ? null : new Uri(uri, UriKind.Absolute);
		string environmentName = ResolveEnvironmentName(parsedUri);
		bool? isNetCore = ParseBoolean("isnetcore");
		bool? safe = ParseBoolean("safe");

		RegAppOptions options = new() {
			EnvironmentName = environmentName,
			Uri = parsedUri?.ToString().TrimEnd('/'),
			Login = ClioParams["login"],
			Password = ClioParams["password"],
			IsNetCore = isNetCore,
			Safe = safe?.ToString(),
			EnvironmentPath = ClioParams["environmentpath"]
		};

		_regCommand.Execute(options);
		return Unit.Task;
	}

	private string ResolveEnvironmentName(Uri parsedUri) {
		string providedName = ClioParams["environmentname"] ?? ClioParams["name"];
		if (!string.IsNullOrWhiteSpace(providedName)) {
			return providedName.Trim().Replace(" ", "-", StringComparison.Ordinal);
		}

		string hostPart = parsedUri.Host.Replace(".", "-", StringComparison.Ordinal);
		bool isDefaultPort = parsedUri.IsDefaultPort
			|| parsedUri.Port == 80
			|| parsedUri.Port == 443;
		return isDefaultPort ? hostPart : $"{hostPart}-{parsedUri.Port}";
	}

	private bool? ParseBoolean(string key) {
		string value = ClioParams[key];
		return bool.TryParse(value, out bool parsedValue) ? parsedValue : null;
	}
}
