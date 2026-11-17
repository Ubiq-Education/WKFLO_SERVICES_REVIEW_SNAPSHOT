using AMAIS.WKFLO.Common.AI.Application.Helpers;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System;
using System.Collections.Generic;
using static AMAIS.WKFLO.Common.Models.CommonConsts;

namespace AMAIS.WKFLO.Worker.UnitTests.Jobs.TopicClassification
{
    /// <summary>
    /// Regression coverage for a live-testing find (US 7142), root-caused by downloading and reading the real
    /// persisted chat-history blob for a ContactForm-gated conversation: a genuine transcript is NOT just
    /// [Question, Filler, RealAnswer] — Semantic Kernel's own function-calling loop inserts an Assistant
    /// message carrying ONLY the <c>FunctionCallContent</c> for the ContactForm tool call, whose
    /// <c>Content</c> (text) is blank, positioned BEFORE the filler text reply and BEFORE the real answer. That
    /// blank-content Assistant message is never tagged <see cref="CommonMetadataConsts.IsContactFormFiller"/>
    /// (only the later, actual filler TEXT reply gets that tag) — so <c>ExtractQuestionAndAnswer</c> matched it
    /// as "the answer" before this fix, reporting an empty (thus omitted per <c>DxpChatbotAnalyticsService</c>'s
    /// AddIfPresent convention) Response for a message that DOES have a real, later, non-blank answer.
    /// </summary>
    public class ContactFormReplayChatHistoryExtractionTests
    {
        [Fact]
        public void ExtractQuestionAndAnswer_SkipsBlankFunctionCallAssistantMessage_FindsRealAnswerAfterFiller()
        {
            var questionId = Guid.NewGuid();
            var fillerId = Guid.NewGuid();
            var answerId = Guid.NewGuid();

            var history = new ChatHistory();
            history.Add(new ChatMessageContent(AuthorRole.User, "tell me about the school soccer team",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = questionId.ToString() }));

            // SK's own function-calling step: an Assistant message with BLANK text, carrying only the
            // FunctionCallContent for the ContactForm tool - never tagged IsContactFormFiller, since only the
            // later, actual text reply gets that tag.
            history.Add(new ChatMessageContent(AuthorRole.Assistant, string.Empty));

            // The tool's own result message.
            history.Add(new ChatMessageContent(AuthorRole.Tool,
                "A registration form has been displayed to the user. Registration is optional to continue."));

            // The actual filler TEXT reply, tagged as filler.
            history.Add(new ChatMessageContent(AuthorRole.Assistant,
                "I can provide more detailed information about the school soccer team if you fill out a contact form.",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = fillerId.ToString(), [CommonMetadataConsts.IsContactFormFiller] = true }));

            // The real final answer.
            history.Add(new ChatMessageContent(AuthorRole.Assistant, "I'm sorry, but I don't have specific details about the school soccer team.",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = answerId.ToString() }));

            var (question, answer, answerMessageId) = ChatHistoryMessageExtractor.ExtractQuestionAndAnswer(history, questionId);

            Assert.Equal("tell me about the school soccer team", question);
            Assert.Equal("I'm sorry, but I don't have specific details about the school soccer team.", answer);
            Assert.Equal(answerId, answerMessageId);
        }

        [Fact]
        public void ExtractQuestionAndAnswer_OnlyBlankFunctionCallMessageExists_ReturnsNullAnswer_NotEmptyString()
        {
            var questionId = Guid.NewGuid();

            var history = new ChatHistory();
            history.Add(new ChatMessageContent(AuthorRole.User, "tell me about the school soccer team",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = questionId.ToString() }));
            history.Add(new ChatMessageContent(AuthorRole.Assistant, string.Empty));

            var (question, answer, answerMessageId) = ChatHistoryMessageExtractor.ExtractQuestionAndAnswer(history, questionId);

            Assert.Equal("tell me about the school soccer team", question);
            Assert.Null(answer);
            Assert.Null(answerMessageId);
        }

        [Fact]
        public void ExtractQuestionAndAnswer_DeadTurnFollowedByALaterQuestion_DoesNotStealTheLaterQuestionsAnswer()
        {
            // A turn that never produced a real answer (e.g. a blank/failed assistant reply) must not have its
            // scan for "the answer" cross into a LATER, unrelated question's own answer - the search is bounded
            // at the next User message.
            var firstQuestionId = Guid.NewGuid();
            var secondQuestionId = Guid.NewGuid();
            var secondAnswerId = Guid.NewGuid();

            var history = new ChatHistory();
            history.Add(new ChatMessageContent(AuthorRole.User, "first question",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = firstQuestionId.ToString() }));
            // The first turn's own assistant reply is blank - no real answer for this question.
            history.Add(new ChatMessageContent(AuthorRole.Assistant, string.Empty));

            history.Add(new ChatMessageContent(AuthorRole.User, "second question",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = secondQuestionId.ToString() }));
            history.Add(new ChatMessageContent(AuthorRole.Assistant, "answer to the second question",
                metadata: new Dictionary<string, object> { [CommonMetadataConsts.MessageId] = secondAnswerId.ToString() }));

            var (question, answer, answerMessageId) = ChatHistoryMessageExtractor.ExtractQuestionAndAnswer(history, firstQuestionId);

            Assert.Equal("first question", question);
            Assert.Null(answer);
            Assert.Null(answerMessageId);
        }
    }
}
