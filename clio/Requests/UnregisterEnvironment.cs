using System;
using System.Threading.Tasks;
using Clio.Command;

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
	: BaseExternalLinkHandler, IExternalLinkHandler {
	private readonly UnregAppCommand _unregAppCommand = unregAppCommand;

	public Type RequestType => typeof(UnregisterEnvironment);

	public Task Handle(IExternalLink request) {
		Uri.TryCreate(request.Content, UriKind.Absolute, out _clioUri);

		string environmentName = ClioParams["name"]?.Trim();
		ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

		UnregAppOptions options = new() {
			Name = environmentName
		};

		_unregAppCommand.Execute(options);
		return Task.CompletedTask;
	}
}
