using Clio.Command;
using FluentValidation;
using System;
using System.Collections.Specialized;
using System.Linq;

namespace Clio.Requests.Validators
{
	internal class ExternalLinkOptionsValidator : AbstractValidator<ExternalLinkOptions>
	{
		public ExternalLinkOptionsValidator()
		{

			RuleFor(r => r.Content).Cascade(CascadeMode.Stop)
			.NotEmpty()
			.Custom((value, context) =>
			{
				if (!Uri.TryCreate(value, UriKind.Absolute, out Uri _uriFromString))
				{
					context.AddFailure(new FluentValidation.Results.ValidationFailure
					{
						ErrorCode = "10",
						ErrorMessage = "Value is not in the correct format",
						Severity = Severity.Error,
						AttemptedValue = value
					});
				}
			})
			.Custom((value, context) =>
			{
				if (!Uri.TryCreate(value, UriKind.Absolute, out Uri _uriFromString) || _uriFromString?.Scheme != "clio")
				{
					context.AddFailure(new FluentValidation.Results.ValidationFailure
					{
						ErrorCode = "20",
						ErrorMessage = "Value has to start with clio://",
						Severity = Severity.Error,
						AttemptedValue = value
					});
				}
			})
			.Custom((value, context) =>
			{
				Uri.TryCreate(value, UriKind.Absolute, out Uri _uriFromString);
				string commandName = _uriFromString?.Host;
				Type runtimeType = GetType().Assembly.GetTypes()
					.Where(t => t.FullName.ToLower() == $"clio.requests.{commandName}")
					.FirstOrDefault();
				if (runtimeType is null)
				{
					context.AddFailure(new FluentValidation.Results.ValidationFailure()
					{
						ErrorCode = "50",
						ErrorMessage = $"Command <{commandName}> not found",
						Severity = Severity.Error,
						AttemptedValue = commandName
					});
				}
			})
			.Custom((value, context) =>
			{
				if (!Uri.TryCreate(value, UriKind.Absolute, out Uri _uriFromString) || _uriFromString.Query is not null)
				{
					NameValueCollection nvc = System.Web.HttpUtility.ParseQueryString(_uriFromString.Query);

					for (int i = 0; i < nvc.Count; i++)
					{
						var key = nvc.Keys[i];
						var val = nvc[i];
						if (string.IsNullOrEmpty(val) || string.IsNullOrEmpty(key))
						{

							key = (string.IsNullOrEmpty(key)) ? "missing" : key;
							val = (string.IsNullOrEmpty(val)) ? "missing" : key;

							context.AddFailure(new FluentValidation.Results.ValidationFailure()
							{
								ErrorCode = "50",
								ErrorMessage = $"Query not in correct format key is '{key}' when value '{val}'",
								Severity = Severity.Error,
								AttemptedValue = _uriFromString
							});
						}
					}

				}
			});
		}
	}
}
