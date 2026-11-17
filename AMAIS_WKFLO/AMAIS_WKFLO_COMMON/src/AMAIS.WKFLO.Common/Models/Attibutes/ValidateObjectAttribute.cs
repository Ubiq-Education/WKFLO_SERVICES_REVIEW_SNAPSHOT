using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.Common.Models.Attibutes
{
    public class ValidateObjectAttribute : ValidationAttribute
    {
        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            var results = new List<ValidationResult>();

            if (value != null)
            {
                if (value is IEnumerable<object> list)
                {
                    foreach (var item in list)
                    {
                        var context = new ValidationContext(item, null, null);
                        Validator.TryValidateObject(item, context, results, true);
                        if (results.Count != 0)
                        {
                            var compositeResults = new CompositeValidationResult(String.Format("Validation for {0} failed!", validationContext.DisplayName), new List<string> { validationContext.MemberName });
                            results.ForEach(compositeResults.AddResult);
                            return compositeResults;
                        }
                    }
                }
                else
                {
                    var context = new ValidationContext(value, null, null);
                    Validator.TryValidateObject(value, context, results, true);
                    if (results.Count != 0)
                    {
                        var compositeResults = new CompositeValidationResult(String.Format("Validation for {0} failed!", validationContext.DisplayName), new List<string> { validationContext.MemberName });
                        results.ForEach(compositeResults.AddResult);
                        return compositeResults;
                    }
                }
            }

            return ValidationResult.Success;
        }
    }

    public class CompositeValidationResult : ValidationResult
    {
        private readonly List<ValidationResult> _results = new();

        public IEnumerable<ValidationResult> Results
        {
            get
            {
                return _results;
            }
        }

        public CompositeValidationResult(string errorMessage) : base(errorMessage) { }
        public CompositeValidationResult(string errorMessage, IEnumerable<string> memberNames) : base(errorMessage, memberNames) { }
        protected CompositeValidationResult(ValidationResult validationResult) : base(validationResult) { }

        public void AddResult(ValidationResult validationResult)
        {
            _results.Add(validationResult);
        }
    }
}
