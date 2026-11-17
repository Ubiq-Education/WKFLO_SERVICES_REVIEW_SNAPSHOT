using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Audit.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.Audit.Queries.ListAuditLog
{
    public class ListAuditLogQueryHandler : IRequestHandler<ListAuditLogQuery, Result>
    {
        private readonly IMasterDataDbContext _db;
        private readonly IWkfloMapper _mapper;
        private readonly TenantContext _tenantContext;

        public ListAuditLogQueryHandler(IMasterDataDbContext db, IWkfloMapper mapper, TenantContext tenantContext)
        {
            _db = db;
            _mapper = mapper;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(ListAuditLogQuery query, CancellationToken cancellationToken)
        {
            var baseQuery = _db.AuditLogEntries
                .AsNoTracking()
                .Where(a => a.TenantGuid == _tenantContext.TenantGuid);

            if (!string.IsNullOrWhiteSpace(query.EntityType))
            {
                baseQuery = baseQuery.Where(a => a.EntityType == query.EntityType);
            }

            if (!string.IsNullOrWhiteSpace(query.Action))
            {
                baseQuery = baseQuery.Where(a => a.Action == query.Action);
            }

            if (!string.IsNullOrWhiteSpace(query.Actor))
            {
                // Explicitly normalized to lower-case on both sides rather than relying on provider-default
                // collation, so this is guaranteed case-insensitive on SQL Server, SQLite, and the mocked
                // LINQ-to-Objects unit tests alike (SQL Server's default collation happens to be
                // case-insensitive already, but SQLite's is not, and LINQ-to-Objects string.Contains is
                // always case-sensitive) — matching this property's own doc comment.
                var actorLower = query.Actor.ToLower();
                baseQuery = baseQuery.Where(a =>
                    a.ActorUserGuid == query.Actor
                    || (a.ActorUserName != null && a.ActorUserName.ToLower().Contains(actorLower))
                    || (a.ActorUserEmail != null && a.ActorUserEmail.ToLower().Contains(actorLower)));
            }

            if (query.DateFrom.HasValue)
            {
                baseQuery = baseQuery.Where(a => a.Timestamp >= query.DateFrom.Value);
            }

            if (query.DateTo.HasValue)
            {
                // DateTo is treated as inclusive of the whole day it names (the UI sends a bare date, e.g.
                // "2026-09-11", which binds to midnight — a plain "<=" would exclude every entry from that
                // day except ones stamped at exactly midnight). Compare against midnight of the following
                // day instead, in the same offset the caller sent, rather than the UTC-normalized Date.
                var exclusiveUpperBound = new DateTimeOffset(query.DateTo.Value.Date.AddDays(1), query.DateTo.Value.Offset);
                baseQuery = baseQuery.Where(a => a.Timestamp < exclusiveUpperBound);
            }

            baseQuery = baseQuery.OrderByDescending(a => a.Timestamp);

            var totalCount = await baseQuery.CountAsync(cancellationToken);

            var items = await baseQuery
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync(cancellationToken);

            var result = new AuditLogListPageResult
            {
                Items = _mapper.Map<List<AuditLogEntryResult>>(items),
                TotalCount = totalCount,
                Page = query.Page,
                PageSize = query.PageSize,
                TotalPages = (int)Math.Ceiling((double)totalCount / query.PageSize)
            };

            return new SuccessResult<AuditLogListPageResult>(result);
        }
    }
}
