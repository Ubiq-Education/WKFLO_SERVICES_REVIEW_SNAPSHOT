namespace AMAIS.WKFLO.Common.Models.Results
{
    public class SuccessCreateResult<T> : SuccessResult<T>
    {
        public SuccessCreateResult(T data) : base(data)
        {
        }

        public override ResultType ResultType => ResultType.Created;
    }
}