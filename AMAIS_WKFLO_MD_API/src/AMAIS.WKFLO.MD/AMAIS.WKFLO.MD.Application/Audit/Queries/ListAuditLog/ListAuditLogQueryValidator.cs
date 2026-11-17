using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.MD.Application.Common;
using FluentValidation;

namespace AMAIS.WKFLO.MD.Application.Audit.Queries.ListAuditLog
{
    public class ListAuditLogQueryValidator : BaseMasterDataValidator<ListAuditLogQuery>
    {
        private static readonly string[] ValidActions =
        [
            CommonConsts.AuditActionConsts.Create,
            CommonConsts.AuditActionConsts.Update,
            CommonConsts.AuditActionConsts.Delete
        ];

        public ListAuditLogQueryValidator(IMasterDataDbContext db, TenantContext tenantContext) : base(db, tenantContext)
        {
            RuleFor(m => m)
               .Must(ThereIsTenantInDataContext)
               .WithMessage(ErrorConst.TenantInvalid)
               .WithName(nameof(TenantContext.TenantName));

            RuleFor(m => m)
               .Must(BeExistingTenant)
               .WithMessage(ErrorConst.TenantMustExist)
               .WithName(nameof(TenantContext.TenantName));

            RuleFor(m => m.Action)
                .Must(action => string.IsNullOrWhiteSpace(action) || ValidActions.Contains(action))
                .WithMessage($"Action must be one of: {string.Join(", ", ValidActions)}.");

            RuleFor(m => m.Page)
                .GreaterThanOrEqualTo(1)
                .WithMessage("Page must be greater than or equal to 1.");

            RuleFor(m => m.PageSize)
                .InclusiveBetween(1, 100)
                .WithMessage("PageSize must be between 1 and 100.");

            RuleFor(m => m)
                .Must(m => !m.DateFrom.HasValue || !m.DateTo.HasValue || m.DateFrom <= m.DateTo)
                .WithMessage("DateFrom must be less than or equal to DateTo.")
                .WithName(nameof(ListAuditLogQuery.DateFrom));
        }
    }
}
