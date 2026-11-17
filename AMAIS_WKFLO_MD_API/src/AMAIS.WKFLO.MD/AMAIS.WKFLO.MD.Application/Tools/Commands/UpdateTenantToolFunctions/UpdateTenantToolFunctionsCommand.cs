using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateTenantToolFunctions
{
    /// <summary>
    /// WI 7447: replaces the MCP function allowlist of a tenant MCP tool. Saved on the tenant tool only; the
    /// published copy picks it up when the tool (or an agent using it) is published.
    /// </summary>
    public class UpdateTenantToolFunctionsCommand : IWkfloRequest<Result>, IRequestId<int>
    {
        /// <summary>Set from the route.</summary>
        [JsonIgnore]
        public int Id { get; set; }

        /// <summary>Every function that must be enabled; anything not listed is disabled. Empty = none.</summary>
        [Required(ErrorMessage = ErrorConst.Required)]
        public List<string> EnabledFunctions { get; set; } = [];

        /// <summary>
        /// WI 7726 (optional): friendly display values per function. Null = leave every display value
        /// unchanged (callers that only send <see cref="EnabledFunctions"/> keep working). Each listed entry
        /// replaces both display values of that function (null/blank clears them); functions not listed keep
        /// theirs. Every name must be in the tool's latest snapshot.
        /// </summary>
        public List<UpdateTenantToolFunctionDisplayInput> Functions { get; set; }
    }

    /// <summary>WI 7726: display-only values of one MCP function. <see cref="Name"/> is the function identifier and is never changed.</summary>
    public class UpdateTenantToolFunctionDisplayInput
    {
        public string Name { get; set; }

        /// <summary>Trimmed; blank = not set. Max 100 characters.</summary>
        public string DisplayName { get; set; }

        /// <summary>Trimmed; blank = not set. Max 500 characters.</summary>
        public string DisplayDescription { get; set; }
    }
}
