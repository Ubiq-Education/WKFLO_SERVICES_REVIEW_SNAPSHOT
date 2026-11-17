namespace AMAIS.WKFLO.Common.Interfaces.Commands
{
    public interface IRequestShareId<T> where T : IEquatable<T>
    {
        T ShareId { get; set; }
    }
}
