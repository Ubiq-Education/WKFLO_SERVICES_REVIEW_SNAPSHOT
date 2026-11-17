using AMAIS.WKFLO.MD.Application.Agents.Queries.GetTenantAgentById;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Agents.Queries.GetTenantAgentById
{
    public class GetTenantAgentByIdQueryTests
    {
        private static readonly Random random = new();

        [Theory]
        [MemberData(nameof(GetTenantAgentData))]
        public void ValidateGetTenantAgentByIdQuery(GetTenantAgentData data, bool expected)
        {
            var command = new GetTenantAgentByIdQuery()
            {
                Id = data.Id
            };
            var context = new ValidationContext(command, serviceProvider: null, items: null);
            var validationResults = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(command, context, validationResults, true);
            Assert.Equal(expected, isValid);
        }

        public static IEnumerable<object[]> GetTenantAgentData()
        {
            yield return new object[]
            {
                    new GetTenantAgentData() { Id = random.Next(1, int.MaxValue) },
                    true
            };

            yield return new object[]
            {
                    new GetTenantAgentData() { Id = 0 },
                    true
            };
        }
    }

    public class GetTenantAgentData
    {
        public int Id { get; set; }
    }
}
