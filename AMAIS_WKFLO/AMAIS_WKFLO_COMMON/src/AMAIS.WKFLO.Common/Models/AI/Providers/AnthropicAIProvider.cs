using AMAIS.WKFLO.Common.Helpers;

namespace AMAIS.WKFLO.Common.Models.AI.Providers
{
    public class AnthropicAIProvider : AIBaseProvider
    {
        public string ApiKey { get; set; }
        public string Model { get; set; }

        public int MaxOutputTokens => AnthropicModelHelper.GetMaxOutputTokens(Model);
    }
}
