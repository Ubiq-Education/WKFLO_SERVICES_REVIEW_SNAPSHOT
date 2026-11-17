using AMAIS.WKFLO.MD.Application.AIModels.Queries.GetAIModelById;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AIModels.Queries.GetAIModelById
{
    public class GetAIModelByIdQueryTests
    {
        private static readonly Random random = new();

        [Theory]
        [MemberData(nameof(GetAIModelData))]
        public void ValidateGetAIModelByIdQuery(GetAIModelData data, bool expected)
        {
            var command = new GetAIModelByIdQuery()
            {
                Id = data.Id
            };
            var context = new ValidationContext(command, serviceProvider: null, items: null);
            var validationResults = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(command, context, validationResults, true);
            Assert.Equal(expected, isValid);
        }

        public static IEnumerable<object[]> GetAIModelData()
        {
            yield return new object[]
            {
                    new GetAIModelData() { Id = random.Next(1, int.MaxValue) },
                    true
            };

            yield return new object[]
            {
                    new GetAIModelData() { Id = 0 },
                    true
            };
        }
    }

    public class GetAIModelData
    {
        public int Id { get; set; }
    }
}
