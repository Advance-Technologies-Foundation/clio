using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using MediatR;

namespace Clio.Requests;

public class ValidationBehaviour<TRequest, TResponse> :
    IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{

    #region Fields: Private

    private readonly IValidator<TRequest> _validator;

    #endregion

    #region Constructors: Public

    public ValidationBehaviour(IValidator<TRequest> validator = null)
    {
        _validator = validator;
    }

    #endregion

    #region Methods: Public

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = _validator?.Validate(request);

        if (validationResult is object && !validationResult.IsValid)
        {
            throw new ValidationException(validationResult.Errors);
        }
        return next();
    }

    #endregion

}
