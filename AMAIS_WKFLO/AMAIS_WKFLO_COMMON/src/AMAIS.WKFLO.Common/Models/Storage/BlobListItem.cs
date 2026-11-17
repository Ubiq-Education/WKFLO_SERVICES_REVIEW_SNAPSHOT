namespace AMAIS.WKFLO.Common.Models.Storage
{
    /// <summary>
    /// A blob returned by <see cref="Interfaces.Providers.IBlobStorageProvider.ListBlobsAsync"/>: its name and when it
    /// was created (null when the storage service did not report it).
    /// </summary>
    public sealed record BlobListItem(string Name, DateTimeOffset? CreatedOn);
}
