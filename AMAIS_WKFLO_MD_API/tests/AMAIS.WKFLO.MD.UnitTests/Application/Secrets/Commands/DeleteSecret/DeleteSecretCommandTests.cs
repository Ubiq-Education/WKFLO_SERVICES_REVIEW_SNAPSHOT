using AMAIS.WKFLO.MD.Application.Secrets.Commands.DeleteSecret;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.Secrets.Commands.DeleteSecret
{
    public class DeleteSecretCommandTests
    {
        [Theory]
        [MemberData(nameof(GetSecretData))]
        public void ValidateDeleteSecretCommand(DeleteSecretData data, bool expected)
        {
            var command = new DeleteSecretCommand()
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
                    new DeleteSecretData() { Id = 1 },
                    true
            };

            yield return new object[]
            {
                    new DeleteSecretData() { Id = 2 },
                    true
            };
        }
    }

    public class DeleteSecretData
    {
        public int Id { get; set; }
    }
}
