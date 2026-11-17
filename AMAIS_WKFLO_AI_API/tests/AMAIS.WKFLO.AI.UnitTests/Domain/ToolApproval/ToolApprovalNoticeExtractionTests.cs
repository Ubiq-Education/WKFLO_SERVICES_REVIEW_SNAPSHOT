using AMAIS.WKFLO.Common.AI.Application.Helpers;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System;
using System.Collections.Generic;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.AI.UnitTests.Domain.ToolApproval
{
    /// <summary>WI 7575: the "needs approval" notice is never taken as a question's real answer.</summary>
    public class ToolApprovalNoticeExtractionTests
    {
        [Fact]
        public void ExtractQuestionAndAnswer_SkipsApprovalNotice_FindsResumedAnswer()
        {
            var questionId = Guid.NewGuid();
            var answerId = Guid.NewGuid();
            var history = new ChatHistory();
            history.Add(new ChatMessageContent(AuthorRole.User, "Delete document 42", metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = questionId.ToString() }));
            history.Add(new ChatMessageContent(AuthorRole.Assistant, "I need your permission to use delete from Docs before I can continue.",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.IsToolApprovalNotice] = true, [CommonMetadataConsts.MessageId] = Guid.NewGuid().ToString() }));
            history.Add(new ChatMessageContent(AuthorRole.Assistant, "Document 42 has been deleted.",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = answerId.ToString() }));

            var (question, answer, answerMessageId) = ChatHistoryMessageExtractor.ExtractQuestionAndAnswer(history, questionId);

            Assert.Equal("Delete document 42", question);
            Assert.Equal("Document 42 has been deleted.", answer);
            Assert.Equal(answerId, answerMessageId);
        }
    }
}
