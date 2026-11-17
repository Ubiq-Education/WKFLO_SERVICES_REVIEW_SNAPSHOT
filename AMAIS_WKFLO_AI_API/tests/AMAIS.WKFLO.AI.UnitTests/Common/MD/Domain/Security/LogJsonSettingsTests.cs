using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.Infrastructure.Behaviours;
using AMAIS.WKFLO.Common.MD.Domain.Security;
using global::Mediator;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Common.MD.Domain.Security
{
    /// <summary>
    /// Bug 8189: logged requests leave file attachments out, so a large attachment is not copied into the logs.
    /// </summary>
    public class LogJsonSettingsTests
    {
        public sealed class TestCommand : IRequest<bool>
        {
            public string SessionId { get; set; }

            public List<FileAttachment> Files { get; set; }
        }

        private static TestCommand CommandWithFile(string fileData) => new()
        {
            SessionId = "session-1",
            Files = [new FileAttachment { FileName = "report.pdf", FileData = fileData, FileSize = 3 }]
        };

        [Fact]
        public void Serialize_FileAttachment_ReplacesFileDataWithItsLength()
        {
            var json = JsonConvert.SerializeObject(CommandWithFile("data:application/pdf;base64,QUJD"), LogJsonSettings.Default);

            Assert.DoesNotContain("QUJD", json);
            Assert.Contains("\"FileData\":\"[omitted: 32 chars]\"", json);
            Assert.Contains("\"FileName\":\"report.pdf\"", json);
            Assert.Contains("\"SessionId\":\"session-1\"", json);
        }

        [Fact]
        public void Serialize_NullFileData_StaysNull()
        {
            var json = JsonConvert.SerializeObject(CommandWithFile(null), LogJsonSettings.Default);

            Assert.Contains("\"FileData\":null", json);
        }

        [Fact]
        public void Serialize_WithoutOmittedProperties_MatchesDefaultSettings()
        {
            var value = new { SessionId = "session-1", Count = 2, Names = new[] { "a", "b" } };

            Assert.Equal(JsonConvert.SerializeObject(value), JsonConvert.SerializeObject(value, LogJsonSettings.Default));
        }

        [Fact]
        public void Deserialize_WithDefaultSettings_StillReadsFileData()
        {
            // Model binding uses Newtonsoft's default settings: [OmitFromLogs] must not drop the attachment there.
            var json = JsonConvert.SerializeObject(CommandWithFile("data:application/pdf;base64,QUJD"));

            var command = JsonConvert.DeserializeObject<TestCommand>(json);

            Assert.Equal("data:application/pdf;base64,QUJD", command.Files[0].FileData);
        }

        [Fact]
        public async Task LoggingBehaviour_RequestWithAttachment_LogsItWithoutTheFileData()
        {
            var fileData = "data:application/pdf;base64," + new string('A', 400_000);
            var logger = new RecordingLogger<TestCommand>();
            var behaviour = new LoggingBehaviour<TestCommand, bool>(logger);

            await behaviour.Handle(CommandWithFile(fileData), (_, _) => ValueTask.FromResult(true), CancellationToken.None);

            var data = (string)Assert.Single(logger.With("data")).Values["data"];
            Assert.DoesNotContain("AAAA", data);
            Assert.Contains($"[omitted: {fileData.Length} chars]", data);
            Assert.Contains("report.pdf", data);
        }
    }
}
