namespace AMAIS.WKFLO.Common.Models.Results
{
    /// <summary>HTTP 409: the request conflicts with the current state (e.g. an approval that was already decided).</summary>
    public class ConflictResult : ErrorResult<Error>
    {
        public ConflictResult() : base() { }

        public ConflictResult(string key, string errorCode) : base()
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

        public override ResultType ResultType => ResultType.Conflict;
    }
}
