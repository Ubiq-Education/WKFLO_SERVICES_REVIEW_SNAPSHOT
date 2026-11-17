using AMAIS.WKFLO.Common.Helpers;
using System;

namespace AMAIS.WKFLO.AI.UnitTests.Helpers
{
    public class ChatImageBlobNamesTests
    {
        private const string TenantGuid = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";
        private static readonly Guid ImageId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

        [Fact]
        public void ForUserUpload_KnownTenant_PrefixesWithTenantGuid()
        {
            var name = ChatImageBlobNames.ForUserUpload(TenantGuid, "session-1", ImageId, "photo.png");

            Assert.Equal($"{TenantGuid}/session-1/{ImageId}-photo.png", name);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-a-guid")]
        [InlineData("00000000-0000-0000-0000-000000000000")]
        public void ForUserUpload_UnknownTenant_UsesLegacyName(string tenantGuid)
        {
            var name = ChatImageBlobNames.ForUserUpload(tenantGuid, "session-1", ImageId, "photo.png");

            Assert.Equal($"session-1/{ImageId}-photo.png", name);
        }

        [Fact]
        public void ForUserUpload_FileNameWithDirectories_KeepsOnlyTheFileName()
        {
            var name = ChatImageBlobNames.ForUserUpload(TenantGuid, "session-1", ImageId, "some/folder/photo.png");

            Assert.Equal($"{TenantGuid}/session-1/{ImageId}-photo.png", name);
        }

        [Fact]
        public void ForEditedImage_KnownTenant_PrefixesWithTenantGuid()
        {
            var name = ChatImageBlobNames.ForEditedImage(TenantGuid, ImageId, "png");

            Assert.Equal($"{TenantGuid}/edited/{ImageId}.png", name);
        }

        [Fact]
        public void ForEditedImage_UnknownTenant_UsesLegacyEditedName()
        {
            var name = ChatImageBlobNames.ForEditedImage(null, ImageId, "png");

            Assert.Equal($"edited/{ImageId}.png", name);
            Assert.Equal(ChatImageBlobNames.EditedImageFileName(ImageId, "png"), name);
        }

        [Theory]
        [InlineData("3F2504E0-4F89-11D3-9A0C-0305E82C3301")]
        [InlineData("{3f2504e0-4f89-11d3-9a0c-0305e82c3301}")]
        [InlineData(" 3f2504e04f8911d39a0c0305e82c3301 ")]
        public void TryGetTenantSegment_AnyGuidFormat_NormalisesToLowercaseD(string tenantGuid)
        {
            Assert.True(ChatImageBlobNames.TryGetTenantSegment(tenantGuid, out var segment));
            Assert.Equal(TenantGuid, segment);
        }

        [Fact]
        public void TryGetTenantSegment_Invalid_ReturnsFalseAndNull()
        {
            Assert.False(ChatImageBlobNames.TryGetTenantSegment("nope", out var segment));
            Assert.Null(segment);
        }

        [Fact]
        public void TenantPrefix_MatchesTheStartOfEveryTenantBlobName()
        {
            var prefix = ChatImageBlobNames.TenantPrefix(Guid.Parse(TenantGuid));

            Assert.Equal($"{TenantGuid}/", prefix);
            Assert.StartsWith(prefix, ChatImageBlobNames.ForUserUpload(TenantGuid, "s", ImageId, "a.png"));
            Assert.StartsWith(prefix, ChatImageBlobNames.ForEditedImage(TenantGuid, ImageId, "png"));
        }
    }
}
