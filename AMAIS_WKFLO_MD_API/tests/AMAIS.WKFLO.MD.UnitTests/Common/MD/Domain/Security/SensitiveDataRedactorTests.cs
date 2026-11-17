using AMAIS.WKFLO.Common.MD.Domain.Security;
using Newtonsoft.Json.Linq;

namespace AMAIS.WKFLO.MD.UnitTests.Common.MD.Domain.Security
{
    public class SensitiveDataRedactorTests
    {
        private sealed class CallbackMessage : IOAuthCallbackRequest
        {
        }

        [Theory]
        [InlineData("clientSecret")]
        [InlineData("client_secret")]
        [InlineData("codeVerifier")]
        [InlineData("code_verifier")]
        [InlineData("accessToken")]
        [InlineData("access_token")]
        [InlineData("RefreshToken")]
        [InlineData("refresh_token")]
        public void Redact_AlwaysSensitiveName_MaskedEvenInsideJsonString(string name)
        {
            var inner = new JObject { [name] = "s3cret-value", ["other"] = "kept" }.ToString(Newtonsoft.Json.Formatting.None);
            var outer = new JObject { ["Payload"] = inner }.ToString(Newtonsoft.Json.Formatting.None);

            var result = SensitiveDataRedactor.Redact(outer);

            Assert.DoesNotContain("s3cret-value", result);
            Assert.Contains("kept", result);
        }

        [Fact]
        public void Redact_Default_LeavesCodeAndStateReadable()
        {
            const string text = "{\"Code\":\"WKFLOMD0403\",\"State\":\"Draft\"}";

            Assert.Same(text, SensitiveDataRedactor.Redact(text));
        }

        [Fact]
        public void RedactFor_OAuthCallback_MasksCodeAndState()
        {
            var result = JObject.Parse(SensitiveDataRedactor.RedactFor(new CallbackMessage(), "{\"Code\":\"auth-code\",\"State\":\"st\",\"Iss\":\"https://issuer\"}"));

            Assert.Equal("***", result.Value<string>("Code"));
            Assert.Equal("***", result.Value<string>("State"));
            Assert.Equal("https://issuer", result.Value<string>("Iss"));
        }

        [Fact]
        public void RedactFor_OAuthCallback_DoesNotMaskLongerNamesContainingCode()
        {
            var result = JObject.Parse(SensitiveDataRedactor.RedactFor(new CallbackMessage(), "{\"Code\":\"auth-code\",\"ErrorCode\":\"E1\",\"StateName\":\"x\"}"));

            Assert.Equal("E1", result.Value<string>("ErrorCode"));
            Assert.Equal("x", result.Value<string>("StateName"));
        }

        [Fact]
        public void RedactFor_OAuthCallback_NonJsonText_MaskedByPattern()
        {
            var result = SensitiveDataRedactor.RedactFor(new CallbackMessage(), "callback \"code\": \"auth-code\" \"errorCode\": \"E1\"");

            Assert.DoesNotContain("auth-code", result);
            Assert.Contains("E1", result);
        }

        [Fact]
        public void RedactFor_NonCallbackMessage_LeavesCodeReadable()
        {
            const string text = "{\"Code\":\"WKFLOMD0403\"}";

            Assert.Same(text, SensitiveDataRedactor.RedactFor(new object(), text));
        }
    }
}
