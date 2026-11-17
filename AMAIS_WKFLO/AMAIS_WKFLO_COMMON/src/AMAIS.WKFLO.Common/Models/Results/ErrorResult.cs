namespace AMAIS.WKFLO.Common.Models.Results
{
    public abstract class ErrorResult<T> : Result where T : class
    {
        public List<T> Errors { get; } = new List<T>();
    }
}
