using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.Common.Infrastructure.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly IMasterDataDbContext _db;
        private readonly ILogger<UserRepository> _logger;

        public UserRepository(IMasterDataDbContext db, ILogger<UserRepository> logger)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<User> EnsureUserExistsAsync(string email, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                throw new ArgumentException("Email cannot be null or empty.", nameof(email));
            }

            try
            {
                // Check if user already exists
                var existingUser = await _db.Users
                    .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

                if (existingUser != null)
                {
                    return existingUser;
                }

                // Create new user
                var newUser = new User
                {
                    Id = Guid.NewGuid(),
                    Email = email
                };

                _db.Users.Add(newUser);

                try
                {
                    await _db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    // Another request created the same user concurrently; detach ours and return the winner.
                    _db.Entry(newUser).State = EntityState.Detached;

                    var concurrentUser = await _db.Users
                        .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

                    if (concurrentUser != null)
                    {
                        return concurrentUser;
                    }

                    throw;
                }

                _logger.LogInformation("Created new user with email: {Email} and ID: {UserId}",
                    email, newUser.Id);

                return newUser;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring user exists for email: {Email}", email);
                throw;
            }
        }
    }
}
