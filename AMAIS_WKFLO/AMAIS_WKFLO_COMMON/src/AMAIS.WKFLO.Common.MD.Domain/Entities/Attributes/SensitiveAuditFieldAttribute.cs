namespace AMAIS.WKFLO.Common.MD.Domain.Entities.Attributes
{
    /// <summary>
    /// Marks a domain entity property whose value must never appear in plain text in an
    /// <see cref="AuditLogEntry.Changes"/> diff — e.g. an unencrypted API key or an encrypted-at-rest token
    /// blob with no legitimate audit value. <see cref="Data.MasterDataDbContext"/>'s change-diff builder
    /// checks for this attribute via reflection and writes a fixed mask instead of the real old/new values
    /// when present, while still recording that the field changed.
    /// </summary>
    /// <remarks>
    /// Apply this only to genuinely sensitive fields (unencrypted secrets, or encrypted blobs with no
    /// legitimate reason to expose even as ciphertext) — not to every string property. The audit trail loses
    /// value if over-masked.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class SensitiveAuditFieldAttribute : Attribute
    {
    }
}
