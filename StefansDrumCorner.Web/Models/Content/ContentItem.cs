
namespace StefansDrumCorner.Web.Models.Content
{
    public enum ContentType
    {
        Video,
        Article,
        Gallery,
        Resource
    }

    public enum ContentStatus
    {
        Draft,
        Published,
        Archived
    }

    public class ContentItem
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Slug { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

       

        public int? CategoryId { get; set; }

        public ContentCategory? Category { get; set; }

        public ContentType ContentType { get; set; }

        public ContentStatus Status { get; set; } = ContentStatus.Draft;

        public int DisplayOrder { get; set; }

        public bool IsFeatured { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAtUtc { get; set; }

        public DateTime? PublishedAtUtc { get; set; }
    }
}
