using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.MD.Application.Common.Validators;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Settings;
using AMAIS.WKFLO.MD.Application.Common;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace AMAIS.WKFLO.MD.Application.Knowledge.Commands.DeleteTenantKnowledge
{
    public class DeleteTenantKnowledgeCommandValidator : DataStreamMasterDataValidator<DeleteTenantKnowledgeCommand>
    {
        public DeleteTenantKnowledgeCommandValidator(IDatastreamProvider datastreamProvider, IOptions<ElasticsearchSettings> settings, IMasterDataDbContext db, TenantContext tenantContext)
            : base(datastreamProvider, settings, db, tenantContext)
        {
            RuleFor(m => m)
               .Must(ThereIsTenantInDataContext)
               .WithMessage(ErrorConst.TenantInvalid)
               .WithName(nameof(TenantContext.TenantName));

            RuleFor(m => m)
               .Must(BeExistingTenant)
               .WithMessage(ErrorConst.TenantMustExist)
               .WithName(nameof(TenantContext.TenantName));

            RuleFor(m => m)
               .MustAsync(BeExistingTenantKnowledge)
               .WithMessage(ErrorConst.KnowledgeMustExist)
               .WithName("Knowledge");
        }
    }
}
