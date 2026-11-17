using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.Common.Models.Attibutes
{
    [AttributeUsage(AttributeTargets.Property)]
    public class RequiredIfAttribute : ValidationAttribute
    {
        protected readonly string _propertyName;
        protected readonly object[] _compare;

        public RequiredIfAttribute(string property, params object[] compare)
        {
            _propertyName = property;
            _compare = compare;

            ErrorMessage = "The field '{0}' is required";
        }

        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            var result = default(ValidationResult);

            var property = validationContext.ObjectType.GetProperty(_propertyName);

            if (property == null)
            {
                throw new MissingMemberException($"{nameof(RequiredIfAttribute)}: unknown property {property}");
            }
            else
            {
                var propertyValue = property.GetValue(validationContext.ObjectInstance, null);

                if (_compare.Any(x => Equals(propertyValue, x)))
                {
                    if (value == null || (value is string && string.IsNullOrWhiteSpace(value as string)))
                    {
                        result = new ValidationResult(ErrorMessage, new List<string>() { validationContext.DisplayName });
                    }
                }
            }

            return result;
        }
    }
}
