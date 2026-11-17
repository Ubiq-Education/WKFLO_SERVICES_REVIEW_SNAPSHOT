namespace AMAIS.WKFLO.Common.Models.Results
{
    public class NoContentResult : Result
    {
        public NoContentResult()
        {
        }

        public override ResultType ResultType => ResultType.NoContent;
    }
}
