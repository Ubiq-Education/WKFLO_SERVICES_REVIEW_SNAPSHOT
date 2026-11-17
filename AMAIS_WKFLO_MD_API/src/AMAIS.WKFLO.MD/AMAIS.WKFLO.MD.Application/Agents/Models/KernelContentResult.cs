using AMAIS.WKFLO.Common.Mappings;
using Mapster;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using System.Text.Json;

namespace AMAIS.WKFLO.MD.Application.Agents.Models
{
    public class KernelContentResult : IMap
    {
        public string Name { get; set; }
        public string Data { get; set; }
        public string Status { get; set; }
        public string StatusCode { get; set; }

        //         public void Mapping(Profile profile)
        //         {
        // 
        // #pragma warning disable SKEXP0001
        // #pragma warning disable SKEXP0110
        // 
        //             profile.CreateMap<KernelContent, KernelContentResult>()
        //                 .Include<TextContent, TextContentResult>()
        //                 .Include<ImageContent, ImageContentResult>()
        //                 .Include<FunctionCallContent, FunctionCallContentResult>()
        //                 .Include<FunctionResultContent, FunctionResultContentResult>()
        //                 .Include<BinaryContent, BinaryContentResult>()
        //                 .Include<AudioContent, AudioContentResult>()
        //                 .Include<ChatMessageContent, ChatMessageContentResult>()
        //                 .Include<ActionContent, ActionContentResult>()
        //                 .Include<ReasoningContent, ReasoningContentResult>()
        //                 .Include<AnnotationContent, AnnotationContentResult>()
        //                 .Include<FileReferenceContent, FileReferenceContentResult>()
        //             .ForMember(dest => dest.Name, source => source.MapFrom(m => m.ToString()))
        //             .ForMember(dest => dest.Status, source => source.MapFrom(m => "success"))
        //             .ForMember(dest => dest.StatusCode, source => source.MapFrom(m => "200"));
        // 
        // 
        //             profile.CreateMap<TextContent, TextContentResult>()
        //                 .ForMember(dest => dest.Name, source => source.MapFrom(m => "Text Content"))
        //                 .ForMember(dest => dest.Data, source => source.MapFrom(m => m.Text))
        //                 .ForMember(dest => dest.Status, source => source.MapFrom(m => "success"))
        //                 .ForMember(dest => dest.StatusCode, source => source.MapFrom(m => "200"));
        // 
        //             profile.CreateMap<ImageContent, ImageContentResult>()
        //                 .ForMember(dest => dest.Name, source => source.MapFrom(m => "Image Content"))
        //                 .ForMember(dest => dest.Data, source => source.MapFrom(m => m.DataUri))
        //                 .ForMember(dest => dest.Status, source => source.MapFrom(m => "success"))
        //                 .ForMember(dest => dest.StatusCode, source => source.MapFrom(m => "200"));
        // 
        //             profile.CreateMap<FunctionCallContent, FunctionCallContentResult>()
        //                 .ForMember(dest => dest.Name, source => source.MapFrom(m => "Function Call Content"))
        //                 .ForMember(dest => dest.Data, source => source.MapFrom(m => m.PluginName + " " + m.FunctionName))
        //                 .ForMember(dest => dest.Status, source => source.MapFrom(m => m.Exception == null ? "success" : "failed"))
        //                 .ForMember(dest => dest.StatusCode, source => source.MapFrom(m => m.Exception == null ? "200" : "500"));
        // 
        //             profile.CreateMap<FunctionResultContent, FunctionResultContentResult>()
        //                 .ForMember(dest => dest.Name, source => source.MapFrom(m => "Function Result Content"))
        //                 .ForMember(dest => dest.Data, source => source.MapFrom(m => m.PluginName + " " + m.FunctionName))
        //                 .ForMember(dest => dest.Status, source => source.MapFrom(m => ((JsonElement)m.Result).GetRawText().Contains("Function failed.") ? "failed" : "success"))
        //                 .ForMember(dest => dest.StatusCode, source => source.MapFrom(m => ((JsonElement)m.Result).GetRawText().Contains("Function failed.") ? "500" : "200"));
        // 
        //             profile.CreateMap<BinaryContent, BinaryContentResult>()
        //                 .ForMember(dest => dest.Name, source => source.MapFrom(m => "Binary Content"))
        //                 .ForMember(dest => dest.Data, source => source.MapFrom(m => m.DataUri))
        //                 .ForMember(dest => dest.Status, source => source.MapFrom(m => "success"))
        //                 .ForMember(dest => dest.StatusCode, source => source.MapFrom(m => "200"));
        // 
        //             profile.CreateMap<AudioContent, AudioContentResult>()
        //                 .ForMember(dest => dest.Name, source => source.MapFrom(m => "Audio Content"))
        //                 .ForMember(dest => dest.Data, source => source.MapFrom(m => m.DataUri))
        //                 .ForMember(dest => dest.Status, source => source.MapFrom(m => "success"))
        //                 .ForMember(dest => dest.StatusCode, source => source.MapFrom(m => "200"));
        // 
        //             profile.CreateMap<ChatMessageContent, ChatMessageContentResult>()
        //                 .ForMember(dest => dest.Name, source => source.MapFrom(m => "Chat Message Content"))
        //                 .ForMember(dest => dest.Data, source => source.MapFrom(m => m.Content))
        //                 .ForMember(dest => dest.Status, source => source.MapFrom(m => "success"))
        //                 .ForMember(dest => dest.StatusCode, source => source.MapFrom(m => "200"));
        // 
        //             profile.CreateMap<ActionContent, ActionContentResult>()
        //                 .ForMember(dest => dest.Name, source => source.MapFrom(m => "Action Content"))
        //                 .ForMember(dest => dest.Data, source => source.MapFrom(m => m.Text))
        //                 .ForMember(dest => dest.Status, source => source.MapFrom(m => "success"))
        //                 .ForMember(dest => dest.StatusCode, source => source.MapFrom(m => "200"));
        // 
        //             profile.CreateMap<ReasoningContent, ReasoningContentResult>()
        //                 .ForMember(dest => dest.Name, source => source.MapFrom(m => "Reasoning Content"))
        //                 .ForMember(dest => dest.Data, source => source.MapFrom(m => m.Text))
        //                 .ForMember(dest => dest.Status, source => source.MapFrom(m => "success"))
        //                 .ForMember(dest => dest.StatusCode, source => source.MapFrom(m => "200"));
        // 
        //             profile.CreateMap<AnnotationContent, AnnotationContentResult>()
        //                 .ForMember(dest => dest.Name, source => source.MapFrom(m => "Annotation Content"))
        //                 .ForMember(dest => dest.Data, source => source.MapFrom(m => m.Label))
        //                 .ForMember(dest => dest.Status, source => source.MapFrom(m => "success"))
        //                 .ForMember(dest => dest.StatusCode, source => source.MapFrom(m => "200"));
        // 
        //             profile.CreateMap<FileReferenceContent, FileReferenceContentResult>()
        //                 .ForMember(dest => dest.Name, source => source.MapFrom(m => "File Reference Content"))
        //                 .ForMember(dest => dest.Data, source => source.MapFrom(m => m.FileId))
        //                 .ForMember(dest => dest.Status, source => source.MapFrom(m => "success"))
        //                 .ForMember(dest => dest.StatusCode, source => source.MapFrom(m => "200"));
        // 
        // #pragma warning restore SKEXP0110
        // #pragma warning restore SKEXP0001
        // 
        //         }

