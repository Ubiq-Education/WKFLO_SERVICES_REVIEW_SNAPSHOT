using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.Models.Entities;
using Mapster;

namespace AMAIS.WKFLO.Common.MD.Domain.Entities
{
    public class Tool : Entity<int>, IMap
    {
        public string ToolType { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Type { get; set; }
        public string Config { get; set; }
        public virtual ICollection<AgentTool> AgentTools { get; set; }

        public int TenantId { get; set; }
        public Tenant Tenant { get; set; }

        /// <summary>
        /// FK to the <see cref="MarketplaceTool"/> this row was installed from via
        /// <c>InstallMarketplaceToolCommandHandler</c>. Declared on the shared TPH base (mirrors
        /// <see cref="AMAIS.WKFLO.Common.MD.Domain.Entities.Agent.InstalledFromMarketplaceAgentId"/>
        /// exactly, same reasoning): the underlying [Tool].[InstalledFromMarketplaceToolId] column
        /// is physically shared by every subclass's rows, and <c>PublishTenantToolCommandHandler</c>'s
        /// Mapster copy needs this same-named scalar property on <see cref="PublishedTenantTool"/>
        /// too so it travels with the published snapshot automatically. Used to dedupe installs per
        /// tenant (at most one non-deleted <see cref="TenantTool"/> per (Tenant, MarketplaceTool)
        /// pair) and to compute <see cref="MarketplaceTool"/>'s live installation count. Different
        /// relationship from <see cref="PublishedTenantTool.MarketplaceToolId"/> (the SHARE
        /// direction). Null for a tool not created via marketplace install.
        /// </summary>
        public int? InstalledFromMarketplaceToolId { get; set; }
        public MarketplaceTool InstalledFromMarketplaceTool { get; set; }

        /// <summary>
        /// WI 7575: whether this tool takes part in the agent tool permission system (Allow / Ask / Block per
        /// agent, see <see cref="AgentTool.ToolPermission"/> and <see cref="AgentTool.FunctionPermissions"/>).
        /// When false the agent's saved permissions are ignored and the tool behaves as Allow. Opt-in: false for
        /// every tool unless a WKFLO admin switches it on.
        /// Declared on the TPH base so Mapster copies it on publish/share/install like every other column.
        /// </summary>
        public bool PermissionsEnabled { get; set; }

        /// <summary>
        /// WI 7447 (MCP only): JSON snapshot of the MCP server's <c>tools/list</c> taken by the discover
        /// endpoint - see <see cref="Models.Tools.ToolPermissions.McpFunctionDescriptor"/>. Kept out of
        /// <see cref="Config"/> on purpose: Config is saved whole by clients and would overwrite it.
        /// </summary>
        public string DiscoveredFunctions { get; set; }

        /// <summary>WI 7447 (MCP only): when <see cref="DiscoveredFunctions"/> was last refreshed.</summary>
        public DateTimeOffset? FunctionsDiscoveredOn { get; set; }

        /// <summary>
        /// WI 7447 (MCP only): JSON string array of the MCP function names an admin enabled. Bug 7856: it only applies
        /// while <see cref="PermissionsEnabled"/> is true (otherwise every function is allowed and this value is kept
        /// untouched); null or an empty array means no allowlist is configured, so every function is allowed. Read it through
        /// <see cref="Models.Tools.ToolPermissions.McpFunctionAllowlist"/> so every consumer applies the same rule.
        /// </summary>
        public string EnabledFunctions { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             // Mapping from TenantTool to PublishedTenantTool
        //             profile.CreateMap<TenantTool, PublishedTenantTool>()
        //                 .ForMember(dest => dest.Id, opt => opt.Ignore()) // Will be auto-generated
        //                 .ForMember(dest => dest.ToolType, opt => opt.MapFrom(src => "PublishedTenantTool"))
        //                 .ForMember(dest => dest.CreatedByTenantToolId, opt => opt.Ignore()) // Will be set manually
        //                 .ForMember(dest => dest.CreatedByTenantTool, opt => opt.Ignore()) // Will be handled separately
        //                 .ForMember(dest => dest.CreatedOn, opt => opt.Ignore()) // Will be set by EF Core
        //                 .ForMember(dest => dest.LastUpdatedOn, opt => opt.Ignore()) // Will be set by EF Core
        //                 .ForMember(dest => dest.AgentTools, opt => opt.Ignore()); // Will be handled separately
        // 
        //             // Mapping from PublishedTenantTool to MarketplaceTool
        //             profile.CreateMap<PublishedTenantTool, MarketplaceTool>()
        //                 .ForMember(dest => dest.Id, opt => opt.Ignore()) // Will be auto-generated
        //                 .ForMember(dest => dest.ToolType, opt => opt.MapFrom(src => "MarketplaceTool"))
        //                 .ForMember(dest => dest.TenantId, opt => opt.MapFrom(src => src.TenantId)) // Track which tenant shared this tool
        //                 .ForMember(dest => dest.Tenant, opt => opt.MapFrom(src => src.Tenant))
        //                 .ForMember(dest => dest.SourcePublishedToolId, opt => opt.Ignore()) // Will be set manually
        //                 .ForMember(dest => dest.CreatedOn, opt => opt.Ignore()) // Will be set by EF Core
        //                 .ForMember(dest => dest.LastUpdatedOn, opt => opt.Ignore()) // Will be set by EF Core
        //                 .ForMember(dest => dest.AgentTools, opt => opt.Ignore()); // Will be handled separately
        // 
        //             // Add this mapping to the Tool.cs class in the Mapping method.
        //             profile.CreateMap<MarketplaceTool, TenantTool>()
        //                 .ForMember(dest => dest.Id, opt => opt.Ignore()) // Will be auto-generated
        //                 .ForMember(dest => dest.ToolType, opt => opt.MapFrom(src => "TenantTool"))
        //                 .ForMember(dest => dest.TenantId, opt => opt.Ignore()) // Will be set manually
        //                 .ForMember(dest => dest.Tenant, opt => opt.Ignore()) // Will be set manually
        //                 .ForMember(dest => dest.IsActive, opt => opt.Ignore()) // Will be set manually
        //                 .ForMember(dest => dest.PublishedTenantToolId, opt => opt.Ignore()) // Not needed for marketplace installs
        //                 .ForMember(dest => dest.PublishedTenantTool, opt => opt.Ignore()) // Not needed for marketplace installs
        //                 .ForMember(dest => dest.AgentTools, opt => opt.Ignore()) // Will be handled separately if needed
        //                 .ForMember(dest => dest.CreatedOn, opt => opt.Ignore()) // Will be set by EF Core
        //                 .ForMember(dest => dest.LastUpdatedOn, opt => opt.Ignore()); // Will be set by EF Core
        //         }

        // Entity → entity mappings: ignore ALL navigation properties.
        // EF Core resolves navs from FK scalars (TenantId, …) — copying nav objects
        // creates new CLR instances with the same PKs that conflict with already-tracked
        // instances and cause "another instance with the same key" errors.
        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<TenantTool, PublishedTenantTool>()
                .Map(dest => dest.ToolType, src => "PublishedTenantTool")
                .Ignore(dest => dest.Id)
                .Ignore(dest => dest.CreatedByTenantToolId)
                .Ignore(dest => dest.CreatedByTenantTool)
                .Ignore(dest => dest.CreatedOn)
                .Ignore(dest => dest.LastUpdatedOn)
                .Ignore(dest => dest.AgentTools)
                .Ignore(dest => dest.Tenant)
                // InstalledFromMarketplaceToolId (scalar) is intentionally NOT ignored — it must
                // flow through to the published snapshot. Only the nav object is ignored.
                .Ignore(dest => dest.InstalledFromMarketplaceTool);

            config.NewConfig<PublishedTenantTool, MarketplaceTool>()
                .Map(dest => dest.ToolType, src => "MarketplaceTool")
                .Ignore(dest => dest.Id)
                .Ignore(dest => dest.SourcePublishedToolId)
                .Ignore(dest => dest.CreatedOn)
                .Ignore(dest => dest.LastUpdatedOn)
                .Ignore(dest => dest.AgentTools)
                .Ignore(dest => dest.Tenant)
                .Ignore(dest => dest.InstalledFromMarketplaceToolId)
                .Ignore(dest => dest.InstalledFromMarketplaceTool);

            config.NewConfig<MarketplaceTool, TenantTool>()
                .Map(dest => dest.ToolType, src => "TenantTool")
                .Ignore(dest => dest.Id)
                .Ignore(dest => dest.TenantId)
                .Ignore(dest => dest.Tenant)
                .Ignore(dest => dest.IsActive)
                .Ignore(dest => dest.PublishedTenantToolId)
                .Ignore(dest => dest.PublishedTenantTool)
                .Ignore(dest => dest.AgentTools)
                .Ignore(dest => dest.CreatedOn)
                .Ignore(dest => dest.LastUpdatedOn)
                .Ignore(dest => dest.InstalledFromMarketplaceToolId)
                .Ignore(dest => dest.InstalledFromMarketplaceTool);
        }
    }

