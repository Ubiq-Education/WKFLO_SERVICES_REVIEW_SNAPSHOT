namespace AMAIS.WKFLO.Common.Models.AI.Providers
{
    public class OpenAIProvider : AIBaseProvider
    {
        public string ApiKey { get; set; }
        public string Model { get; set; }
    }
}
