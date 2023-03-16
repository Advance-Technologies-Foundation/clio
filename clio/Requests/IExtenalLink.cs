using MediatR;

namespace Clio.Requests
{
	internal interface IExtenalLink : IRequest
	{
		public string Content
		{
			get; set;
		}
	}

}
