using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.MD.Application.Common;
using FluentValidation;

namespace AMAIS.WKFLO.MD.Application.Tags.Queries.ListTagsByEntity
{
    public class ListTagsByEntityQueryValidator : BaseMasterDataValidator<ListTagsByEntityQuery>
    {
        private static readonly HashSet<string> _validEntityTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            CommonConsts.TaggableEntityTypes.Agent,
            CommonConsts.TaggableEntityTypes.Tool,
            CommonConsts.TaggableEntityTypes.AIModel
        };

        public ListTagsByEntityQueryValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
            RuleFor(m => m)
               .Must(ThereIsTenantInDataContext)
               .WithMessage(ErrorConst.TenantInvalid)
               .WithName(nameof(TenantContext.TenantName));

            RuleFor(m => m)
               .Must(BeExistingTenant)
               .WithMessage(ErrorConst.TenantMustExist)
               .WithName(nameof(TenantContext.TenantName));

            RuleFor(m => m.EntityType)
               .Must(entityType => _validEntityTypes.Contains(entityType))
               .WithMessage(ErrorConst.TagEntityTypeInvalid)
               .WithName(nameof(ListTagsByEntityQuery.EntityType));
        }
    }
}
