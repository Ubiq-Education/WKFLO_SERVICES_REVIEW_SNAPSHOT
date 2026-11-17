using AMAIS.WKFLO.MD.Application.Tools.Queries.GetTenantToolById;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantTools.Queries.GetTenantToolById
{
    public class GetTenantToolByIdQueryTests
    {
        private static readonly Random random = new();

        [Theory]
        [MemberData(nameof(GetTenantToolData))]
        public void ValidateGetTenantToolByIdQuery(GetTenantToolData data, bool expected)
        {
            var command = new GetTenantToolByIdQuery()
            {
                Id = data.Id
            };
            var context = new ValidationContext(command, serviceProvider: null, items: null);
            var validationResults = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(command, context, validationResults, true);
            Assert.Equal(expected, isValid);
        }

        public static IEnumerable<object[]> GetTenantToolData()
        {
            yield return new object[]
            {
                    new GetTenantToolData() { Id = random.Next(1, int.MaxValue) },
                    true
            };

            yield return new object[]
            {
                    new GetTenantToolData() { Id = 0 },
                    true
            };
        }
    }

    public class GetTenantToolData
    {
        public int Id { get; set; }
    }
}
