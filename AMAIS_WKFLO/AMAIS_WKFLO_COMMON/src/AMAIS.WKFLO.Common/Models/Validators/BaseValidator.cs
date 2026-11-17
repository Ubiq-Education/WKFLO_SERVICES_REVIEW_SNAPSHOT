using AMAIS.WKFLO.Common.Models.Results;
using FluentValidation;
using FluentValidation.Results;

namespace AMAIS.WKFLO.Common.Models.Validators
{
    public abstract class BaseValidator<T> : AbstractValidator<T>
    {
        public virtual InvalidResult ToInvalidResultResponse(ValidationResult validationResult)
        {
            var result = new InvalidResult();
            if (validationResult != null && validationResult.Errors != null)
            {
                foreach (var error in validationResult.Errors)
                {
                    result.AddError(error.PropertyName, error.ErrorMessage);
                }
            }
            return result;
        }
    }
}
