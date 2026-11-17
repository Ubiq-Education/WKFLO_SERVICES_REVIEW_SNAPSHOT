using AMAIS.WKFLO.MD.Application.Secrets.Commands.CreateSecret;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Secrets.Commands.CreateSecret
{
    public class CreateSecretCommandTests
    {
        private static readonly string ApiKeyTooLong = new('*', 501);
        private static readonly string EndpointTooLong = new('*', 501);

        [Theory]
        [MemberData(nameof(GetSecretData))]
        public void ValidateCreateSecretCommand(CreateSecretData data, bool expected)
        {
            var command = new CreateSecretCommand()
            {
                ApiKey = data.ApiKey,
                Name = data.Name,
                IsActive = data.IsActive,
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
                    new CreateSecretData() { ApiKey = "ApiKey 1", Name = "Name", IsActive = true },
                    true
            };

            yield return new object[]
            {
                    new CreateSecretData() { ApiKey = null, Name = "Name", IsActive = true  },
                    false
            };

            yield return new object[]
            {
                    new CreateSecretData() { ApiKey = "ApiKey 2", Name = null, IsActive = true  },
                    false
            };

            yield return new object[]
            {
                    new CreateSecretData() { ApiKey = "ApiKey 2", Name = "Name", IsActive = false  },
                    true
            };

            yield return new object[]
            {
                    new CreateSecretData() { ApiKey = ApiKeyTooLong, Name = "Name", IsActive = true },
                    false
            };

            yield return new object[]
            {
                    new CreateSecretData() { ApiKey = "ApiKey 2", Name = EndpointTooLong, IsActive = true },
                    false
            };
        }
    }

    public class CreateSecretData
    {
        public string ApiKey { get; set; }
        public string Name { get; set; }
        public bool IsActive { get; set; }
    }
}
