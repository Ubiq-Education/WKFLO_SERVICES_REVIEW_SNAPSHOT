namespace AMAIS.WKFLO.Common.ApiClient.Providers.Interfaces
{
    public interface IClientApiCacheProvider
    {
        void SetCacheEntry(string key, string value, long expireTime);

        string TryGetEntryValue(string key);
    }
}
