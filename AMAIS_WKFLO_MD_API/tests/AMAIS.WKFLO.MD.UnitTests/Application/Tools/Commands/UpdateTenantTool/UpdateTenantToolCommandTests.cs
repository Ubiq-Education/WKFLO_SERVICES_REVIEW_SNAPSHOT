using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateTenantTool;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.UnitTests.Application.TenantTools.Commands.UpdateTenantTool
{
    public class UpdateTenantToolCommandTests
    {
        private static readonly string NameTooLong = new('*', 301);
        private static readonly string TypeTooLong = new('*', 101);

        [Theory]
        [MemberData(nameof(GetTenantToolData))]
        public void ValidateUpdateTenantToolCommand(UpdateTenantToolData data, bool expected)
        {
            var command = new UpdateTenantToolCommand()
            {
                Id = 1,
                Name = data.Name,
                Description = data.Description,
                Type = data.Type,
                Config = data.Config,
                IsActive = data.IsActive
            };
            var context = new ValidationContext(command, serviceProvider: null, items: null);
            var validationResults = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(command, context, validationResults, true);
            Assert.Equal(expected, isValid);
        }

        // Bug 7871: the PUT endpoint relies on Newtonsoft setting MaxToolOutputLengthSpecified whenever the body
        // carries maxToolOutputLength (null included), even though the flag itself is [JsonIgnore].
        [Theory]
        [InlineData("{\"isActive\":false}", false, null)]
        [InlineData("{\"maxToolOutputLength\":null}", true, null)]
        [InlineData("{\"maxToolOutputLength\":1234}", true, 1234)]
        public void Deserialize_SetsMaxToolOutputLengthSpecifiedOnlyWhenFieldPresent(string json, bool expectedSpecified, int? expectedValue)
        {
            var command = JsonConvert.DeserializeObject<UpdateTenantToolCommand>(json);

            Assert.Equal(expectedSpecified, command.MaxToolOutputLengthSpecified);
            Assert.Equal(expectedValue, command.MaxToolOutputLength);
        }

        [Fact]
        public void Deserialize_IgnoresMaxToolOutputLengthSpecifiedSentByCaller()
        {
            var command = JsonConvert.DeserializeObject<UpdateTenantToolCommand>("{\"maxToolOutputLengthSpecified\":true}");

            Assert.False(command.MaxToolOutputLengthSpecified);
        }

        // Mirrors TenantToolUpdate: pre-fill from the stored tool, then merge the request (nulls skipped). The
        // request's flag must survive the merge, and the stored cap must still be there for the endpoint to keep or
        // overwrite. The pre-fill assigns MaxToolOutputLength on updateCommand, so only the request's flag is
        // meaningful to the endpoint.
        [Theory]
        [InlineData("{\"isActive\":false}", false)]
        [InlineData("{\"maxToolOutputLength\":null}", true)]
        public void EndpointMerge_KeepsStoredCapAndCarriesSpecifiedFlag(string json, bool expectedSpecified)
        {
            var mapper = WkfloMapperExtensions.CreateMapper();
            var request = JsonConvert.DeserializeObject<UpdateTenantToolCommand>(json);
            var updateCommand = new UpdateTenantToolCommand();

            mapper.Map(new TenantTool { Id = 1, Name = "n", Description = "d", Type = "t", Config = "{}", MaxToolOutputLength = 5000 }, updateCommand);
            Assert.True(updateCommand.MaxToolOutputLengthSpecified);

            mapper.Map(request, updateCommand);

            Assert.Equal(5000, updateCommand.MaxToolOutputLength);
            Assert.Equal(expectedSpecified, request.MaxToolOutputLengthSpecified);
            // The merge must not copy the request's flag over the target, or the pre-filled cap would vanish from
            // the serialized command (LoggingBehaviour) whenever the request omitted the field.
            Assert.True(updateCommand.MaxToolOutputLengthSpecified);
            Assert.Contains("\"MaxToolOutputLength\":5000", JsonConvert.SerializeObject(updateCommand));
        }

        // Copilot review on PR 16897: Newtonsoft also uses the Specified flag when writing, so a typed assignment
        // must set it or JsonConvert.SerializeObject would silently drop maxToolOutputLength.
        [Theory]
        [InlineData(1234, "\"maxToolOutputLength\":1234")]
        [InlineData(null, "\"maxToolOutputLength\":null")]
        public void Serialize_IncludesMaxToolOutputLengthOnceAssigned(int? value, string expectedFragment)
        {
            var json = JsonConvert.SerializeObject(new UpdateTenantToolCommand { Id = 1, MaxToolOutputLength = value },
                new JsonSerializerSettings { ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver() });

            Assert.Contains(expectedFragment, json);
            Assert.DoesNotContain("maxToolOutputLengthSpecified", json, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Serialize_OmitsMaxToolOutputLengthWhenNeverAssigned()
        {
            var json = JsonConvert.SerializeObject(new UpdateTenantToolCommand { Id = 1, IsActive = false });

            Assert.DoesNotContain("MaxToolOutputLength", json, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Serialize_RoundTripsExplicitNullAsSpecified()
        {
            var json = JsonConvert.SerializeObject(new UpdateTenantToolCommand { Id = 1, MaxToolOutputLength = null });

            var command = JsonConvert.DeserializeObject<UpdateTenantToolCommand>(json);

            Assert.True(command.MaxToolOutputLengthSpecified);
            Assert.Null(command.MaxToolOutputLength);
        }

        public static IEnumerable<object[]> GetTenantToolData()
        {
            yield return new object[]
            {
                    new UpdateTenantToolData() { Name = "Name Test", Description = "Description Test", Type = "Type Test", Config = "{}", IsActive = true },
                    true
            };

            yield return new object[]
            {
                    new UpdateTenantToolData() { Name = null, Description = "Description Test", Type = "Type Test", Config = "{}", IsActive = true  },
                    false
            };

            yield return new object[]
            {
                    new UpdateTenantToolData() { Name = "Name Test", Description = null, Type = "Type Test", Config = "{}", IsActive = true  },
                    false
            };

            yield return new object[]
            {
                    new UpdateTenantToolData() { Name = "Name Test", Description = "Description Test", Type = null, Config = "{}", IsActive = false  },
                    false
            };

            yield return new object[]
            {
                    new UpdateTenantToolData() {Name = NameTooLong, Description = "Description Test", Type = "Type Test", Config = "{}", IsActive = true},
                    false
            };

            yield return new object[]
            {
                    new UpdateTenantToolData() {Name = "Name Test", Description = "Description Test", Type = TypeTooLong, Config = "{}", IsActive = true},
                    false
            };
        }
    }

    public class UpdateTenantToolData
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Type { get; set; }
        public string Config { get; set; }
        public bool IsActive { get; set; }
    }
}
