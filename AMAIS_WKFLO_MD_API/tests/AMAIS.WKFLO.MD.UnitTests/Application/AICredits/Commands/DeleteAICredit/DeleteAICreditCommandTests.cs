using AMAIS.WKFLO.MD.Application.AICredits.Commands.DeleteAICredit;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AICredits.Commands.DeleteAICredit
{
    public class DeleteAICreditCommandTests
    {
        [Theory]
        [MemberData(nameof(GetAICreditData))]
        public void ValidateDeleteAICreditCommand(DeleteAICreditData data, bool expected)
        {
            var command = new DeleteAICreditCommand()
            {
                Id = data.Id
            };
            var context = new ValidationContext(command, serviceProvider: null, items: null);
            var validationResults = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(command, context, validationResults, true);
            Assert.Equal(expected, isValid);
        }

        public static IEnumerable<object[]> GetAICreditData()
        {
            yield return new object[]
            {
                new DeleteAICreditData() { Id = 1 },
                true
            };

            yield return new object[]
            {
                new DeleteAICreditData() { Id = 2 },
                true
            };
        }
    }

    public class DeleteAICreditData
    {
        public int Id { get; set; }
    }
}
