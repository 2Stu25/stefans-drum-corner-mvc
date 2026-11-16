
namespace StefansDrumCorner.Web.Models.Content
{
    public enum MediaRole
    {
        MainVideo,
        Thumbnail,
        Document,
        GalleryImage,
        Audio,
        Other
    }

    public class ContentMedia
    {
        public int Id { get; set; }

        public int ContentItemId { get; set; }

        public ContentItem ContentItem { get; set; } = null!;

        public int MediaAssetId { get; set; }

        public MediaAsset MediaAsset { get; set; } = null!;

        public MediaRole Role { get; set; }

        public int DisplayOrder { get; set; }

        public string? Caption { get; set; }
    }
}
