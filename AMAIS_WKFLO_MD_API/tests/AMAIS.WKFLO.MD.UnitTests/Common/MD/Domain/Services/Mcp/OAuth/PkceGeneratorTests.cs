using AMAIS.WKFLO.Common.MD.Domain.Services.Mcp.OAuth;
using System;

namespace AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Services.Mcp.OAuth
{
    public class PkceGeneratorTests
    {
        [Fact]
        public void GenerateCodeVerifier_ReturnsUrlSafeNonEmptyValue()
        {
            var verifier = PkceGenerator.GenerateCodeVerifier();

            Assert.False(string.IsNullOrWhiteSpace(verifier));
            Assert.DoesNotContain('+', verifier);
            Assert.DoesNotContain('/', verifier);
            Assert.DoesNotContain('=', verifier);
        }

        [Fact]
        public void GenerateCodeVerifier_TwoCallsProduceDifferentValues()
        {
            Assert.NotEqual(PkceGenerator.GenerateCodeVerifier(), PkceGenerator.GenerateCodeVerifier());
        }

        [Fact]
        public void GenerateState_TwoCallsProduceDifferentValues()
        {
            Assert.NotEqual(PkceGenerator.GenerateState(), PkceGenerator.GenerateState());
        }

        [Fact]
        public void ComputeCodeChallenge_MatchesRfc7636AppendixBTestVector()
        {
            // RFC 7636 Appendix B's worked example.
            const string verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
            const string expectedChallenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

            var challenge = PkceGenerator.ComputeCodeChallenge(verifier);

            Assert.Equal(expectedChallenge, challenge);
        }

        [Fact]
        public void ComputeCodeChallenge_IsDeterministicForTheSameVerifier()
        {
            var verifier = PkceGenerator.GenerateCodeVerifier();

            Assert.Equal(PkceGenerator.ComputeCodeChallenge(verifier), PkceGenerator.ComputeCodeChallenge(verifier));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ComputeCodeChallenge_NullOrWhitespaceVerifier_Throws(string verifier)
        {
            // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for a null argument
            // (a subtype of ArgumentException) — ThrowsAny accepts either.
            Assert.ThrowsAny<ArgumentException>(() => PkceGenerator.ComputeCodeChallenge(verifier));
        }
    }
}
