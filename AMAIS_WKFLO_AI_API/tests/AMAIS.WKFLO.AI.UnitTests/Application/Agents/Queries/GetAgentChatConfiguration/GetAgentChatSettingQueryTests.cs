using AMAIS.WKFLO.AI.Application.Agents.Queries.GetPublicAgentChatConfiguration;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Queries.GetAgentChatConfiguration
{
    public class GetAgentChatSettingQueryTests
    {
        private static readonly Random random = new();

        [Theory]
        [MemberData(nameof(GetAgentChatSettingData))]
        public void ValidateGetAgentChatSettingQuery(GetAgentChatSettingData data, bool expected)
        {
            var query = new GetPublicAgentChatSettingQuery()
            {
                ShareId = data.ShareId
            };

            var context = new ValidationContext(query, serviceProvider: null, items: null);
            var validationResults = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(query, context, validationResults, true);

            Assert.Equal(expected, isValid);
        }

        public static IEnumerable<object[]> GetAgentChatSettingData()
        {
            // Valid cases - non-empty strings
            yield return new object[]
            {
                new GetAgentChatSettingData() { ShareId = Guid.NewGuid().ToString() },
                true
            };

            yield return new object[]
            {
                new GetAgentChatSettingData() { ShareId = "valid-id" },
                true
            };

            yield return new object[]
            {
                new GetAgentChatSettingData() { ShareId = "12345" },
                true
            };

            // Invalid cases - null or empty ShareId
            yield return new object[]
            {
                new GetAgentChatSettingData() { ShareId = null },
                false
            };

            yield return new object[]
            {
                new GetAgentChatSettingData() { ShareId = "" },
                false
            };

            yield return new object[]
            {
                new GetAgentChatSettingData() { ShareId = "   " },
                false
            };
        }
    }

    public class GetAgentChatSettingData
    {
        public string ShareId { get; set; }
        public bool IsWidget { get; set; }
    }
}