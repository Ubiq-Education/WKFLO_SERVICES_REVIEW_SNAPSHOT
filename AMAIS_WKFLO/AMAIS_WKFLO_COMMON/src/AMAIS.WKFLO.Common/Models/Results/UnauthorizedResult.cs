namespace AMAIS.WKFLO.Common.Models.Results
{
    public class UnauthorizedResult : ErrorResult<Error>
    {
        public UnauthorizedResult() : base() { }

        public override ResultType ResultType => ResultType.Unauthorized;
    }
}
