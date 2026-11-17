using AMAIS.WKFLO.AI.WebAPI.Infrastructure.CORS;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Infrastructure.CORS
{
    public class DynamicCorsPolicyProviderTests
    {
        private readonly CorsTestEnvironment _env = new();

        private DynamicCorsPolicyProvider CreateProvider()
            => new(_env.CreateStore(), _env.BuildConfiguration(), NullLogger<DynamicCorsPolicyProvider>.Instance);

        private static DefaultHttpContext ContextFor(string origin)
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Headers.Origin = origin;
            return ctx;
        }

        [Fact]
        public async Task AllowAnyOrigin_AllowsAnyOriginWithCredentials_AndDoesNotThrow()
        {
            _env.Config["CorsSettings:AllowAnyOrigin"] = "true";
            var provider = CreateProvider();

            var policy = await provider.GetPolicyAsync(new DefaultHttpContext(), "p");

            Assert.True(policy.SupportsCredentials);
            Assert.True(policy.IsOriginAllowed("https://anything.example"));
            Assert.Equal(0, _env.Loads);
        }

        [Fact]
        public async Task UsesLiveStore_AndReusesPolicy()
        {
            _env.Rows.Add(CorsTestEnvironment.Row("https://a.com"));
            var provider = CreateProvider();
            var ctx = ContextFor("https://a.com");

            var p1 = await provider.GetPolicyAsync(ctx, "p");
            var p2 = await provider.GetPolicyAsync(ctx, "p");

            Assert.Same(p1, p2);
            Assert.True(p1.SupportsCredentials);
            Assert.True(p1.IsOriginAllowed("https://a.com"));
            Assert.False(p1.IsOriginAllowed("https://b.com"));
        }

        [Fact]
        public async Task CachedPolicy_AcceptsOriginAddedToDbAfterPolicyCreation()
        {
            _env.Rows.Add(CorsTestEnvironment.Row("https://a.com"));
            var provider = CreateProvider();
            var p1 = await provider.GetPolicyAsync(ContextFor("https://a.com"), "p");
            Assert.False(p1.IsOriginAllowed("https://new.com"));

            _env.Rows.Add(CorsTestEnvironment.Row("https://new.com"));
            _env.Time.Advance(TimeSpan.FromSeconds(31));
            var p2 = await provider.GetPolicyAsync(ContextFor("https://new.com"), "p");

            Assert.Same(p1, p2);
            Assert.True(p1.IsOriginAllowed("https://new.com"));
        }

        [Fact]
        public void PolicyBuilder_AllowAnyOriginWithCredentials_ThrowsOnBuild()
        {
            // Why the old dev AllowAnyOrigin path fell back to localhost.
            Assert.Throws<InvalidOperationException>(
                () => new CorsPolicyBuilder().AllowAnyOrigin().AllowCredentials().Build());
        }
    }
}
