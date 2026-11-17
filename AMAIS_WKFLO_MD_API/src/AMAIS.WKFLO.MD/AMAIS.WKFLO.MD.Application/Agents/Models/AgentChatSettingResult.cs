using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Mapster;
using Newtonsoft.Json;

namespace AMAIS.WKFLO.MD.Application.Agents.Models
{
    public class AgentChatSettingResult : IMap
    {
        public string TenantGuid { get; set; }
        public string HeaderTitle { get; set; }
        public string ChatWidth { get; set; }
        public string PrimaryColor { get; set; }
        public string ForegroundColor { get; set; }
        public string BubbleStyle { get; set; }
        public string FloatingPosition { get; set; }
        public string LauncherStyle { get; set; }
        public string LauncherText { get; set; }
        public string BubbleIconPath { get; set; }
        public string ChatbotIconPath { get; set; }
        public string CssUrl { get; set; }
        public string WelcomeMessage { get; set; }
        public string FooterMessage { get; set; }
        public IList<PredefinedPromptModel> PredefinedPrompts { get; set; }
        public bool ShowDescription { get; set; }
        public bool ChainOfThought { get; set; }
        public bool IsVoiceChatEnabled { get; set; }
        public bool CanSelectFeature { get; set; }
        /// <summary>
        /// True when the agent is tagged "Maisy". The explicit flag the chat UI gates MAISY-only behaviour on
        /// (conversations list, new chat, top bar). <see cref="CanSelectFeature"/> carries the same value today
        /// but describes one feature (the feature-selection button), so it must not be reused as the Maisy check.
        /// </summary>
        public bool IsMaisyAgent { get; set; }
        public bool IsWidget { get; set; }
        public string ApiEndpoint { get; set; }
        public bool IsShareable { get; set; }
        public string ShareId { get; set; }
        public int AgentId { get; set; }
        public AgentSecurityConfiguration SecurityConfiguration { get; set; }

        // Additional properties for agent information
        public string AgentName { get; set; }
        public string AgentDescription { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<AgentChatSetting, AgentChatSettingResult>()
        //                 .ForMember(dest => dest.TenantGuid, opt => opt.MapFrom(src => src.Agent.Tenant.Guid))
        //                 .ForMember(dest => dest.AgentName, opt => opt.MapFrom(src => src.Agent.Name))
        //                 .ForMember(dest => dest.AgentDescription, opt => opt.MapFrom(src => src.Agent.Description))
        //                 .ForMember(dest => dest.SecurityConfiguration, opt => opt.MapFrom(src =>
        //                     string.IsNullOrEmpty(src.SecurityConfiguration)
        //                         ? new AgentSecurityConfiguration { Type = "none" }
        //                         : JsonConvert.DeserializeObject<AgentSecurityConfiguration>(src.SecurityConfiguration)))
        //                 .ReverseMap()
        //                 .ForMember(dest => dest.SecurityConfiguration, opt => opt.MapFrom(src =>
        //                     JsonConvert.SerializeObject(src.SecurityConfiguration)));
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<AgentChatSetting, AgentChatSettingResult>()
                .Map(dest => dest.TenantGuid, src => src.Agent != null && src.Agent.Tenant != null ? src.Agent.Tenant.Guid.ToString() : null)
                .Map(dest => dest.AgentName, src => src.Agent != null ? src.Agent.Name : null)
                .Map(dest => dest.AgentDescription, src => src.Agent != null ? src.Agent.Description : null)
                .Map(dest => dest.SecurityConfiguration, src =>
                    string.IsNullOrEmpty(src.SecurityConfiguration)
                        ? new AgentSecurityConfiguration { Type = "none" }
                        : JsonConvert.DeserializeObject<AgentSecurityConfiguration>(src.SecurityConfiguration));

            config.NewConfig<AgentChatSettingResult, AgentChatSetting>()
                .Map(dest => dest.SecurityConfiguration, src =>
                    JsonConvert.SerializeObject(src.SecurityConfiguration));
        }
    }

    public class AgentSecurityConfiguration
    {
        public string Type { get; set; }
        public AzureAdConfiguration AzureAd { get; set; }
        public ApiKeyConfiguration ApiKey { get; set; }
        public JwtConfiguration Jwt { get; set; }
    }

    public class AzureAdConfiguration
    {
        public string ClientId { get; set; }
        public string TenantId { get; set; }
        public string[] Scopes { get; set; }
    }

    public class ApiKeyConfiguration
    {
        public string Value { get; set; }
        public string TenantName { get; set; }
        public string[] TenantDomains { get; set; }
    }

    public class JwtConfiguration
    {
        public string[] Domains { get; set; }
    }
}


