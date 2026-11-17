using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tags.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Tags.Commands.CreateTag
{
    public class CreateTagCommandHandler : IRequestHandler<CreateTagCommand, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;

        public CreateTagCommandHandler(IMasterDataDbContext db, IWkfloMapper mapper, TenantContext tenantContext)
        {
            _db = db;
            _mapper = mapper;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(CreateTagCommand command, CancellationToken cancellationToken)
        {
            var tag = _mapper.Map<Tag>(command);

            var tenant = await _db.Tenants
                .Where(m => m.Name == _tenantContext.TenantName)
                .FirstOrDefaultAsync(cancellationToken);

            tag.Tenant = tenant ?? new Tenant { Name = _tenantContext.TenantName };

            await _db.Tags.AddAsync(tag, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            return new SuccessResult<TagCreateResult>(_mapper.Map<TagCreateResult>(tag));
        }
    }
}
