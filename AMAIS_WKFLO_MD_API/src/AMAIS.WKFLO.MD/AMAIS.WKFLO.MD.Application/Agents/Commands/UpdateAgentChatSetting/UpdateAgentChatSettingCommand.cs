using AMAIS.WKFLO.Common.Interfaces.Commands;
using AMAIS.WKFLO.Common.Mappings;
using AMAIS.WKFLO.Common.MD.Domain.Entities;
using AMAIS.WKFLO.Common.Mediator;
using AMAIS.WKFLO.Common.Models.Results;
using AMAIS.WKFLO.MD.Application.Common;
using Mapster;
using System.ComponentModel.DataAnnotations;

namespace AMAIS.WKFLO.MD.Application.Agents.Commands.UpdateAgentChatSetting
{
    public class UpdateAgentChatSettingCommand : IWkfloRequest<Result>, IRequestId<int>, IMap
    {
        [Required(ErrorMessage = ErrorConst.Required)]
        [Range(1, int.MaxValue, ErrorMessage = ErrorConst.MustBePositive)]
        public int Id { get; set; }

        [StringLength(200, ErrorMessage = ErrorConst.MaximumLength)]
        public string HeaderTitle { get; set; }

        [StringLength(50, ErrorMessage = ErrorConst.MaximumLength)]
        public string ChatWidth { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        [StringLength(7, ErrorMessage = ErrorConst.MaximumLength)]
        public string PrimaryColor { get; set; }

        [StringLength(7, ErrorMessage = ErrorConst.MaximumLength)]
        public string ForegroundColor { get; set; }

        [StringLength(50, ErrorMessage = ErrorConst.MaximumLength)]
        public string BubbleStyle { get; set; }

        [StringLength(50, ErrorMessage = ErrorConst.MaximumLength)]
        public string FloatingPosition { get; set; }

        [StringLength(50, ErrorMessage = ErrorConst.MaximumLength)]
        public string LauncherStyle { get; set; }

        [StringLength(500, ErrorMessage = ErrorConst.MaximumLength)]
        public string LauncherText { get; set; }

        [StringLength(500, ErrorMessage = ErrorConst.MaximumLength)]
        public string BubbleIconPath { get; set; }

        [StringLength(500, ErrorMessage = ErrorConst.MaximumLength)]
        public string ChatbotIconPath { get; set; }

        [StringLength(500, ErrorMessage = ErrorConst.MaximumLength)]
        public string CssUrl { get; set; }

        [StringLength(500, ErrorMessage = ErrorConst.MaximumLength)]
        public string WelcomeMessage { get; set; }

        [StringLength(4000, ErrorMessage = ErrorConst.MaximumLength)]
        public string FooterMessage { get; set; }

        public IList<Models.PredefinedPromptModel> PredefinedPrompts { get; set; }

        // Nullable so the handler can distinguish "caller omitted the field" (null, apply the
        // business default, or keep the stored value on update) from "caller explicitly sent
        // false" — see AgentChatSettingDefaults.
        public bool? ShowDescription { get; set; }

        public bool ChainOfThought { get; set; }

        public bool IsWidget { get; set; }

        [Required(ErrorMessage = ErrorConst.Required)]
        [StringLength(500, ErrorMessage = ErrorConst.MaximumLength)]
        public string ApiEndpoint { get; set; }

        public string SecurityConfiguration { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        //             profile.CreateMap<UpdateAgentChatSettingCommand, AgentChatSetting>()
        //                 .ForMember(dest => dest.Id, opt => opt.Ignore())
        //                 .ForMember(dest => dest.AgentId, opt => opt.Ignore())
        //                 .ForMember(dest => dest.Agent, opt => opt.Ignore())
        //                 .ForMember(dest => dest.CreatedOn, opt => opt.Ignore())
        //                 .ForMember(dest => dest.PredefinedPrompts, opt => opt.Ignore());
        // 
        //             profile.CreateMap<AgentChatSetting, UpdateAgentChatSettingCommand>()
        //                 .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.AgentId));
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
            config.NewConfig<UpdateAgentChatSettingCommand, AgentChatSetting>()
                .Ignore(dest => dest.Id)
                .Ignore(dest => dest.AgentId)
                .Ignore(dest => dest.Agent)
                .Ignore(dest => dest.CreatedOn)
                .Ignore(dest => dest.PredefinedPrompts);

            config.NewConfig<AgentChatSetting, UpdateAgentChatSettingCommand>()
                .Map(dest => dest.Id, src => src.AgentId);
        }
    }
}

