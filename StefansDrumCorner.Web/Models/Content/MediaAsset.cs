
namespace StefansDrumCorner.Web.Models.Content
{
    public enum MediaType
    {
        Video,
        Image,
        Document,
        Audio,
        Other
    }

    public class MediaAsset
    {
        public int Id { get; set; }

        public string OriginalFileName { get; set; } = string.Empty;

        public string StorageKey { get; set; } = string.Empty;

        public string StorageProvider { get; set; } = "Local";

        public string ContentType { get; set; } = string.Empty;

        public long FileSizeBytes { get; set; }

        public MediaType MediaType { get; set; }

        public string? AltText { get; set; }

        public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
