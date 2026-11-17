using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using Mapster;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.UpdateAgentChatSettingShareable
{
    public class UpdateAgentChatSettingShareableCommand : IWkfloRequest<Result>, IRequestId<int>, IMap
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        [Range(1, int.MaxValue, ErrorMessage = ErrorConst.MustBePositive)]
        public int Id { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public bool IsWidget { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        public bool IsShareable { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<UpdateAgentChatSettingShareableCommand, AgentChatSetting>()
        //                 .ForMember(dest => dest.Id, opt => opt.Ignore())
        //                 .ForMember(dest => dest.AgentId, opt => opt.Ignore())
        //                 .ForMember(dest => dest.Agent, opt => opt.Ignore())
        //                 .ForMember(dest => dest.CreatedOn, opt => opt.Ignore());
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<UpdateAgentChatSettingShareableCommand, AgentChatSetting>()
                .Ignore(dest => dest.Id)
                .Ignore(dest => dest.AgentId)
                .Ignore(dest => dest.Agent)
                .Ignore(dest => dest.CreatedOn);
        }
    }
}

