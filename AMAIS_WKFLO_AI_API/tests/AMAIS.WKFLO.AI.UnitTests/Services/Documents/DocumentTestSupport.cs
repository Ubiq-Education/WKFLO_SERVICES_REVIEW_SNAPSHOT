using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace AMAIS.WKFLO.AI.UnitTests.Services.Documents
{
    /// <summary>
    /// A deterministic embedding generator: a text is a vector of how often each topic word occurs in it, so a question about
    /// "alpha" is closest to the chunks that talk about alpha. Counts the calls and the texts it embedded.
    /// </summary>
    public sealed class KeywordEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public static readonly string[] Topics = ["alpha", "beta", "gamma", "delta", "epsilon", "zeta"];

        private int _calls;
        public int Calls => _calls;
        public List<string> Embedded { get; } = [];

        /// <summary>Runs before each call; may delay, block until cancelled or throw.</summary>
        public Func<int, CancellationToken, Task> OnCall { get; set; }

        public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions options = null, CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _calls);
            if (OnCall != null)
                await OnCall(call, cancellationToken);

            var list = values.ToList();
            lock (Embedded)
                Embedded.AddRange(list);

            return new GeneratedEmbeddings<Embedding<float>>(list.Select(v => new Embedding<float>(Vectorize(v))));
        }

        public static float[] Vectorize(string text)
        {
            var vector = new float[Topics.Length + 1];
            for (var i = 0; i < Topics.Length; i++)
            {
                var index = 0;
                while ((index = text.IndexOf(Topics[i], index, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    vector[i]++;
                    index += Topics[i].Length;
                }
            }

            vector[^1] = 0.01f;
            return vector;
        }

        public object GetService(Type serviceType, object serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    internal static class DocumentTestSupport
    {
        public static TenantAgent Agent(int id = 42) => new() { Id = id, Name = "Kit", AIModel = new AIModel { Id = 3, Provider = "openai", Model = "gpt-4o" } };

        /// <summary>A kernel whose chat completion service streams the given reply (one chunk), carrying the given usage metadata.</summary>
        public static Kernel KernelReplying(string reply, long inputTokens = 100, long outputTokens = 20, Exception fail = null, List<string> prompts = null)
        {
            var chat = new Mock<IChatCompletionService>();
            chat.Setup(x => x.GetStreamingChatMessageContentsAsync(It.IsAny<ChatHistory>(), It.IsAny<PromptExecutionSettings>(), It.IsAny<Kernel>(), It.IsAny<CancellationToken>()))
                .Returns((ChatHistory history, PromptExecutionSettings _, Kernel _, CancellationToken _) =>
                {
                    prompts?.Add(string.Join("\n", history.Select(m => m.Content)));
                    return Stream(reply, inputTokens, outputTokens, fail);
                });

            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton(chat.Object);
            return builder.Build();
        }

        private static async IAsyncEnumerable<StreamingChatMessageContent> Stream(string reply, long inputTokens, long outputTokens, Exception fail,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (fail != null)
                throw fail;

            var metadata = new Dictionary<string, object> { ["Usage"] = new UsageDetails { InputTokenCount = inputTokens, OutputTokenCount = outputTokens } };
            yield return new StreamingChatMessageContent(AuthorRole.Assistant, reply, metadata: metadata);
        }
    }
}
