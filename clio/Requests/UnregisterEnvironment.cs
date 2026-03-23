using System;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command;
using MediatR;

namespace Clio.Requests;

/// <summary>
/// Unregisters a clio environment from a deep link.
/// </summary>
public class UnregisterEnvironment : IExternalLink {
	/// <summary>
	/// Raw deep-link content.
	/// </summary>
	public string Content { get; set; }
}

/// <summary>
/// Handles deep links in the form of:
/// <code>clio://UnregisterEnvironment?name=studio-dev</code>
/// </summary>
internal class UnregisterEnvironmentHandler(UnregAppCommand unregAppCommand)
	: BaseExternalLinkHandler, IRequestHandler<UnregisterEnvironment> {
	private readonly UnregAppCommand _unregAppCommand = unregAppCommand;

	public Task Handle(UnregisterEnvironment request, CancellationToken cancellationToken) {
		Uri.TryCreate(request.Content, UriKind.Absolute, out _clioUri);

		string environmentName = ClioParams["name"]?.Trim();
		ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

		UnregAppOptions options = new() {
			Name = environmentName
		};

		_unregAppCommand.Execute(options);
		return Unit.Task;
	}
}
