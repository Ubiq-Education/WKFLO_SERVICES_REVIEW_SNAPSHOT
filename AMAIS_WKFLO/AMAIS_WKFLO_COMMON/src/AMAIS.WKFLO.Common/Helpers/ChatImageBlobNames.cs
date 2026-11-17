namespace AMAIS.WKFLO.Common.Helpers
{
    /// <summary>
    /// US 7974 / Task 7979: the one place that builds blob names in the <see cref="ContainerName"/> container.
    /// New blobs are written under the owning tenant's Guid, so a tenant's images can be listed (and purged at that
    /// tenant's own local midnight) with the <see cref="TenantPrefix"/> prefix:
    /// <list type="bullet">
    /// <item>user uploads: <c>{tenantGuid}/{sessionId}/{imageId}-{fileName}</c></item>
    /// <item>edited/generated images: <c>{tenantGuid}/edited/{imageId}.{extension}</c></item>
    /// </list>
    /// The tenant Guid is always written in the lowercase "D" format. When the tenant is not known (no valid Guid),
    /// the legacy, tenant-less names are returned: <c>{sessionId}/{imageId}-{fileName}</c> and
    /// <c>edited/{imageId}.{extension}</c>. Blobs written before this change also use those legacy names; readers find
    /// blobs from their URL path (everything after the container segment), so both shapes keep working.
    /// </summary>
    public static class ChatImageBlobNames
    {
        public const string ContainerName = "chat-images";

        public const string EditedFolder = "edited";

        /// <summary>
        /// Normalises <paramref name="tenantGuid"/> to the segment used in blob names (lowercase "D" format).
        /// False, with a null <paramref name="segment"/>, when it is missing or not a Guid.
        /// </summary>
        public static bool TryGetTenantSegment(string tenantGuid, out string segment)
        {
            if (!string.IsNullOrWhiteSpace(tenantGuid) && Guid.TryParse(tenantGuid.Trim(), out var guid) && guid != Guid.Empty)
            {
                segment = guid.ToString("D");
                return true;
            }

            segment = null;
            return false;
        }

        /// <summary>The prefix every blob of the tenant starts with (for listing): <c>{tenantGuid}/</c>.</summary>
        public static string TenantPrefix(Guid tenantGuid) => $"{tenantGuid:D}/";

        /// <summary>
        /// Blob name for an image the user uploaded in a chat: <c>{tenantGuid}/{sessionId}/{imageId}-{fileName}</c>,
        /// or the legacy <c>{sessionId}/{imageId}-{fileName}</c> when <paramref name="tenantGuid"/> is not a valid Guid.
        /// Only the file name part of <paramref name="fileName"/> is kept.
        /// </summary>
        public static string ForUserUpload(string tenantGuid, string sessionId, Guid imageId, string fileName) =>
            WithTenantPrefix(tenantGuid, $"{sessionId}/{imageId}-{Path.GetFileName(fileName)}");

        /// <summary>
        /// Blob name for an image produced by the image edit/generation tool: <c>{tenantGuid}/edited/{imageId}.{extension}</c>,
        /// or the legacy <see cref="EditedImageFileName"/> when <paramref name="tenantGuid"/> is not a valid Guid.
        /// </summary>
        public static string ForEditedImage(string tenantGuid, Guid imageId, string extension) =>
            WithTenantPrefix(tenantGuid, EditedImageFileName(imageId, extension));

        /// <summary>
        /// The tenant-less name of an edited image, <c>edited/{imageId}.{extension}</c>: the file name shown to the
        /// model and the chat for it (unchanged from before tenant prefixes, so the tenant Guid is not put in prompts).
        /// </summary>
        public static string EditedImageFileName(Guid imageId, string extension) => $"{EditedFolder}/{imageId}.{extension}";

        private static string WithTenantPrefix(string tenantGuid, string relativeName) =>
            TryGetTenantSegment(tenantGuid, out var segment) ? $"{segment}/{relativeName}" : relativeName;
    }
}
