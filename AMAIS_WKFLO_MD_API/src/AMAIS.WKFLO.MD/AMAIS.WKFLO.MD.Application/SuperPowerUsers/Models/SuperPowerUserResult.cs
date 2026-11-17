using AMAIS.WKFLO.Common.MD.Domain.Entities;

namespace AMAIS.WKFLO.MD.Application.SuperPowerUsers.Models
{
    public class SuperPowerUserResult
    {
        public int Id { get; set; }
        public string Email { get; set; }
        public string CreatedBy { get; set; }
        public DateTimeOffset CreatedOn { get; set; }

        public static SuperPowerUserResult From(SuperPowerUser entity) => new()
        {
            Id = entity.Id,
            Email = entity.Email,
            CreatedBy = entity.CreatedBy,
            CreatedOn = entity.CreatedOn
        };
    }
}
