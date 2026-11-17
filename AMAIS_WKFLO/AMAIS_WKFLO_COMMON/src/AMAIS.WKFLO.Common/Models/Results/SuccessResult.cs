namespace AMAIS.WKFLO.Common.Models.Results
{
    public class SuccessResult<T> : Result
    {
        public SuccessResult(T data)
        {
            Data = data;
        }

        public override ResultType ResultType => ResultType.Ok;

        public T Data { get; }
    }
}