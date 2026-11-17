namespace AMAIS.WKFLO.Common.Interfaces.Services
{
    public interface IAmaisTokenApiService
    {
        Task<string> GetVoiceTokenAsync();
        Task<string> GetAmaisIntegrationTokenAsync();
    }
}
