using AMAIS.WKFLO.Common.Helpers;
using AMAIS.WKFLO.Common.Interfaces.Providers;
using Azure.Core;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Logging;

namespace AMAIS.WKFLO.Common.Infrastructure.Providers
{
    public class AzureSpeechProvider : IAzureSpeechProvider
    {
        private readonly ILogger<AzureSpeechProvider> _logger;

        public AzureSpeechProvider(ILogger<AzureSpeechProvider> logger)
        {
            _logger = logger;
        }

        public async Task<byte[]> SynthesizeSpeechAsync(string text, string voiceName, string langCode, CancellationToken cancellationToken = default)
        {
            var endpoint = Environment.GetEnvironmentVariable("AZURE_SPEECH_ENDPOINT")
                ?? throw new InvalidOperationException("AZURE_SPEECH_ENDPOINT environment variable is not set.");

            var credential = AzureCredentialHelper.Create();
            var tokenResult = await credential.GetTokenAsync(
                new TokenRequestContext(new[] { "https://cognitiveservices.azure.com/.default" }),
                cancellationToken);

            var speechConfig = SpeechConfig.FromEndpoint(new Uri(endpoint));
            speechConfig.AuthorizationToken = tokenResult.Token;
            speechConfig.SetSpeechSynthesisOutputFormat(SpeechSynthesisOutputFormat.Audio16Khz32KBitRateMonoMp3);

            var ssml = BuildSsml(text, voiceName, langCode);

            using var audioConfig = AudioConfig.FromStreamOutput(AudioOutputStream.CreatePullStream());
            using var synthesizer = new SpeechSynthesizer(speechConfig, audioConfig);

            using var result = await synthesizer.SpeakSsmlAsync(ssml);

            if (result.Reason == ResultReason.SynthesizingAudioCompleted)
            {
                return result.AudioData;
            }

            if (result.Reason == ResultReason.Canceled)
            {
                var cancellation = SpeechSynthesisCancellationDetails.FromResult(result);
                _logger.LogError("Speech synthesis canceled. Reason: {Reason}. ErrorDetails: {ErrorDetails}",
                    cancellation.Reason, cancellation.ErrorDetails);
                throw new InvalidOperationException($"Speech synthesis failed: {cancellation.ErrorDetails}");
            }

            throw new InvalidOperationException($"Speech synthesis failed with reason: {result.Reason}");
        }

        private static string BuildSsml(string text, string voiceName, string langCode)
        {
            var escapedText = System.Security.SecurityElement.Escape(text);
            return $"""
                <speak version='1.0' xml:lang='{langCode}'>
                    <voice xml:lang='{langCode}' name='{voiceName}'>
                        <prosody rate='1.0' pitch='-2%'>{escapedText}</prosody>
                    </voice>
                </speak>
                """;
        }
    }
}
