using System;
using System.Threading.Tasks;

namespace Clio.Requests;

/// <summary>
/// Handles a single <see cref="IExternalLink"/> deep-link request type
/// in the externalLink command path.
/// </summary>
/// <remarks>
/// Implementations declare which concrete <see cref="IExternalLink"/> request they serve via
/// <see cref="RequestType"/>; the dispatcher (<see cref="Clio.Command.ExternalLinkCommand"/>)
/// selects the matching handler by comparing the reflected request type against
/// <see cref="RequestType"/>.
/// </remarks>
internal interface IExternalLinkHandler {

	/// <summary>
	/// Gets the concrete <see cref="IExternalLink"/>-derived request type this handler serves.
	/// </summary>
	Type RequestType { get; }

	/// <summary>
	/// Handles the supplied deep-link request.
	/// </summary>
	/// <param name="request">The deep-link request to handle.</param>
	/// <returns>A task that completes when the request has been handled.</returns>
	Task Handle(IExternalLink request);
}
