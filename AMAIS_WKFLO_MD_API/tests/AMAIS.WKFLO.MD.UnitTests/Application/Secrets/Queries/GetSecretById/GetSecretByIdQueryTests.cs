using AMAIS.WKFLO.MD.Application.Secrets.Queries.GetSecretById;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Secrets.Queries.GetSecretById
{
    public class GetSecretByIdQueryTests
    {
        private static readonly Random random = new();

        [Theory]
        [MemberData(nameof(GetSecretData))]
        public void ValidateGetSecretByIdQuery(GetSecretData data, bool expected)
        {
            var command = new GetSecretByIdQuery()
            {
                Id = data.Id
            };
            var context = new ValidationContext(command, serviceProvider: null, items: null);
            var validationResults = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(command, context, validationResults, true);
            Assert.Equal(expected, isValid);
        }

        public static IEnumerable<object[]> GetSecretData()
        {
            yield return new object[]
            {
                    new GetSecretData() { Id = random.Next(1, int.MaxValue) },
                    true
            };

            yield return new object[]
            {
                    new GetSecretData() { Id = 0 },
                    true
            };
        }
    }

    public class GetSecretData
    {
        public int Id { get; set; }
    }
}
