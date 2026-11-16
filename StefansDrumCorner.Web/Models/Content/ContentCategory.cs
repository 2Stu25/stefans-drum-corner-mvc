
namespace StefansDrumCorner.Web.Models.Content
{
    public class ContentCategory
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Slug { get; set; } = string.Empty;

        public string Section { get; set; } = string.Empty;

        public int DisplayOrder { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
