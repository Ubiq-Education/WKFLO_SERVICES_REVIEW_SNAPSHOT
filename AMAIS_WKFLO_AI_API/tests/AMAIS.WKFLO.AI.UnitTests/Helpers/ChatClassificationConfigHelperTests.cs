using AMAIS.WKFLO.Common.Helpers;
using AMAIS.WKFLO.Common.Models.Config;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.AI.UnitTests.Helpers
{
    public class ChatClassificationConfigHelperTests
    {
        [Fact]
        public void Version_MissingFromStoredJson_DefaultsToOne()
        {
            // Tools configured before the Version field existed have no "version" property in their stored
            // JSON at all — Newtonsoft leaves the property at its object-initializer default in that case.
            const string legacyJson = """{"topics":[{"id":"3fa85f64-5717-4562-b3fc-2c963f66afa6","name":"Billing"}]}""";

            var config = JsonConvert.DeserializeObject<ChatClassificationToolConfig>(legacyJson);

            Assert.Equal(1, config.Version);
        }

        [Fact]
        public void Version_PresentInStoredJson_IsRead()
        {
            const string json = """{"topics":[],"version":5}""";

            var config = JsonConvert.DeserializeObject<ChatClassificationToolConfig>(json);

            Assert.Equal(5, config.Version);
        }

        [Fact]
        public void ResolveTopicVersion_DefaultConfig_ReturnsOne()
        {
            var config = new ChatClassificationToolConfig();

            Assert.Equal("1", ChatClassificationConfigHelper.ResolveTopicVersion(config));
        }

        [Fact]
        public void ResolveTopicVersion_PositiveVersion_ReturnsItAsString()
        {
            var config = new ChatClassificationToolConfig { Version = 7 };

            Assert.Equal("7", ChatClassificationConfigHelper.ResolveTopicVersion(config));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void ResolveTopicVersion_NonPositiveVersion_FallsBackToOne(int corruptVersion)
        {
            // Defensive fallback: versions start at 1 and only ever increment, so a stored 0/negative value
            // is corrupt data, not a legitimate "no version" signal — treat it the same as missing/null.
            var config = new ChatClassificationToolConfig { Version = corruptVersion };

            Assert.Equal("1", ChatClassificationConfigHelper.ResolveTopicVersion(config));
        }
    }
}
