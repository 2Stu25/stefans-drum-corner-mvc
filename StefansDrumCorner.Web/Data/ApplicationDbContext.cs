
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using StefansDrumCorner.Web.Models.Content;

namespace StefansDrumCorner.Web.Data
{
    public class ApplicationDbContext : IdentityDbContext<IdentityUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // Content Manager tables
        public DbSet<ContentItem> ContentItems { get; set; }

        public DbSet<ContentCategory> ContentCategories { get; set; }

        public DbSet<MediaAsset> MediaAssets { get; set; }

        public DbSet<ContentMedia> ContentMedia { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // ContentItem -> ContentCategory
            builder.Entity<ContentItem>()
                .HasOne(c => c.Category)
                .WithMany()
                .HasForeignKey(c => c.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);

            // ContentCategory -> ParentCategory
            builder.Entity<ContentCategory>()
                .HasOne(c => c.ParentCategory)
                .WithMany(c => c.SubCategories)
                .HasForeignKey(c => c.ParentCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<ContentCategory>()
                .HasOne(c => c.Image)
                .WithMany()
                .HasForeignKey(c => c.ImageId)
                .OnDelete(DeleteBehavior.SetNull);

            // ContentMedia -> ContentItem
            builder.Entity<ContentMedia>()
                .HasOne(cm => cm.ContentItem)
                .WithMany(c => c.ContentMedia)
                .HasForeignKey(cm => cm.ContentItemId)
                .OnDelete(DeleteBehavior.Cascade);

            // ContentMedia -> MediaAsset
            builder.Entity<ContentMedia>()
                .HasOne(cm => cm.MediaAsset)
                .WithMany()
                .HasForeignKey(cm => cm.MediaAssetId)
                .OnDelete(DeleteBehavior.Restrict);


            // Unique slug indexes
            builder.Entity<ContentItem>()
                .HasIndex(c => c.Slug)
                .IsUnique();

            builder.Entity<ContentCategory>()
                .HasIndex(c => c.Slug)
                .IsUnique();

        }
    }
}
