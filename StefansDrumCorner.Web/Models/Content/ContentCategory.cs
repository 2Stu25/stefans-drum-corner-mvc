
namespace StefansDrumCorner.Web.Models.Content
{
    public class ContentCategory
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Subtitle { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public string Slug { get; set; } = string.Empty;

        public int? ParentCategoryId { get; set; }

        public ContentCategory? ParentCategory { get; set; }

        public ICollection<ContentCategory> SubCategories { get; set; }
            = new List<ContentCategory>();

        public int DisplayOrder { get; set; }

        public bool IsActive { get; set; } = true;

        // Optional image used to represent this category.
        public int? ImageId { get; set; }

        public MediaAsset? Image { get; set; }
    }
}
