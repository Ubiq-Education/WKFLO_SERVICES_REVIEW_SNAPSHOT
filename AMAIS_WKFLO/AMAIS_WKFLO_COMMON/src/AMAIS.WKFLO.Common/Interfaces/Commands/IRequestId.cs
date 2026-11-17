namespace AMAIS.WKFLO.Common.Interfaces.Commands
{
    public interface IRequestId<T> where T : IEquatable<T>
    {
        T Id { get; set; }
    }
}
