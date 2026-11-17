namespace AMAIS.WKFLO.Common.ApiClient.Client.Models
{
    public class OAuthBehaviourSettings
    {
        public bool CacheEnable { get; set; }
        public int RetrieveTokenBeforeExpiration { get; set; }
        public int SlidingExpirationTime { get; set; }
    }
}
