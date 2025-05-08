using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace Clio.Requests;

public class ValidationBehaviour<TRequest, TResponse>(IValidator<TRequest> validator = null) :
    IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IValidator<TRequest> _validator = validator;

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = _validator?.Validate(request);

        if (validationResult is not null && !validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }

        return next();
    }
}
