using AMAIS.WKFLO.Common.Infrastructure.Providers;
using Azure.Storage.Blobs.Models;
using System;

namespace AMAIS.WKFLO.MD.UnitTests.Infrastructure.Providers
{
    /// <summary>
    /// Covers the mapping used by <see cref="BlobStorageProvider.ListBlobsAsync"/> from a listed <see cref="BlobItem"/>
    /// (built with the SDK's <see cref="BlobsModelFactory"/>, no storage account needed).
    /// </summary>
    public class BlobStorageProviderListBlobsTests
    {
        [Fact]
        public void ToBlobListItem_MapsNameAndCreatedOn()
        {
            var createdOn = new DateTimeOffset(2026, 10, 21, 23, 59, 0, TimeSpan.Zero);
            var item = BlobsModelFactory.BlobItem(
                name: "6f1c0a52-0000-0000-0000-000000000001/session-1/image-1.png",
                properties: BlobsModelFactory.BlobItemProperties(accessTierInferred: false, createdOn: createdOn));

            var result = BlobStorageProvider.ToBlobListItem(item);

            Assert.Equal("6f1c0a52-0000-0000-0000-000000000001/session-1/image-1.png", result.Name);
            Assert.Equal(createdOn, result.CreatedOn);
        }

        [Fact]
        public void ToBlobListItem_NoCreatedOn_MapsNull()
        {
            var item = BlobsModelFactory.BlobItem(
                name: "edited/image-2.png",
                properties: BlobsModelFactory.BlobItemProperties(accessTierInferred: false));

            var result = BlobStorageProvider.ToBlobListItem(item);

            Assert.Equal("edited/image-2.png", result.Name);
            Assert.Null(result.CreatedOn);
        }
    }
}
