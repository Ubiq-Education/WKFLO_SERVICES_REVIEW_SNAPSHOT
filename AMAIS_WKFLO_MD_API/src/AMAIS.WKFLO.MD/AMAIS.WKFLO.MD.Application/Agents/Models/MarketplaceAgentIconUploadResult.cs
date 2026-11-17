namespace AMAIS.WKFLO.MD.Application.Agents.Models
{
    /// <summary>
    /// Result of uploading a Marketplace agent icon image. Populated directly in the handler from
    /// IImageStorageService's result — there is no domain entity to map from since the endpoint does
    /// not persist anything onto MarketplaceAgent.IconUrl; the caller (wkflo_ui) is responsible for
    /// saving the returned AbsoluteUrl via MarketplaceAgentUpdate's own Save flow, same separation of
    /// concerns as AgentChatIconUploadResult/UploadAgentChatIconCommandHandler.
    /// </summary>
    public class MarketplaceAgentIconUploadResult
    {
        public string RelativePath { get; set; }
        public string AbsoluteUrl { get; set; }
    }
}
