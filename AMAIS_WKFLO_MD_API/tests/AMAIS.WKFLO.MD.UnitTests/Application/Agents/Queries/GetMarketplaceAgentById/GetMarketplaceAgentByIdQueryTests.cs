using AMAIS.WKFLO.MD.Application.Agents.Queries.GetMarketplaceAgentById;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Queries.GetMarketplaceAgentById
{
    public class GetMarketplaceAgentByIdQueryTests
    {
        private static readonly Random random = new();

        [Theory]
        [MemberData(nameof(GetMarketplaceAgentData))]
        public void ValidateGetMarketplaceAgentByIdQuery(GetMarketplaceAgentData data, bool expected)
        {
            var command = new GetMarketplaceAgentByIdQuery()
            {
                Id = data.Id
            };
            var context = new ValidationContext(command, serviceProvider: null, items: null);
            var validationResults = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(command, context, validationResults, true);
            Assert.Equal(expected, isValid);
        }

        public static IEnumerable<object[]> GetMarketplaceAgentData()
        {
            yield return new object[]
            {
                    new GetMarketplaceAgentData() { Id = random.Next(1, int.MaxValue) },
                    true
            };

            yield return new object[]
            {
                    new GetMarketplaceAgentData() { Id = 0 },
                    true
            };
        }
    }

    public class GetMarketplaceAgentData
    {
        public int Id { get; set; }
    }
}
