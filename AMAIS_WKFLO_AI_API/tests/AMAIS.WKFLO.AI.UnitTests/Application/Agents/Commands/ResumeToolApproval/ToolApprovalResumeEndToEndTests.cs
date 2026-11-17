using AMAIS.WKFLO.AI.Application.Agents.Commands.DecideToolApproval;
using AMAIS.WKFLO.AI.Application.Agents.Commands.ResumeToolApproval;
using AMAIS.WKFLO.AI.UnitTests.Domain.ToolApproval;
using AMAIS.WKFLO.Common.AI.Application.Domain.Agents.ToolApproval;
using AMAIS.WKFLO.Common.AI.Application.Interfaces.Services.SemanticKernel;
using AMAIS.WKFLO.Common.AI.Application.Services.AI;
using AMAIS.WKFLO.Common.AI.Application.Services.SemanticKernel.Models;
using AMAIS.WKFLO.Common.Contexts;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using AMAIS.WKFLO.Common.Interfaces.Services.WkfloServer;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.MD.Domain.Entities.Interfaces;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.AI;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.Common.Models.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MockQueryable.Moq;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Application.Agents.Commands.ResumeToolApproval
{
    /// <summary>
    /// WI 7575 regression: after "Allow once"/"Allow for this conversation" the conversation must continue - the
    /// decision starts the resume turn (in-process, background), the resume runs in a fresh scope with the decision
    /// request's tenant/session/user/share contexts, and the turn runs the stored call for the same chat.
    /// </summary>
    public class ToolApprovalResumeEndToEndTests
    {
        private static readonly DateTimeOffset Now = new(2026, 5, 1, 10, 5, 0, TimeSpan.Zero);

        [Fact]
        public async Task Decision_StartsBackgroundResume_InNewScope_WithTheConversationIdentityAndChatFromTheRecord()
        {
            // Scoped contexts + a mediator that records what the resume sees inside its own scope.
            ResumeToolApprovalCommand sentCommand = null;
            ChatContext seenChat = null;
            TenantContext seenTenant = null;
            UserContext seenUser = null;
            AgentSharableContext seenSharable = null;

            var services = new ServiceCollection();
            services.AddSingleton<IWkfloMapper>(WkfloMapperExtensions.CreateMapper());
            services.AddScoped<TenantContext>();
            services.AddScoped<ChatContext>();
            services.AddScoped<UserContext>();
            services.AddScoped<AgentSharableContext>();
            services.AddScoped<IWkfloMediator>(sp =>
            {
                var mediator = new Mock<IWkfloMediator>();
                mediator.Setup(m => m.Send(It.IsAny<IWkfloRequest<Result>>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((IWkfloRequest<Result> request, CancellationToken _) =>
                    {
                        sentCommand = (ResumeToolApprovalCommand)request;
                        seenChat = sp.GetRequiredService<ChatContext>();
                        seenTenant = sp.GetRequiredService<TenantContext>();
                        seenUser = sp.GetRequiredService<UserContext>();
                        seenSharable = sp.GetRequiredService<AgentSharableContext>();
                        return new SuccessResult<string>("ok");
                    });
                return mediator.Object;
            });
            var provider = services.BuildServiceProvider();

            // The decision request's own (soon disposed) contexts - its page header differs from the original turn's.
            var requestChat = new ChatContext { SessionId = "session-1", PagePath = "/somewhere-else", IsTestChat = false };
            var requestTenant = new TenantContext { TenantName = "TestTenant", TenantGuid = "tenant-guid" };
            var requestUser = new UserContext { Guid = "user-1", Email = "person@example.com" };
            var requestSharable = new AgentSharableContext { ShareId = "share-1" };

            ToolApprovalRecord WithConversation(ToolApprovalRecord r)
            {
                r.UserId = "user-1";
                r.ShareId = "share-1";
                r.Chat = new ToolApprovalChatState { PagePath = "/home", FeatureId = "12", FeatureType = "Text", IsTestChat = true, RootIsWidget = true };
                return r;
            }
            var queue = new BackgroundToolApprovalResumeQueue(provider.GetRequiredService<IServiceScopeFactory>(), WkfloMapperExtensions.CreateMapper(),
                requestTenant, requestChat, requestUser, requestSharable, NullLogger<BackgroundToolApprovalResumeQueue>.Instance);

            // Decide through the real processor so the whole decision -> resume wiring is covered.
            var state = new Mock<IToolApprovalStateService>();
            state.Setup(x => x.GetAsync("approval-1", It.IsAny<CancellationToken>())).ReturnsAsync(WithConversation(ToolApprovalHistoryTests.Record()));
            state.Setup(x => x.TryDecideAsync("approval-1", "AllowOnce", Now, It.IsAny<CancellationToken>()))
                .ReturnsAsync((ToolApprovalDecisionOutcome.Decided, WithConversation(ToolApprovalHistoryTests.Record("Approved", "AllowOnce"))));
            var processor = new ToolApprovalDecisionProcessor(state.Object, Mock.Of<IToolApprovalNotifier>(), queue, requestTenant, requestUser,
                Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now));

            var result = await processor.DecideAsync(new DecideTenantToolApprovalCommand { Id = 5, ApprovalId = "approval-1", SessionId = "session-1", Decision = "AllowOnce" },
                5, TestContext.Current.CancellationToken);

            // The request scope ends: mutating its contexts must not affect the running resume.
            requestChat.SessionId = "changed";
            requestTenant.TenantName = "changed";
            await queue.LastResume;

            Assert.Equal(ResultType.Ok, result.ResultType);
            Assert.Equal("approval-1", sentCommand.ApprovalId);
            Assert.Equal("session-1", sentCommand.SessionId);
            Assert.Equal("session-1", seenChat.SessionId);
            Assert.Equal("/home", seenChat.PagePath);           // from the record, not the decision request
            Assert.Equal("12", seenChat.FeatureId);
            Assert.True(seenChat.IsTestChat);
            Assert.Equal("TestTenant", seenTenant.TenantName);
            Assert.Equal("tenant-guid", seenTenant.TenantGuid);
            Assert.Equal("user-1", seenUser.Guid);
            Assert.Equal("person@example.com", seenUser.Email); // caller credentials/claims kept
            Assert.Equal("share-1", seenSharable.ShareId);
        }

        [Fact]
        public async Task BackgroundResume_Failure_IsContained()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IWkfloMapper>(WkfloMapperExtensions.CreateMapper());
            services.AddScoped<TenantContext>();
            services.AddScoped<ChatContext>();
            services.AddScoped<UserContext>();
            services.AddScoped<AgentSharableContext>();
            services.AddScoped(_ =>
            {
                var mediator = new Mock<IWkfloMediator>();
                mediator.Setup(m => m.Send(It.IsAny<IWkfloRequest<Result>>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
                return mediator.Object;
            });
            var provider = services.BuildServiceProvider();
            var queue = new BackgroundToolApprovalResumeQueue(provider.GetRequiredService<IServiceScopeFactory>(), WkfloMapperExtensions.CreateMapper(),
                new TenantContext(), new ChatContext(), new UserContext(), new AgentSharableContext(), NullLogger<BackgroundToolApprovalResumeQueue>.Instance);

            await queue.EnqueueAsync(new ResumeToolApprovalCommand { ApprovalId = "approval-1", SessionId = "session-1" }, ToolApprovalHistoryTests.Record("Approved", "AllowOnce"), TestContext.Current.CancellationToken);

            await queue.LastResume; // does not throw
        }

        [Fact]
        public async Task ResumeAfterToolApproval_RunsAResumeTurnForTheSameChat_AndStreamsTheAnswerThere()
        {
            var record = ToolApprovalHistoryTests.Record("Approved", "AllowOnce");
            var tenant = new Tenant { Id = 1, Name = "TestTenant" };
            var agent = new TenantAgent
            {
                Id = record.AgentId,
                Name = "Support Agent",
                Tenant = tenant,
                IsActive = true,
                AIModel = new AIModel { Id = 3, Provider = "openai", Model = "gpt-4o", Name = "model", Secret = new Secret { ApiKey = "test-key" } },
                AgentTools = new List<AgentTool>()
            };

            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Agents).Returns(new List<Agent> { agent }.BuildMockDbSet().Object);
            db.Setup(x => x.EntityTags).Returns(new List<EntityTag>().BuildMockDbSet().Object);
            db.Setup(x => x.AgentChatSettings).Returns(new List<AgentChatSetting>().BuildMockDbSet().Object);

            RunTextOptions seenOptions = null;
            var semanticKernel = new Mock<ISemanticKernelAgentService>();
            semanticKernel.Setup(x => x.RunTextAsync(It.IsAny<RunTextOptions>(), It.IsAny<CancellationToken>()))
                .Callback<RunTextOptions, CancellationToken>((options, _) => seenOptions = options)
                .ReturnsAsync(new AgentAnswer { MessageId = "answer-1", Message = "Document 42 has been deleted.", TokenUsage = new AITokenUsage() });
            var notifications = new Mock<IChatNotificationService>();

            var service = new RunnableAISyncService(db.Object, WkfloMapperExtensions.CreateMapper(), semanticKernel.Object, notifications.Object,
                Mock.Of<IDateTimeProvider>(p => p.DateTimeOffsetUtcNow() == Now), new TenantContext { TenantName = "TestTenant" });

            var answer = await service.ResumeAfterToolApprovalAsync(record, TestContext.Current.CancellationToken);

            Assert.Equal("Document 42 has been deleted.", answer);
            Assert.Same(record, seenOptions.ToolApprovalResume);
            Assert.True(seenOptions.ApprovalsAvailable);
            Assert.Equal("session-1", seenOptions.SessionId);
            Assert.Equal(record.AgentId, seenOptions.Agent.Id);
            notifications.Verify(x => x.SendTypingIndicator("session-1", true, record.AgentId, "Support Agent", It.IsAny<string>()), Times.Once);
            notifications.Verify(x => x.SendMessageToChat("session-1", It.Is<ChatMessage>(m => m.Content == "Document 42 has been deleted."), record.AgentId, "Support Agent", It.IsAny<string>()), Times.Once);
            notifications.Verify(x => x.SendConversationStateUpdate("session-1", "idle", null), Times.Once);
        }

        [Fact]
        public async Task ResumeAfterToolApproval_AgentOfAnotherTenant_DoesNotRun()
        {
            var record = ToolApprovalHistoryTests.Record("Approved", "AllowOnce");
            var agent = new TenantAgent { Id = record.AgentId, Name = "Other", Tenant = new Tenant { Name = "OtherTenant" }, AgentTools = new List<AgentTool>() };
            var db = new Mock<IMasterDataDbContext>();
            db.Setup(x => x.Agents).Returns(new List<Agent> { agent }.BuildMockDbSet().Object);
            var semanticKernel = new Mock<ISemanticKernelAgentService>();
            var service = new RunnableAISyncService(db.Object, WkfloMapperExtensions.CreateMapper(), semanticKernel.Object, Mock.Of<IChatNotificationService>(),
                Mock.Of<IDateTimeProvider>(), new TenantContext());

            Assert.Null(await service.ResumeAfterToolApprovalAsync(record, TestContext.Current.CancellationToken));
            semanticKernel.Verify(x => x.RunTextAsync(It.IsAny<RunTextOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
