using AMAIS.WKFLO.DXP.MCP.Application.Pages;

namespace AMAIS.WKFLO.DXP.MCP.UnitTests.Pages
{
    public class PagePathTests
    {
        [Theory]
        [InlineData("/admissions", "/admissions")]
        [InlineData("admissions", "/admissions")]
        [InlineData("/Admissions/", "/admissions")]
        [InlineData("  /About-Us  ", "/about-us")]
        [InlineData("/news//latest/", "/news/latest")]
        [InlineData("/admissions?utm=x#top", "/admissions")]
        [InlineData("/caf%C3%A9", "/café")]
        [InlineData("https://other.example.com/Admissions/?q=1", "/admissions")]
        [InlineData("/", "/")]
        [InlineData("//", "/")]
        [InlineData("https://www.example.org", "/")]
        public void TryNormalize_ProducesLowercasePathWithLeadingSlash(string input, string expected)
        {
            var ok = PagePath.TryNormalize(input, out var path, out var error);

            Assert.True(ok);
            Assert.Null(error);
            Assert.Equal(expected, path);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void TryNormalize_WhenEmpty_Fails(string? input)
        {
            var ok = PagePath.TryNormalize(input, out var path, out var error);

            Assert.False(ok);
            Assert.Null(path);
            Assert.NotNull(error);
        }

        [Theory]
        [InlineData("https://www.example.org", "/admissions", "https://www.example.org/admissions")]
        [InlineData("https://WWW.Example.org", "/admissions", "https://www.example.org/admissions")]
        [InlineData("https://www.example.org", "/", "https://www.example.org/")]
        [InlineData("http://localhost:44332", "/news", "http://localhost:44332/news")]
        public void BuildCanonicalUrl_CombinesOriginAndPath(string origin, string path, string expected)
        {
            var url = PagePath.BuildCanonicalUrl(new Uri(origin), path);

            Assert.Equal(expected, url.AbsoluteUri);
        }

        [Theory]
        [InlineData("https://www.example.org/Admissions/", "/admissions")]
        [InlineData("https://www.example.org", "/")]
        [InlineData("https://www.example.org/", "/")]
        [InlineData("not a url", null)]
        [InlineData(null, null)]
        public void FromDocumentUrl_NormalisesPath(string? url, string? expected)
        {
            Assert.Equal(expected, PagePath.FromDocumentUrl(url));
        }
    }
}
