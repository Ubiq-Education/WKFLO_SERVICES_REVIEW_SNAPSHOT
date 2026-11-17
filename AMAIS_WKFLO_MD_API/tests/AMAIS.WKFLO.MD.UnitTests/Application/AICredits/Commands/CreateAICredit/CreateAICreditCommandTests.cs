using AMAIS.WKFLO.MD.Application.AICredits.Commands.CreateAICredit;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AICredits.Commands.CreateAICredit
{
    public class CreateAICreditCommandTests
    {
        [Theory]
        [MemberData(nameof(GetAICreditData))]
        public void ValidateCreateAICreditCommand(CreateAICreditData data, bool expected)
        {
            var command = new CreateAICreditCommand()
            {
                AIModelId = data.AIModelId,
                TokensIn = data.TokensIn,
                CostInMTok = data.CostInMTok,
                TokensOut = data.TokensOut,
                CostOutMTok = data.CostOutMTok,
                MultiplierIn = data.MultiplierIn,
                MultiplierOut = data.MultiplierOut
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
                new CreateAICreditData() { AIModelId = 1, TokensIn = 1000, CostInMTok = 1.5m, TokensOut = 2000, CostOutMTok = 3.0m, MultiplierIn = 1.0m, MultiplierOut = 1.0m },
                true
            };

            yield return new object[]
            {
                new CreateAICreditData() { AIModelId = 1, TokensIn = 0, CostInMTok = 0m, TokensOut = 0, CostOutMTok = 0m, MultiplierIn = 1.0m, MultiplierOut = 1.0m },
                true
            };
        }
    }

    public class CreateAICreditData
    {
        public int AIModelId { get; set; }
        public long TokensIn { get; set; }
        public decimal CostInMTok { get; set; }
        public long TokensOut { get; set; }
        public decimal CostOutMTok { get; set; }
        public decimal MultiplierIn { get; set; }
        public decimal MultiplierOut { get; set; }
    }
}
