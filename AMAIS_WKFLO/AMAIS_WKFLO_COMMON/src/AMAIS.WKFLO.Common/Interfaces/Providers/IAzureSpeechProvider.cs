namespace AMAIS.WKFLO.Common.Interfaces.Providers
{
    public interface IAzureSpeechProvider
    {
        Task<byte[]> SynthesizeSpeechAsync(string text, string voiceName, string langCode, CancellationToken cancellationToken = default);
    }
}
