using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateTenantTool;
using AMAIS.WKFLO.MD.Application.Tools.Models;
using AMAIS.WKFLO.MD.Application.Tools.Settings;
using Microsoft.Extensions.Options;
using MockQueryable.Moq;
using Moq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantTools.Commands.UpdateTenantTool
{
    /// <summary>WI 7575: permissionsEnabled on update - null keeps the value; the published copy only changes on publish.</summary>
    public class UpdateTenantToolCommandHandlerPermissionsEnabledTests
    {
        private const int ToolId = 4001;

        private static (UpdateTenantToolCommandHandler handler, TenantTool tool, PublishedTenantTool copy, Mock<IMasterDataDbContext> db) Create(bool currentValue)
        {
            var tool = new TenantTool { Id = ToolId, Name = "Tool", Description = "d", Type = "HTTP", Config = "{}", IsActive = true, PermissionsEnabled = currentValue };
            var copy = new PublishedTenantTool { Id = 5001, Type = "HTTP", CreatedByTenantToolId = ToolId, PermissionsEnabled = currentValue };

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.TenantTools).Returns(new List<TenantTool> { tool }.BuildMockDbSet().Object);
            db.Setup(x => x.PublishedTenantTools).Returns(new List<PublishedTenantTool> { copy }.BuildMockDbSet().Object);
            db.Setup(x => x.SaveChangesAsync(default)).ReturnsAsync(1);

            var handler = new UpdateTenantToolCommandHandler(db.Object, WkfloMapperExtensions.CreateMapper(),
                Options.Create(new McpOAuthSettings { PublicBaseUrl = "https://wkflo.example.com" }), Mock.Of<IMcpTokenProtector>());

            return (handler, tool, copy, db);
        }

        private static UpdateTenantToolCommand Command(bool? permissionsEnabled) => new()
        {
            Id = ToolId,
            Name = "Tool",
            Description = "d",
            Type = "HTTP",
            Config = "{}",
            IsActive = true,
            PermissionsEnabled = permissionsEnabled
        };

        [Fact]
        public async Task Handle_NullPermissionsEnabled_KeepsCurrentValue()
        {
            var (handler, tool, copy, db) = Create(currentValue: true);

            var result = await handler.Handle(Command(null), TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantToolUpdateResult>>(result);
            Assert.True(success.Data.PermissionsEnabled);
            Assert.True(tool.PermissionsEnabled);
            Assert.True(copy.PermissionsEnabled);
            db.Verify(x => x.PublishedTenantTools, Times.Never);
        }

        [Fact]
        public async Task Handle_ChangedPermissionsEnabled_NeverTouchesPublishedCopies()
        {
            var (handler, _, copy, db) = Create(currentValue: false);

            await handler.Handle(Command(true), TestContext.Current.CancellationToken);

            Assert.False(copy.PermissionsEnabled);
            db.Verify(x => x.PublishedTenantTools, Times.Never);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public async Task Handle_ChangedPermissionsEnabled_UpdatesTenantTool(bool current, bool requested)
        {
            var (handler, tool, copy, _) = Create(current);

            var result = await handler.Handle(Command(requested), TestContext.Current.CancellationToken);

            var success = Assert.IsType<SuccessResult<TenantToolUpdateResult>>(result);
            Assert.Equal(requested, success.Data.PermissionsEnabled);
            Assert.Equal(requested, tool.PermissionsEnabled);
            Assert.Equal(current, copy.PermissionsEnabled);
        }
    }
}
