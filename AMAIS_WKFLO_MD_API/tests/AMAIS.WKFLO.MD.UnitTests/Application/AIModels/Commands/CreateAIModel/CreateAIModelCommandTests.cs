using AMAIS.WKFLO.MD.Application.AIModels.Commands.CreateAIModel;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.AIModels.Commands.CreateAIModel
{
    public class CreateAIModelCommandTests
    {
        private static readonly string NameTooLong = new('*', 301);
        private static readonly string ModelTooLong = new('*', 101);
        private static readonly string ProviderTooLong = new('*', 101);
        private static readonly string TypeTooLong = new('*', 101);
        private static readonly string AuthModeTooLong = new('*', 51);

        [Theory]
        [MemberData(nameof(GetAIModelData))]
        public void ValidateCreateAIModelCommand(CreateAIModelData data, bool expected)
        {
            var command = new CreateAIModelCommand()
            {
                Name = data.Name,
                Model = data.Model,
                Provider = data.Provider,
                Type = data.Type,
                IsActive = data.IsActive,
                AuthMode = data.AuthMode
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
                    new CreateAIModelData() { Name = "Name Test", Model = "Model Test", Provider = "Provider Test", Type = "text", IsActive = true },
                    true
            };

            yield return new object[]
            {
                    new CreateAIModelData() { Name = null, Model = "Model Test", Provider = "Provider Test", Type = "text", IsActive = true  },
                    false
            };

            yield return new object[]
            {
                    new CreateAIModelData() { Name = "Name Test", Model = null, Provider = "Provider Test", Type = "text", IsActive = true  },
                    false
            };

            yield return new object[]
            {
                    new CreateAIModelData() { Name = "Name Test", Model = "Model Test", Provider = null, Type = "text", IsActive = false  },
                    false
            };

            yield return new object[]
            {
                    new CreateAIModelData() { Name = "Name Test", Model = "Model Test", Provider = "Provider Test", Type = null, IsActive = false  },
                    false
            };

            yield return new object[]
            {
                    new CreateAIModelData() {Name = NameTooLong, Model = "Model Test", Provider = "Provider Test", Type = "text", IsActive = true},
                    false
            };

            yield return new object[]
            {
                    new CreateAIModelData() {Name = "Name Test", Model = ModelTooLong, Provider = "Provider Test", Type = "text", IsActive = true},
                    false
            };

            yield return new object[]
            {
                    new CreateAIModelData() { Name = "Name Test", Model = "Model Test", Provider = ProviderTooLong, Type = "text", IsActive = false  },
                    false
            };

            yield return new object[]
            {
                    new CreateAIModelData() { Name = "Name Test", Model = "Model Test", Provider = "Provider Test", Type = TypeTooLong, IsActive = false  },
                    false
            };

            yield return new object[]
            {
                    new CreateAIModelData() { Name = "Name Test", Model = "Model Test", Provider = "Provider Test", Type = "text", IsActive = true, AuthMode = "apiKey" },
                    true
            };

            yield return new object[]
            {
                    new CreateAIModelData() { Name = "Name Test", Model = "Model Test", Provider = "Provider Test", Type = "text", IsActive = true, AuthMode = AuthModeTooLong },
                    false
            };
        }
    }

    public class CreateAIModelData
    {
        public string Name { get; set; }
        public string Model { get; set; }
        public string Provider { get; set; }
        public string Type { get; set; }
        public bool IsActive { get; set; }
        public string AuthMode { get; set; }
    }
}
