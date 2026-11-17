namespace AMAIS.WKFLO.Common.Models.Results
{
    /// <summary>HTTP 410: the target existed but is no longer usable (e.g. an expired approval).</summary>
    public class GoneResult : ErrorResult<Error>
    {
        public GoneResult() : base() { }

        public GoneResult(string key, string errorCode) : base()
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

        public override ResultType ResultType => ResultType.Gone;
    }
}
