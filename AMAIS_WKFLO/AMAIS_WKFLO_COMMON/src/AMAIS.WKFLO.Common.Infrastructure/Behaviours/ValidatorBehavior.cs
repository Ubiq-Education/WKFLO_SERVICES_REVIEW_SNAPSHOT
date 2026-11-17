using AMAIS.WKFLO.Common.Models.Attibutes;
using AMAIS.WKFLO.Common.Models.Results;
using FluentValidation;
using global::Mediator;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.Common.Infrastructure.Behaviours
{
    public class ValidatorBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
        where TMessage : notnull, IMessage
        where TResponse : Result
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IEnumerable<IValidator<TMessage>> _validators;

        public ValidatorBehavior(
            IServiceProvider serviceProvider,
            IEnumerable<IValidator<TMessage>> validators)
        {
            _serviceProvider = serviceProvider;
            _validators = validators;
        }

        public async ValueTask<TResponse> Handle(TMessage message, MessageHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken)
        {
            var response = new InvalidResult();

            //technical validation
            var context = new ValidationContext(message, _serviceProvider, null);
            var validationResults = new List<ValidationResult>();
            if (!Validator.TryValidateObject(message, context, validationResults, true))
            {
                CheckErrors(validationResults, response, string.Empty);
            }

            //business validation
            if (_validators.Any())
            {
                var businessValidationContext = new ValidationContext<TMessage>(message);
                var businessValidationResult = await Task.WhenAll(_validators.Select(x => x.ValidateAsync(businessValidationContext)));

                var businessErrors = businessValidationResult
                    .SelectMany(x => x.Errors)
                    .Where(x => x != null)
                    .ToList();

                businessErrors
                   .ForEach(x => response.AddError(x.PropertyName, x.ErrorMessage));
            }

            return response.Errors.Any() ?
                response as TResponse :
                await next(message, cancellationToken);
        }

        private void CheckErrors(IEnumerable<ValidationResult> validationResults, InvalidResult response, string prefix)
        {
            foreach (var validationResult in validationResults)
            {
                if (validationResult is CompositeValidationResult compositeValidationResult)
                {
                    foreach (var key in compositeValidationResult.MemberNames)
                    {
                        CheckErrors(compositeValidationResult.Results, response, string.Concat(prefix, key, "."));
                    }
                }
                else
                {
                    if (validationResult.MemberNames.Count() == 0)
                    {
                        response.AddError(String.Empty, validationResult.ErrorMessage);
                    }
                    else
                    {
                        foreach (var key in validationResult.MemberNames)
                        {
                            response.AddError(prefix + key, validationResult.ErrorMessage);
                        }
                    }
                }
            }
        }
    }
}