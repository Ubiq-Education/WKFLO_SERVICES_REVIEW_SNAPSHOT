using AMAIS.WKFLO.MD.Application.Tools.Queries.GetMarketplaceToolById;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Tools.Queries.GetMarketplaceToolById
{
    public class GetMarketplaceToolByIdQueryTests
    {
        private static readonly Random random = new();

        [Theory]
        [MemberData(nameof(GetMarketplaceToolData))]
        public void ValidateGetMarketplaceToolByIdQuery(GetMarketplaceToolData data, bool expected)
        {
            var command = new GetMarketplaceToolByIdQuery()
            {
                Id = data.Id
            };
            var context = new ValidationContext(command, serviceProvider: null, items: null);
            var validationResults = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(command, context, validationResults, true);
            Assert.Equal(expected, isValid);
        }

        public static IEnumerable<object[]> GetMarketplaceToolData()
        {
            yield return new object[]
            {
                    new GetMarketplaceToolData() { Id = random.Next(1, int.MaxValue) },
                    true
            };

            yield return new object[]
            {
                    new GetMarketplaceToolData() { Id = 0 },
                    true
            };
        }
    }

    public class GetMarketplaceToolData
    {
        public int Id { get; set; }
    }
}
