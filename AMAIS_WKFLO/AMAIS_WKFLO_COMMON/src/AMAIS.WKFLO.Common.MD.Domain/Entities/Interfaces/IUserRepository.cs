namespace AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces
{
    public interface IUserRepository
    {
        Task<User> EnsureUserExistsAsync(string email, CancellationToken cancellationToken = default);
    }
}
