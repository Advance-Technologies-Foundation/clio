using MediatR;

namespace Clio.Requests
{
	internal interface IExternalLink : IRequest
	{
		public string Content
		{
			get; set;
		}
	}

}
