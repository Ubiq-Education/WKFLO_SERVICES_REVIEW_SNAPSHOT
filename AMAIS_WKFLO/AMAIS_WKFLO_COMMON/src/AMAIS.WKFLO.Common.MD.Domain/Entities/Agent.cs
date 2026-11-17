using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Models.Entities;
using Mapster;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    // Base agent class
    public class Agent : Entity<int>, IMap
    {
        public string AgentType { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string SystemPrompt { get; set; }
        public string ResponseTone { get; set; }
        public virtual ICollection<AgentTool> AgentTools { get; set; }
        public int? AIModelId { get; set; }
        public AIModel AIModel { get; set; }
        public int TenantId { get; set; }
        public Tenant Tenant { get; set; }
        /// <summary>
        /// Absolute URL of the agent's icon. Declared on the shared TPH base (rather than just
        /// <see cref="MarketplaceAgent"/>) because the underlying [Agent].[IconUrl] column is
        /// physically shared by every subclass's rows in the same table — see
        /// AMAIS.WKFLO.Common.MD.DB/dbo/Tables/Agent.sql. Today it is populated in two places:
        /// directly on a <see cref="MarketplaceAgent"/> via the Manage Marketplace Agents edit
        /// popup (<c>UpdateMarketplaceAgentCommand</c>), and on a <see cref="MarketplaceAgent"/>
        /// at share-time, copied from the source TenantAgent's
        /// <see cref="AgentChatSetting.ChatbotIconPath"/> (see
        /// <c>ShareTenantAgentCommandHandler</c>). Nothing currently writes it on a
        /// <see cref="TenantAgent"/> or <see cref="PublishedTenantAgent"/> row directly, but
        /// <c>ShareTenantAgentCommandHandler</c> reads <see cref="PublishedTenantAgent"/>'s value
        /// (falling back to the chat-icon lookup when unset) so any future direct-icon source on
        /// the published agent flows through automatically. Null when no icon has been set.
        /// </summary>
        public string IconUrl { get; set; }

        /// <summary>
        /// FK to the <see cref="MarketplaceAgent"/> this row was installed from via
        /// <c>InstallMarketplaceAgentCommandHandler</c>. Declared on the shared TPH base (rather
        /// than just <see cref="TenantAgent"/>) for the same reason as <see cref="IconUrl"/>: the
        /// underlying [Agent].[InstalledFromMarketplaceAgentId] column is physically shared by
        /// every subclass's rows, and <c>PublishTenantAgentCommandHandler</c>'s Mapster copy needs
        /// this same-named scalar property to exist on <see cref="PublishedTenantAgent"/> too so it
        /// travels with the published snapshot automatically (plain convention mapping, no explicit
        /// <c>.Map()</c> needed). Used to dedupe installs per tenant (at most one non-deleted
        /// <see cref="TenantAgent"/> per (Tenant, MarketplaceAgent) pair) and to compute
        /// <see cref="MarketplaceAgent"/>'s live installation count. This is a DIFFERENT
        /// relationship from <see cref="PublishedTenantAgent.MarketplaceAgentId"/> (the SHARE
        /// direction — which <see cref="PublishedTenantAgent"/> was shared to create a given
        /// <see cref="MarketplaceAgent"/>): don't confuse the two. Null for an agent that wasn't
        /// created via marketplace install (e.g. created directly, or installed before this field
        /// existed).
        /// </summary>
        public int? InstalledFromMarketplaceAgentId { get; set; }
        public MarketplaceAgent InstalledFromMarketplaceAgent { get; set; }

        /// <summary>
        /// Whether Short Term Memory is enabled for this agent row. Declared on the shared TPH
        /// base (rather than on <see cref="RunnableAgent"/> or <see cref="MarketplaceAgent"/>
        /// individually) because the underlying [Agent].[EnableSTM] column is physically shared
        /// by every subclass's rows in the same table — see
        /// AMAIS.WKFLO.Common.MD.DB/dbo/Tables/Agent.sql. A single declaring property maps to one
        /// shared physical column by EF Core's default TPH convention; two independent
        /// same-named declarations on sibling branches (as this briefly was, with
        /// <see cref="RunnableAgent"/> and <see cref="MarketplaceAgent"/> each declaring their
        /// own copy) make EF Core treat them as unrelated properties and split them into
        /// type-prefixed shadow columns (e.g. <c>TenantAgent_EnableSTM</c>) that don't exist in
        /// the real database. On a <see cref="MarketplaceAgent"/> this is only the template's
        /// default value, copied once into a newly-installed <see cref="TenantAgent"/> at install
        /// time (see <c>InstallMarketplaceAgentCommandHandler</c>) and editable via the Manage
        /// Marketplace Agents edit popup (<c>UpdateMarketplaceAgentCommand</c>). On a
        /// <see cref="RunnableAgent"/> (<see cref="TenantAgent"/>/<see cref="PublishedTenantAgent"/>)
        /// it is the live agent's own runtime STM toggle. These are different concepts that happen
        /// to share one column — harmless, since a given row is only ever one AgentType and never
        /// reads the other type's meaning for it.
        /// </summary>
        public bool EnableSTM { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<TenantAgent, PublishedTenantAgent>()
        //                 .ForMember(dest => dest.Id, opt => opt.Ignore()) // Will be auto-generated
        //                 .ForMember(dest => dest.AgentType, opt => opt.MapFrom(src => "PublishedTenantAgent"))
        //                 .ForMember(dest => dest.CreatedByTenantAgentId, opt => opt.Ignore()) // Will be set manually
        //                 .ForMember(dest => dest.CreatedByTenantAgent, opt => opt.Ignore()) // Will be handled separately
        //                 .ForMember(dest => dest.CreatedOn, opt => opt.Ignore()) // Will be set by EF Core
        //                 .ForMember(dest => dest.LastUpdatedOn, opt => opt.Ignore()) // Will be set by EF Core
        //                 .ForMember(dest => dest.AgentTools, opt => opt.Ignore()); // Will be handled separately
        // 
        //             profile.CreateMap<PublishedTenantAgent, MarketplaceAgent>()
        //                 .ForMember(dest => dest.Id, opt => opt.Ignore()) // Will be auto-generated
        //                 .ForMember(dest => dest.AgentType, opt => opt.MapFrom(src => "MarketplaceAgent"))
        //                 .ForMember(dest => dest.SourcePublishedAgentId, opt => opt.Ignore()) // Will be set manually
        //                 .ForMember(dest => dest.CreatedOn, opt => opt.Ignore()) // Will be set by EF Core
        //                 .ForMember(dest => dest.LastUpdatedOn, opt => opt.Ignore()) // Will be set by EF Core
        //                 .ForMember(dest => dest.AgentTools, opt => opt.Ignore()); // Will be handled separately
        // 
        //             profile.CreateMap<MarketplaceAgent, TenantAgent>()
        //                 .ForMember(dest => dest.Id, opt => opt.Ignore()) // Will be auto-generated
        //                 .ForMember(dest => dest.AgentType, opt => opt.MapFrom(src => "TenantAgent"))
        //                 .ForMember(dest => dest.TenantId, opt => opt.Ignore()) // Will be set manually
        //                 .ForMember(dest => dest.Tenant, opt => opt.Ignore()) // Will be set manually
        //                 .ForMember(dest => dest.IsActive, opt => opt.Ignore()) // Will be set manually
        //                 .ForMember(dest => dest.EnableSTM, opt => opt.Ignore()) // Will be set manually
        //                 .ForMember(dest => dest.EnableLTM, opt => opt.Ignore()) // Will be set manually
        //                 .ForMember(dest => dest.LastRunAt, opt => opt.Ignore()) // Will be set manually
        //                 .ForMember(dest => dest.PublishedTenantAgentId, opt => opt.Ignore())
        //                 .ForMember(dest => dest.PublishedTenantAgent, opt => opt.Ignore())
        //                 .ForMember(dest => dest.CreatedOn, opt => opt.Ignore()) // Se establecerá por EF Core
        //                 .ForMember(dest => dest.LastUpdatedOn, opt => opt.Ignore()) // Se establecerá por EF Core
        //                 .ForMember(dest => dest.AgentTools, opt => opt.Ignore()); // Se manejará por separado
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            // Entity → entity mappings: ignore ALL navigation properties.
            // EF Core resolves navs from FK scalars (AIModelId, TenantId, …) — copying nav
            // objects creates new CLR instances with the same PKs that conflict with already-
            // tracked instances and cause "another instance with the same key" errors.
            config.NewConfig<TenantAgent, PublishedTenantAgent>()
                .Map(dest => dest.AgentType, src => "PublishedTenantAgent")
                .Ignore(dest => dest.Id)
                .Ignore(dest => dest.CreatedByTenantAgentId)
                .Ignore(dest => dest.CreatedByTenantAgent)
                .Ignore(dest => dest.CreatedOn)
                .Ignore(dest => dest.LastUpdatedOn)
                .Ignore(dest => dest.AgentTools)
                .Ignore(dest => dest.AIModel)
                .Ignore(dest => dest.Tenant)
                // InstalledFromMarketplaceAgentId (scalar) is intentionally NOT ignored here — it
                // must flow through to the published snapshot (see doc comment above). Only the nav
                // object is ignored, same reasoning as every other nav property in this config.
                .Ignore(dest => dest.InstalledFromMarketplaceAgent);

            config.NewConfig<PublishedTenantAgent, MarketplaceAgent>()
                .Map(dest => dest.AgentType, src => "MarketplaceAgent")
                .Ignore(dest => dest.Id)
                .Ignore(dest => dest.SourcePublishedAgentId)
                .Ignore(dest => dest.CreatedOn)
                .Ignore(dest => dest.LastUpdatedOn)
                .Ignore(dest => dest.AgentTools)
                .Ignore(dest => dest.AIModel)
                .Ignore(dest => dest.Tenant)
                // A freshly-(re)shared MarketplaceAgent is a catalog entry, not an install record —
                // don't carry forward whatever the source PublishedTenantAgent was itself installed from.
                .Ignore(dest => dest.InstalledFromMarketplaceAgentId)
                .Ignore(dest => dest.InstalledFromMarketplaceAgent);

            config.NewConfig<MarketplaceAgent, TenantAgent>()
                .Map(dest => dest.AgentType, src => "TenantAgent")
                .Ignore(dest => dest.Id)
                .Ignore(dest => dest.TenantId)
                .Ignore(dest => dest.Tenant)
                .Ignore(dest => dest.IsActive)
                // EnableSTM is intentionally NOT ignored here (unlike EnableLTM/EnableSpinUpAgents
                // right below, which MarketplaceAgent has no equivalent of): EnableSTM is declared
                // once on the shared Agent base (see its doc comment), so MarketplaceAgent's value
                // (the template's default) is what convention mapping correctly copies into the new
                // TenantAgent's initial value. InstallMarketplaceAgentCommandHandler still assigns it
                // explicitly right after this Map() call for clarity/defense-in-depth, but that
                // explicit assignment is a no-op given this convention copy, not a fight against it.
                .Ignore(dest => dest.EnableLTM)
                .Ignore(dest => dest.EnableSpinUpAgents)
                .Ignore(dest => dest.LastRunAt)
                .Ignore(dest => dest.PublishedTenantAgentId)
                .Ignore(dest => dest.PublishedTenantAgent)
                .Ignore(dest => dest.CreatedOn)
                .Ignore(dest => dest.LastUpdatedOn)
                .Ignore(dest => dest.AgentTools)
                .Ignore(dest => dest.AIModel)
                .Ignore(dest => dest.Tenant)
                // Set explicitly by InstallMarketplaceAgentCommandHandler to the SOURCE MarketplaceAgent's
                // own id — never copied from whatever (normally null) value the source itself carries.
                .Ignore(dest => dest.InstalledFromMarketplaceAgentId)
                .Ignore(dest => dest.InstalledFromMarketplaceAgent);
        }
    }

    // Public Marketplace Agent that can be installed
    public class MarketplaceAgent : Agent
    {
        public int? SourcePublishedAgentId { get; set; }
        public PublishedTenantAgent SourcePublishedAgent { get; set; }
    }

    // Common class to mark a Agent that can be run
    public abstract class RunnableAgent : Agent
    {
        public bool IsActive { get; set; }
        public bool EnableLTM { get; set; }
        /// <summary>
        /// When true, this agent's Semantic Kernel gets the multi-agent orchestration plugins
        /// (trigger a lead/fan-out orchestration, publish a cross-agent event) attached — letting the
        /// agent's own LLM autonomously spin up other agents within the tenant (story #6480). Defaults
        /// to false/off; a tenant admin must opt an agent into this explicitly.
        /// </summary>
        public bool EnableSpinUpAgents { get; set; }
        public DateTimeOffset? LastRunAt { get; set; }
        /// <summary>
        /// Maximum number of tool calls allowed per conversation turn. Null means unlimited.
        /// </summary>
        public int? MaxToolCallsPerTurn { get; set; }
        /// <summary>
        /// Free-text instructions an administrator adds on top of the platform-owned
        /// <see cref="Agent.SystemPrompt"/> (WI 7318). The admin never sees or edits SystemPrompt;
        /// this text is appended to it at runtime by <c>SemanticKernelExtensions.GetWkflowPromptAsync</c>
        /// as plain text (no tool-token parsing). Max 2000 characters. Declared on
        /// <see cref="RunnableAgent"/> (not the <see cref="Agent"/> base) on purpose: the
        /// TenantAgent → PublishedTenantAgent Mapster convention copies it on publish, while
        /// <see cref="MarketplaceAgent"/> has no such property, so sharing to / installing from the
        /// marketplace can never carry one tenant's text to another tenant. Null when not set.
        /// </summary>
        public string AdditionalInstructions { get; set; }

        /// <summary>Max length of <see cref="AdditionalInstructions"/> — mirrors [Agent].[AdditionalInstructions] NVARCHAR(2000).</summary>
        public const int AdditionalInstructionsMaxLength = 2000;

        /// <summary>
        /// Canonical stored form of <see cref="AdditionalInstructions"/>: trimmed, with blank/whitespace-only
        /// input collapsed to null so "cleared" is always represented the same way.
        /// </summary>
        public static string NormalizeAdditionalInstructions(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    // Edit mode tenant agent for testing
    public class TenantAgent : RunnableAgent
    {
        public int? PublishedTenantAgentId { get; set; }
        public PublishedTenantAgent PublishedTenantAgent { get; set; }
    }

    // Tenant agent to be used publicly on chatbots or workflows. After publishing a TenantAgent, a PublishedTenantAgent is created
    public class PublishedTenantAgent : RunnableAgent
    {
        public int? CreatedByTenantAgentId { get; set; }
        public TenantAgent CreatedByTenantAgent { get; set; }

        public int? MarketplaceAgentId { get; set; }
        public MarketplaceAgent MarketplaceAgent { get; set; }
    }
}
