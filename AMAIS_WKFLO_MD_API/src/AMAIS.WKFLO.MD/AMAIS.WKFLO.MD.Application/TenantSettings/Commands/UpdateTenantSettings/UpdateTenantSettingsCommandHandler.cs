using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.TenantSettings;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using AMAIS.WKFLO.MD.Application.TenantSettings.Models;
using EFCoreSecondLevelCacheInterceptor;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace AMAIS.WKFLO.MD.Application.TenantSettings.Commands.UpdateTenantSettings
{
    public class UpdateTenantSettingsCommandHandler : IRequestHandler<UpdateTenantSettingsCommand, Result>
    {
        private const int MaxSaveAttempts = 2;

        private readonly IMasterDataDbContext _db;
        private readonly TenantContext _tenantContext;

        public UpdateTenantSettingsCommandHandler(IMasterDataDbContext db, TenantContext tenantContext)
        {
            _db = db;
            _tenantContext = tenantContext;
        }

        public async ValueTask<Result> Handle(UpdateTenantSettingsCommand command, CancellationToken cancellationToken)
        {
            var tenantId = await _db.Tenants
                .AsNoTracking()
                .Where(t => t.Name == _tenantContext.TenantName && !t.IsDeleted)
                .Select(t => (int?)t.Id)
                .Cacheable()
                .FirstOrDefaultAsync(cancellationToken);

            if (tenantId is null)
                return new NotFoundResult();

            // Normalise every provided value up front (the validator has already rejected bad input; this
            // also produces the canonical value to store, e.g. Windows → IANA time zone id). Null = unchanged.
            var invalid = new InvalidResult();
            var timeZone = Normalise(command.TimeZone, TimeZoneIdNormaliser.TryNormalise, invalid, nameof(command.TimeZone), ErrorConst.TenantSettingTimeZoneInvalid);
            var timeFormat = Normalise(command.TimeFormat, TimeFormatNormaliser.TryNormalise, invalid, nameof(command.TimeFormat), ErrorConst.TenantSettingTimeFormatInvalid);
            var dateFormat = Normalise(command.DateFormat, DateFormatNormaliser.TryNormalise, invalid, nameof(command.DateFormat), ErrorConst.TenantSettingDateFormatInvalid);

            if (invalid.Errors.Count > 0)
                return invalid;

            // Null = "not sent, leave unchanged", so this is "the request set at least one value", not "a value
            // differs from the DB": any provided value is assigned and saved (EF skips the UPDATE when it's equal).
            // An empty request has nothing to apply, so it must not insert a defaults row or revive a deleted one.
            var hasRequestedValues = timeZone is not null || timeFormat is not null || dateFormat is not null;

            for (var attempt = 1; ; attempt++)
            {
                // Tracked, and including a soft-deleted row: TenantId is unique regardless of IsDeleted, so a
                // deleted row must be revived rather than a new one inserted.
                var setting = await _db.TenantSettings
                    .Where(s => s.TenantId == tenantId.Value)
                    .FirstOrDefaultAsync(cancellationToken);

                if (!hasRequestedValues)
                    return new SuccessResult<TenantSettingsResult>(TenantSettingsResult.Create(setting));

                var inserted = false;
                if (setting is null)
                {
                    setting = new TenantSetting { TenantId = tenantId.Value };
                    _db.TenantSettings.Add(setting);
                    inserted = true;
                }
                else if (setting.IsDeleted)
                {
                    // A deleted row reads as "never saved" (defaults), so reviving it must not bring back the
                    // values it held before deletion for any setting this request leaves unchanged.
                    setting.IsDeleted = false;
                    setting.TimeZone = TenantSettingDefaults.TimeZone;
                    setting.TimeFormat = TenantSettingDefaults.TimeFormat;
                    setting.DateFormat = TenantSettingDefaults.DateFormat;
                }

                if (timeZone is not null)
                    setting.TimeZone = timeZone;
                if (timeFormat is not null)
                    setting.TimeFormat = timeFormat;
                if (dateFormat is not null)
                    setting.DateFormat = dateFormat;

                try
                {
                    await _db.SaveChangesAsync(cancellationToken);
                    return new SuccessResult<TenantSettingsResult>(TenantSettingsResult.Create(setting));
                }
                catch (DbUpdateException) when (inserted && attempt < MaxSaveAttempts)
                {
                    // The read-then-insert above is not atomic: a concurrent first-time save for the same tenant
                    // can insert its row between our read and our save, and the unique TenantId index rejects
                    // ours. Discard everything this failed save left in the tracker (our Added row and the
                    // AuditLogEntry rows SaveChangesAsync appended for it), then re-read — the competing row is
                    // now visible, so the retry updates it instead of inserting. Bounded to one retry: if the
                    // failure was not that race, the retry fails the same way and the exception surfaces.
                    _db.ChangeTracker.Clear();
                }
            }
        }

        private delegate bool Normaliser(string rawValue, out string normalisedValue);

        /// <summary>
        /// Returns the normalised value, or null when <paramref name="rawValue"/> is null (unchanged) or
        /// invalid (in which case an error is added to <paramref name="invalid"/>).
        /// </summary>
        private static string Normalise(string rawValue, Normaliser normaliser, InvalidResult invalid, string propertyName, string errorCode)
        {
            if (rawValue is null)
                return null;

            if (normaliser(rawValue, out var normalised))
                return normalised;

            invalid.AddError(propertyName, errorCode);
            return null;
        }
    }
}
