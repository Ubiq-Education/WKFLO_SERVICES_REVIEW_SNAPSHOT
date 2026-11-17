namespace AMAIS.WKFLO.Common.Models.Results
{
    public class NotFoundResult : ErrorResult<Error>
    {
        public NotFoundResult() : base() { }

        public NotFoundResult(string key, string errorCode) : base()
        {
            AddError(key, errorCode);
        }

        public void AddError(string property, string code)
        {
            Errors.Add(new Error()
            {
                Property = property,
                Code = code
            });
        }

        public override ResultType ResultType => ResultType.NotFound;
    }
}
