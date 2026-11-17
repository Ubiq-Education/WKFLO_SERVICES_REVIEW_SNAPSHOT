using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Models;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.WebOrigins.Models;
using AMAIS.WKFLO.MD.FunctionalTests.Common.Utilities;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.MD.FunctionalTests.EndPoints.WebOrigins
{
    public class ListTenantWebOriginsQueryEndPointTests(TestFixture fixture) : IClassFixture<TestFixture>, IDisposable
    {
        private const string Url = "/api/v1/tenant/web-origins";
        private const string CallerEmail = "test@example.com"; // email claim of TokenBuilder.BuildJWTToken()

        private TestFixture Fixture => fixture;
        private readonly List<int> _seededWebOriginIds = [];
        private readonly List<int> _seededSuperPowerUserIds = [];

        private void SetHeaders(bool withToken = true)
        {
            Fixture.Client.DefaultRequestHeaders.Clear();
            if (withToken)
                Fixture.Client.DefaultRequestHeaders.Add("Authorization", "Bearer " + TokenBuilder.BuildJWTToken());
            Fixture.Client.DefaultRequestHeaders.Add(CommonConsts.TenantName, "Test");
        }

        private int SeedSuperPowerUser()
        {
            var db = Fixture.GetDbContext();
            var row = new SuperPowerUser { Email = CallerEmail };
            db.SuperPowerUsers.Add(row);
            db.SaveChanges();
            _seededSuperPowerUserIds.Add(row.Id);
            return row.Id;
        }

        private void SeedWebOrigins()
        {
            var db = Fixture.GetDbContext();
            var tenantId = db.Tenants.AsNoTracking().Single(t => t.Name == "Test").Id;
            var otherTenantId = db.Tenants.AsNoTracking().Single(t => t.Name == "Test2").Id;
            var rows = new List<WebOrigin>
            {
                new() { TenantId = tenantId, WebOriginType = "SignalR", Name = "https://active.example.com", IsActive = true },
                new() { TenantId = tenantId, WebOriginType = "SignalR", Name = "https://deleted.example.com", IsActive = true, IsDeleted = true },
                new() { TenantId = otherTenantId, WebOriginType = "SignalR", Name = "https://other-tenant.example.com", IsActive = true }
            };
            db.WebOrigins.AddRange(rows);
            db.SaveChanges();
            _seededWebOriginIds.AddRange(rows.Select(r => r.Id));
        }

        public void Dispose()
        {
            // The fixture (and its SQLite DB) is shared across tests in this class, so remove what was seeded.
            var db = Fixture.GetDbContext();
            db.WebOrigins.RemoveRange(db.WebOrigins.Where(w => _seededWebOriginIds.Contains(w.Id)).ToList());
            db.SuperPowerUsers.RemoveRange(db.SuperPowerUsers.Where(s => _seededSuperPowerUserIds.Contains(s.Id)).ToList());
            db.SaveChanges();
        }

        [Fact]
        public async Task ListWebOrigins_SuperPowerUser_ReturnsTenantRowsIncludingDeleted()
        {
            SeedSuperPowerUser();
            SeedWebOrigins();
            SetHeaders();

            var response = await Fixture.Client.GetAsync(Url, TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            var data = JsonConvert.DeserializeObject<SuccessResult<List<TenantWebOriginListResult>>>(body).Data;
            data.Select(d => d.Name).Should().Equal("https://active.example.com", "https://deleted.example.com");
            data.Single(d => d.Name == "https://deleted.example.com").IsDeleted.Should().BeTrue();
            data.Single(d => d.Name == "https://active.example.com").IsDeleted.Should().BeFalse();
        }

        [Fact]
        public async Task ListWebOrigins_NotSuperPowerUser_ReturnsForbidden()
        {
            SeedWebOrigins();
            SetHeaders();

            var response = await Fixture.Client.GetAsync(Url, TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            body.Should().Contain("WKFLOMD1601");
        }

        [Fact]
        public async Task ListWebOrigins_NoToken_ReturnsUnauthorized()
        {
            SetHeaders(withToken: false);

            var response = await Fixture.Client.GetAsync(Url, TestContext.Current.CancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }
}