        public void Mapping(TypeAdapterConfig config)
        {
#pragma warning disable SKEXP0001
#pragma warning disable SKEXP0110

            config.NewConfig<KernelContent, KernelContentResult>()
                .Include<TextContent, TextContentResult>()
                .Include<ImageContent, ImageContentResult>()
                .Include<FunctionCallContent, FunctionCallContentResult>()
                .Include<FunctionResultContent, FunctionResultContentResult>()
                .Include<BinaryContent, BinaryContentResult>()
                .Include<AudioContent, AudioContentResult>()
                .Include<ChatMessageContent, ChatMessageContentResult>()
                .Include<ActionContent, ActionContentResult>()
                .Include<ReasoningContent, ReasoningContentResult>()
                .Include<AnnotationContent, AnnotationContentResult>()
                .Include<FileReferenceContent, FileReferenceContentResult>()
                .Map(dest => dest.Name, src => src.ToString())
                .Map(dest => dest.Status, src => "success")
                .Map(dest => dest.StatusCode, src => "200");

            config.NewConfig<TextContent, TextContentResult>()
                .Map(dest => dest.Name, src => "Text Content")
                .Map(dest => dest.Data, src => src.Text)
                .Map(dest => dest.Status, src => "success")
                .Map(dest => dest.StatusCode, src => "200");

            config.NewConfig<ImageContent, ImageContentResult>()
                .Map(dest => dest.Name, src => "Image Content")
                .Map(dest => dest.Data, src => src.DataUri)
                .Map(dest => dest.Status, src => "success")
                .Map(dest => dest.StatusCode, src => "200");

            config.NewConfig<FunctionCallContent, FunctionCallContentResult>()
                .Map(dest => dest.Name, src => "Function Call Content")
                .Map(dest => dest.Data, src => src.PluginName + " " + src.FunctionName)
                .Map(dest => dest.Status, src => src.Exception == null ? "success" : "failed")
                .Map(dest => dest.StatusCode, src => src.Exception == null ? "200" : "500");

            config.NewConfig<FunctionResultContent, FunctionResultContentResult>()
                .Map(dest => dest.Name, src => "Function Result Content")
                .Map(dest => dest.Data, src => src.PluginName + " " + src.FunctionName)
                .Map(dest => dest.Status, src => ((JsonElement)src.Result).GetRawText().Contains("Function failed.") ? "failed" : "success")
                .Map(dest => dest.StatusCode, src => ((JsonElement)src.Result).GetRawText().Contains("Function failed.") ? "500" : "200");

            config.NewConfig<BinaryContent, BinaryContentResult>()
                .Map(dest => dest.Name, src => "Binary Content")
                .Map(dest => dest.Data, src => src.DataUri)
                .Map(dest => dest.Status, src => "success")
                .Map(dest => dest.StatusCode, src => "200");

            config.NewConfig<AudioContent, AudioContentResult>()
                .Map(dest => dest.Name, src => "Audio Content")
                .Map(dest => dest.Data, src => src.DataUri)
                .Map(dest => dest.Status, src => "success")
                .Map(dest => dest.StatusCode, src => "200");

            config.NewConfig<ChatMessageContent, ChatMessageContentResult>()
                .Map(dest => dest.Name, src => "Chat Message Content")
                .Map(dest => dest.Data, src => src.Content)
                .Map(dest => dest.Status, src => "success")
                .Map(dest => dest.StatusCode, src => "200");

            config.NewConfig<ActionContent, ActionContentResult>()
                .Map(dest => dest.Name, src => "Action Content")
                .Map(dest => dest.Data, src => src.Text)
                .Map(dest => dest.Status, src => "success")
                .Map(dest => dest.StatusCode, src => "200");

            config.NewConfig<ReasoningContent, ReasoningContentResult>()
                .Map(dest => dest.Name, src => "Reasoning Content")
                .Map(dest => dest.Data, src => src.Text)
                .Map(dest => dest.Status, src => "success")
                .Map(dest => dest.StatusCode, src => "200");

            config.NewConfig<AnnotationContent, AnnotationContentResult>()
                .Map(dest => dest.Name, src => "Annotation Content")
                .Map(dest => dest.Data, src => src.Label)
                .Map(dest => dest.Status, src => "success")
                .Map(dest => dest.StatusCode, src => "200");

            config.NewConfig<FileReferenceContent, FileReferenceContentResult>()
                .Map(dest => dest.Name, src => "File Reference Content")
                .Map(dest => dest.Data, src => src.FileId)
                .Map(dest => dest.Status, src => "success")
                .Map(dest => dest.StatusCode, src => "200");

#pragma warning restore SKEXP0110
#pragma warning restore SKEXP0001
        }
    }

    public class TextContentResult : KernelContentResult
    {

    }

    public class ImageContentResult : KernelContentResult
    {

    }

    public class FunctionCallContentResult : KernelContentResult
    {

    }

    public class FunctionResultContentResult : KernelContentResult
    {

    }

    public class BinaryContentResult : KernelContentResult
    {

    }

    public class AudioContentResult : KernelContentResult
    {

    }

    public class ChatMessageContentResult : KernelContentResult
    {

    }

    public class ActionContentResult : KernelContentResult
    {

    }

    public class ReasoningContentResult : KernelContentResult
    {

    }

    public class AnnotationContentResult : KernelContentResult
    {

    }

    public class FileReferenceContentResult : KernelContentResult
    {

    }
}
