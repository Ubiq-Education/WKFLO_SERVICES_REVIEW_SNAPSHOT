using AMAIS.WKFLO.MD.Application.AIModels.Commands.DeleteAIModel;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AIModels.Commands.DeleteAIModel
{
    public class DeleteAIModelCommandTests
    {
        [Theory]
        [MemberData(nameof(GetAIModelData))]
        public void ValidateDeleteAIModelCommand(DeleteAIModelData data, bool expected)
        {
            var command = new DeleteAIModelCommand()
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
                    new DeleteAIModelData() { Id = 1 },
                    true
            };

            yield return new object[]
            {
                    new DeleteAIModelData() { Id = 2 },
                    true
            };
        }
    }

    public class DeleteAIModelData
    {
        public int Id { get; set; }
    }
}
