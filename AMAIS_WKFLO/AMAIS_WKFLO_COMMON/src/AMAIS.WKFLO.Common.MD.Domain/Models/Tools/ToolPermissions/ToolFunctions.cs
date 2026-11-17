using AMAIS.WKFLO.Common.MD.Domain.Entities;
using Newtonsoft.Json.Linq;

namespace AMAIS.WKFLO.Common.MD.Domain.Models.Tools.ToolPermissions
{
    /// <summary>WI 7447: status of one MCP function in the admin allowlist view.</summary>
    public static class ToolFunctionStatus
    {
        /// <summary>In the latest snapshot and already reviewed (enabled or deliberately left disabled).</summary>
        public const string Active = "Active";

        /// <summary>In the latest snapshot but nobody has saved the allowlist since it appeared.</summary>
        public const string New = "New";

        /// <summary>Enabled, but missing from the latest snapshot (the server no longer offers it).</summary>
        public const string Removed = "Removed";
    }

    /// <summary>WI 7447: response of GET (MD) / discover (AI) <c>tenant/tool/{id}/functions</c>.</summary>
    public class ToolFunctionsResult
    {
        public List<ToolFunctionResult> Functions { get; set; } = [];
        public DateTimeOffset? FunctionsDiscoveredOn { get; set; }
    }

    public class ToolFunctionResult
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public JToken InputSchema { get; set; }
        public bool IsEnabled { get; set; }

        /// <summary>One of <see cref="ToolFunctionStatus"/>.</summary>
        public string Status { get; set; }

        /// <summary>WI 7726: admin-chosen friendly name, or null (fall back to <see cref="Name"/>). Always null for Removed functions.</summary>
        public string DisplayName { get; set; }

        /// <summary>WI 7726: admin-chosen friendly description, or null (fall back to <see cref="Description"/>). Always null for Removed functions.</summary>
        public string DisplayDescription { get; set; }
    }

    /// <summary>
    /// WI 7447: pure helpers behind the MCP function allowlist. Shared by the MD API (GET/PUT functions) and the
    /// AI API (discover) so both return the exact same shape.
    /// </summary>
    public static class ToolFunctionsBuilder
    {
        public static ToolFunctionsResult Build(Tool tool)
        {
            var snapshot = ToolPermissionJson.ParseDiscoveredFunctions(tool?.DiscoveredFunctions);
            var enabled = ToolPermissionJson.ParseEnabledFunctions(tool?.EnabledFunctions);
            var snapshotNames = new HashSet<string>(snapshot.Select(f => f.Name), StringComparer.Ordinal);

            var functions = snapshot
                .Select(f => new ToolFunctionResult
                {
                    Name = f.Name,
                    Description = f.Description,
                    InputSchema = f.InputSchema,
                    IsEnabled = enabled.Contains(f.Name),
                    Status = f.IsNew ? ToolFunctionStatus.New : ToolFunctionStatus.Active,
                    DisplayName = f.DisplayName,
                    DisplayDescription = f.DisplayDescription
                })
                .Concat(enabled
                    .Where(name => !snapshotNames.Contains(name))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .Select(name => new ToolFunctionResult
                    {
                        Name = name,
                        IsEnabled = true,
                        Status = ToolFunctionStatus.Removed
                    }))
                .ToList();

            return new ToolFunctionsResult { Functions = functions, FunctionsDiscoveredOn = tool?.FunctionsDiscoveredOn };
        }

        /// <summary>
        /// Builds the new snapshot from a fresh <c>tools/list</c> result. A function keeps its previous
        /// <see cref="McpFunctionDescriptor.IsNew"/> flag when it was already in the previous snapshot;
        /// a function seen for the first time is New - except when it is already enabled (an admin decided it).
        /// WI 7726: the admin-chosen display name/description are carried over by name too (the server's own
        /// name/description always come from the fresh result); functions gone upstream drop them.
        /// </summary>
        public static List<McpFunctionDescriptor> MergeSnapshot(IEnumerable<McpFunctionDescriptor> previous, IEnumerable<McpFunctionDescriptor> fresh, ISet<string> enabled)
        {
            var previousByName = (previous ?? [])
                .GroupBy(f => f.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            return (fresh ?? [])
                .Where(f => !string.IsNullOrWhiteSpace(f?.Name))
                .GroupBy(f => f.Name, StringComparer.Ordinal)
                .Select(g => g.First())
                .Select(f => new McpFunctionDescriptor
                {
                    Name = f.Name,
                    Description = f.Description,
                    InputSchema = f.InputSchema,
                    IsNew = previousByName.TryGetValue(f.Name, out var known)
                        ? known.IsNew
                        : enabled == null || !enabled.Contains(f.Name),
                    DisplayName = known?.DisplayName,
                    DisplayDescription = known?.DisplayDescription
                })
                .ToList();
        }

        /// <summary>Saving the allowlist means the admin reviewed every listed function: none is New any more.</summary>
        public static List<McpFunctionDescriptor> MarkAllReviewed(IEnumerable<McpFunctionDescriptor> snapshot) =>
            (snapshot ?? []).Select(f => new McpFunctionDescriptor
            {
                Name = f.Name,
                Description = f.Description,
                InputSchema = f.InputSchema,
                IsNew = false,
                DisplayName = f.DisplayName,
                DisplayDescription = f.DisplayDescription
            }).ToList();

        /// <summary>
        /// Names that may be saved as enabled: anything in the latest snapshot, plus anything already enabled
        /// (so a Removed function can stay enabled until the admin turns it off).
        /// </summary>
        public static HashSet<string> GetEnableableNames(Tool tool)
        {
            var names = new HashSet<string>(ToolPermissionJson.ParseDiscoveredFunctions(tool?.DiscoveredFunctions).Select(f => f.Name), StringComparer.Ordinal);
            names.UnionWith(ToolPermissionJson.ParseEnabledFunctions(tool?.EnabledFunctions));
            return names;
        }
    }
}
