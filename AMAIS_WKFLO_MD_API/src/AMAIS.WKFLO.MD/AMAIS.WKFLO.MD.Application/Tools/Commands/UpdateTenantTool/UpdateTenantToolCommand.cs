using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using Mapster;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Tools.Commands.UpdateTenantTool
{
    public class UpdateTenantToolCommand : IWkfloRequest<Result>, IRequestId<int>, IMap
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        public int Id { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        [MaxLength(300, ErrorMessage = ErrorConst.MaximumLength)]
        public string Name { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public string Description { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        [MaxLength(100, ErrorMessage = ErrorConst.MaximumLength)]
        public string Type { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public string Config { get; set; }

        public bool? IsActive { get; set; }

        /// <summary>Max output chars before SemanticKernelLargeToolOutputFilter truncates this tool's
        /// results. In a PUT body, an explicit <c>"maxToolOutputLength": null</c> clears the cap (no
        /// truncation), while omitting the field keeps the stored cap (partial updates such as the tool card's
        /// enable/disable toggle send only <c>isActive</c>). <see cref="MaxToolOutputLengthSpecified"/> tells
        /// the two apart.</summary>
        public int? MaxToolOutputLength
        {
            get => _maxToolOutputLength;
            set
            {
                _maxToolOutputLength = value;
                MaxToolOutputLengthSpecified = true;
            }
        }

        private int? _maxToolOutputLength;

        /// <summary>
        /// Bug 7871: true once <see cref="MaxToolOutputLength"/> has been assigned, null included. Newtonsoft
        /// (the MD API's input formatter) calls that setter only when the request body contains
        /// <c>maxToolOutputLength</c>, so the PUT endpoint can keep an explicit null that its null-skipping merge
        /// would otherwise drop, while an omitted field leaves this false. Newtonsoft also reads this
        /// <c>{Property}Specified</c> flag (found by name, despite JsonIgnore) to decide whether to write the
        /// property, so setting it from the value setter keeps typed callers' assignments in the serialized JSON.
        /// The JsonIgnore attributes keep the flag itself out of the wire format and the OpenAPI schema.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public bool MaxToolOutputLengthSpecified { get; private set; }

        /// <summary>
        /// WI 7575: "Manage permissions for this tool". Null leaves the current value unchanged. Reaches the
        /// published copy when the tool is published.
        /// </summary>
        public bool? PermissionsEnabled { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<UpdateTenantToolCommand, TenantTool>();
        //             profile.CreateMap<TenantTool, UpdateTenantToolCommand>();
        //             profile.CreateMap<UpdateTenantToolCommand, UpdateTenantToolCommand>()
        //                 .ForAllMembers(opts =>
        //                 {
        //                     opts.Condition((src, dest, srcMember) => srcMember != null);
        //                 });
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            // PermissionsEnabled is nullable here (null = unchanged) and applied explicitly by the handler.
            config.NewConfig<UpdateTenantToolCommand, TenantTool>()
                .Ignore(dest => dest.PermissionsEnabled);
            config.NewConfig<TenantTool, UpdateTenantToolCommand>();
            // The Specified flag describes the incoming request only (TenantToolUpdate reads it there). Mapster would
            // otherwise copy it over the merge target, so a request that omits maxToolOutputLength would reset the
            // target's flag and drop the pre-filled stored cap from the serialized command (e.g. the request log).
            config.NewConfig<UpdateTenantToolCommand, UpdateTenantToolCommand>()
                .IgnoreNullValues(true)
                .Ignore(dest => dest.MaxToolOutputLengthSpecified);
        }
    }
}