    public class MarketplaceTool : Tool
    {
        public int? SourcePublishedToolId { get; set; }
        public PublishedTenantTool SourcePublishedTool { get; set; }
    }

    public abstract class RunableTool : Tool
    {
        public bool IsActive { get; set; }

        /// <summary>
        /// Identity of this tool's MCP OAuth2 connection (see <see cref="ToolOAuthToken.McpOAuthConnectionId"/>).
        /// Null until the tenant first connects. Generated once by <c>StartToolMcpOAuthCommandHandler</c> for a
        /// <see cref="TenantTool"/> and copied verbatim onto its <see cref="PublishedTenantTool"/> on publish
        /// (plain Mapster convention mapping — see <see cref="Tool.Mapping"/>), so a tenant tool and its
        /// published copy share exactly one token row, one DCR client, one redirect_uri instead of each having
        /// an independent connection. Never present on <see cref="MarketplaceTool"/> (not a <see cref="RunableTool"/>) —
        /// installing from the marketplace always starts a brand-new connection.
        /// </summary>
        public Guid? McpOAuthConnectionId { get; set; }

        /// <summary>
        /// Max character length of this tool's output before <c>SemanticKernelLargeToolOutputFilter</c>
        /// truncates it prior to re-entering the model's context. Null (the default) means no cap is applied
        /// for this tool — opt-in per tool, not a global default, since most tools (e.g. Humantic) never
        /// return anything large enough to need one.
        /// </summary>
        public int? MaxToolOutputLength { get; set; }
    }

    public class TenantTool : RunableTool
    {
        public int? PublishedTenantToolId { get; set; }
        public PublishedTenantTool PublishedTenantTool { get; set; }
    }

    public class PublishedTenantTool : RunableTool
    {
        public int? CreatedByTenantToolId { get; set; }
        public TenantTool CreatedByTenantTool { get; set; }

        public int? MarketplaceToolId { get; set; }
        public MarketplaceTool MarketplaceTool { get; set; }
    }
}
