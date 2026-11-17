using AMAIS.WKFLO.AI.Application.Agents.Commands.RunPublicAgentByShareId;
using AMAIS.WKFLO.AI.Application.Agents.Commands.RunTenantAgentById;
using AMAIS.WKFLO.AI.WebAPI.EndPoints.Public.Agents;
using AMAIS.WKFLO.AI.WebAPI.EndPoints.Tenant.Agents;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.AI.Models;
using AMAIS.WKFLO.Common.AI.Application.Services.Documents;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AMAIS.WKFLO.AI.UnitTests.EndPoints
{
    /// <summary>
    /// Bug 8189 / Task 8219: the synchronous run endpoints bind attachments from the request body, so they clear the fields only
    /// the server-side hand-off may set before the command reaches its handler.
    /// </summary>
    public class AgentRunEndpointsServerFieldsTests
    {
        private static List<FileAttachment> Files() =>
        [
            new() { FileName = "x.pdf", SourceDocumentId = Guid.NewGuid(), SourceDocument = new AttachedDocumentInfo { DocumentId = Guid.NewGuid() } }
        ];

        [Fact]
        public async Task TenantAgentRun_ClearsTheServerOnlyDocumentFields()
        {
            var mediator = new Mock<IWkfloMediator>();
            RunTenantAgentByIdCommand sent = null;
            mediator.Setup(m => m.Send(It.IsAny<IWkfloRequest<Result>>(), It.IsAny<CancellationToken>()))
                .Callback<IWkfloRequest<Result>, CancellationToken>((c, _) => sent = (RunTenantAgentByIdCommand)c)
                .ReturnsAsync(new SuccessResult<bool>(true));
            var command = new RunTenantAgentByIdCommand { Input = new RunTenantAgentInputCommand { Text = "hi", Files = Files() } };

            await new TenantAgentRun(mediator.Object).HandleAsync(1, command, TestContext.Current.CancellationToken);

            var file = Assert.Single(sent.Input.Files);
            Assert.Null(file.SourceDocumentId);
            Assert.Null(file.SourceDocument);
        }

        [Fact]
        public async Task PublicAgentChatRun_ClearsTheServerOnlyDocumentFields()
        {
            var mediator = new Mock<IWkfloMediator>();
            RunPublicAgentByShareIdCommand sent = null;
            mediator.Setup(m => m.Send(It.IsAny<IWkfloRequest<Result>>(), It.IsAny<CancellationToken>()))
                .Callback<IWkfloRequest<Result>, CancellationToken>((c, _) => sent = (RunPublicAgentByShareIdCommand)c)
                .ReturnsAsync(new SuccessResult<bool>(true));
            var command = new RunPublicAgentByShareIdCommand { Input = new RunPublicAgentInputCommand { Text = "hi", Files = Files() } };

            await new PublicAgentChatRun(mediator.Object).HandleAsync("share-1", command, TestContext.Current.CancellationToken);

            var file = Assert.Single(sent.Input.Files);
            Assert.Null(file.SourceDocumentId);
            Assert.Null(file.SourceDocument);
        }
    }
}
