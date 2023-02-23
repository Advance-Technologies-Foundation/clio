using MediatR;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Requests
{
	public class OpenUrl : IExtenalLink
	{
		public string Content
		{
			get; set;
		}
	}

	/// <summary>
	/// Opens Url in default browser
	/// </summary>
	/// <remarks>
	/// Handles extenral link request
	/// <example><code>clio --externalLink clio://OpenUrl/?url=https%3A%2F%2Fgoogle.ca</code></example>
	/// </remarks>
	public class OpenUrlHandler : BaseExternalLinkHandler, IRequestHandler<OpenUrl>
	{

		public OpenUrlHandler()
		{
		}


		public Task<Unit> Handle(OpenUrl request, CancellationToken cancellationToken)
		{

			if (!IsLinkValid(request.Content))
				return Unit.Task;

#if (DEBUG)
			PrintArguments();
#endif

			var requestedLink = clioParams["url"];
			Process.Start(new ProcessStartInfo { FileName = requestedLink, UseShellExecute = true });
			return Unit.Task;
		}
	}
}
