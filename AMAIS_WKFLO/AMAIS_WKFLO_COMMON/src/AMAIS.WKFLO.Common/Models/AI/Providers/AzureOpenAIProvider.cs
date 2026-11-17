namespace AMAIS.WKFLO.Common.Models.AI.Providers
{
    public class AzureOpenAIProvider : AIBaseProvider
    {
        public string ApiKey { get; set; }
        public string Model { get; set; }
        public string DeploymentName { get; set; }
        public string Endpoint { get; set; }
        public string ApiVersion { get; set; }
        public bool UseManagedIdentity { get; set; }

        /// <summary>
        /// True when <see cref="Model"/> is a Claude model deployed on Azure via Microsoft
        /// Foundry rather than an actual Azure OpenAI Service deployment. The two are
        /// different resource kinds with incompatible wire protocols, so this flag decides
        /// which SDK/connector AIProviderHelper.GetKernel uses.
        /// </summary>
        public bool IsAnthropicModel { get; set; }
    }
}
