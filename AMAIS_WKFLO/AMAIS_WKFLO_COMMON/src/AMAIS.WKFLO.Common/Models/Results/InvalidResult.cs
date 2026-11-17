namespace AMAIS.WKFLO.Common.Models.Results
{
    public class InvalidResult : ErrorResult<Error>
    {
        public InvalidResult() : base() { }

        public InvalidResult(string key, string errorCode) : base()
        {
            AddError(key, errorCode);
        }

        public InvalidResult AddError(string property, string code)
        {
            Errors.Add(new Error()
            {
                Property = property,
                Code = code
            });

            return this;
        }

        public override ResultType ResultType => ResultType.Invalid;
    }
}
