using MediatR;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Requests
{
	public class OpenUrl : IExtenalLink
	{
		public string Content {
			get; set;
		}
	}

	/// <summary>
	/// Opens Url in default browser
	/// </summary>
	/// <remarks>
	/// Handles extenral link request
	/// <example><code>clio externalLink clio://OpenUrl/?url=https%3A%2F%2Fgoogle.ca</code></example>
	/// </remarks>
	internal class OpenUrlHandler : BaseExternalLinkHandler, IRequestHandler<OpenUrl>
	{
		public Task Handle(OpenUrl request, CancellationToken cancellationToken)
		{
			Uri.TryCreate(request.Content, UriKind.Absolute, out _clioUri);
			string requestedLink = ClioParams["url"];
			Process.Start(new ProcessStartInfo { FileName = requestedLink, UseShellExecute = true });
			return Unit.Task;
		}
	}
}
