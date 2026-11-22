
using Microsoft.EntityFrameworkCore;
using StefansDrumCorner.Web.Models.Content;

namespace StefansDrumCorner.Web.Data
{
    public static class PracticeLabCategorySeeder
    {
        public static async Task SeedAsync(IServiceProvider services)
        {
            await using var scope = services.CreateAsyncScope();

            var context = scope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>();

            var practiceLab = await context.ContentCategories
                .FirstOrDefaultAsync(c => c.Slug == "practice-lab");

            if (practiceLab == null)
            {
                throw new InvalidOperationException(
                    "The Practice Lab parent category was not found.");
            }

            var standardCategories = new[]
            {
                new
                {
                    Name = "Practice Pad",
                    Slug = "practice-pad",
                    Subtitle = "Pad Technique & Development",
                    Order = 1
                },
                new
                {
                    Name = "Grip & Sticking",
                    Slug = "grip-sticking",
                    Subtitle = "Hand Technique & Control",
                    Order = 2
                },
                new
                {
                    Name = "Weighted Training",
                    Slug = "weighted-training",
                    Subtitle = "Strength & Endurance",
                    Order = 3
                },
                new
                {
                    Name = "Exercises & Studies",
                    Slug = "exercises-studies",
                    Subtitle = "Rudiments, Exercises & Studies",
                    Order = 4
                }
            };

            foreach (var standard in standardCategories)
            {
                var existing = await context.ContentCategories
                    .FirstOrDefaultAsync(c => c.Slug == standard.Slug);

                if (existing != null)
                {
                    if (existing.ParentCategoryId != practiceLab.Id)
                    {
                        throw new InvalidOperationException(
                            $"Category '{standard.Slug}' already exists " +
                            "under a different parent category.");
                    }

                    if (!existing.IsActive)
                    {
                        existing.IsActive = true;
                    }

                    continue;
                }

                context.ContentCategories.Add(new ContentCategory
                {
                    Name = standard.Name,
                    Slug = standard.Slug,
                    Subtitle = standard.Subtitle,
                    ParentCategoryId = practiceLab.Id,
                    DisplayOrder = standard.Order,
                    IsActive = true
                });
            }

            await context.SaveChangesAsync();
        }
    }
}
