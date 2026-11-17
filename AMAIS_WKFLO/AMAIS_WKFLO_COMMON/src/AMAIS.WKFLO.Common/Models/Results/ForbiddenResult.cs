namespace AMAIS.WKFLO.Common.Models.Results
{
    public class ForbiddenResult : ErrorResult<Error>
    {
        public ForbiddenResult() : base() { }

        public ForbiddenResult(string key, string errorCode) : base()
        {
            AddError(key, errorCode);
        }

        public ForbiddenResult AddError(string property, string code)
        {
            Errors.Add(new Error()
            {
                Property = property,
                Code = code
            });

            return this;
        }

        public override ResultType ResultType => ResultType.Forbidden;
    }
}
