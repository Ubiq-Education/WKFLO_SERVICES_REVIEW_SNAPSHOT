using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.Common.Models.Attibutes
{
    public class DateInRangeAttribute : ValidationAttribute
    {
        protected readonly string _propertyName;
        protected readonly int _months;

        public DateInRangeAttribute(string property, int months)
        {
            _propertyName = property;
            _months = months;

            ErrorMessage = "The field '{0}' is not in range";
        }

        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            var result = default(ValidationResult);

            var property = validationContext.ObjectType.GetProperty(_propertyName);

            if (property == null)
            {
                throw new MissingMemberException($"{nameof(DateInRangeAttribute)}: unknown property {property}");
            }
            else
            {
                var propertyValue = property.GetValue(validationContext.ObjectInstance, null);

                if (value != null && propertyValue != null)
                {
                    if (value is DateTimeOffset && propertyValue is DateTimeOffset)
                    {
                        var dateFrom = (DateTimeOffset)value;
                        var dateTo = (DateTimeOffset)propertyValue;
                        if (dateFrom.AddMonths(_months) < dateTo)
                        {
                            result = new ValidationResult(ErrorMessage, new List<string>() { validationContext.DisplayName });
                        }
                    }
                    if (value is DateTime && propertyValue is DateTime)
                    {
                        var dateFrom = (DateTime)value;
                        var dateTo = (DateTime)propertyValue;
                        if (dateFrom.AddMonths(_months) < dateTo)
                        {
                            result = new ValidationResult(ErrorMessage, new List<string>() { validationContext.DisplayName });
                        }
                    }
                    else
                    {
                        result = new ValidationResult(ErrorMessage, new List<string>() { validationContext.DisplayName });
                    }
                }
            }

            return result;
        }
    }
}
