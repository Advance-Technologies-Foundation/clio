using FluentValidation;
using FluentValidation.Results;
using MediatR;
using System.Threading;
using System.Threading.Tasks;

namespace Clio.Requests
{
	public class ValidationBehaviour<TRequest, TResponse> :
		IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
	{
		private readonly IValidator<TRequest> _validator;

		public ValidationBehaviour(IValidator<TRequest> validator = null)
		{
			_validator = validator;
		}


		public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
		{
			ValidationResult validationResult = _validator?.Validate(request);

			if (validationResult is object && !validationResult.IsValid)
			{
				throw new ValidationException(validationResult.Errors);
			}
			return next();
		}
	}
}
