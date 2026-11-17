using System.ComponentModel;

namespace AMAIS.WKFLO.Common.AI.Application.Plugins.Amais.Models
{
    public class GalleryImage
    {
        [Description("New image file name for the gallery to be updated.")] public string FileName { get; set; }
        [Description("New image file mime type for the gallery to be updated.")] public string FileMimeType { get; set; }
    }
}
