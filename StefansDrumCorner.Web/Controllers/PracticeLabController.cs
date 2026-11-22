
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StefansDrumCorner.Web.Data;
using StefansDrumCorner.Web.Models.Content;

namespace StefansDrumCorner.Web.Controllers
{
    public class PracticeLabController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PracticeLabController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var category = await _context.ContentCategories
                .Include(c => c.SubCategories
                    .Where(s => s.IsActive))
                .FirstOrDefaultAsync(c =>
                    c.Slug == "practice-lab" &&
                    c.IsActive);

            if (category == null)
            {
                return NotFound();
            }

            var categoryIds = category.SubCategories
                .Select(s => s.Id)
                .Append(category.Id)
                .ToList();

            var contentItems = await _context.ContentItems
                .Where(item =>
                    item.CategoryId.HasValue &&
                    categoryIds.Contains(item.CategoryId.Value) &&
                    item.Status == ContentStatus.Published)
                .Include(item => item.ContentMedia)
                    .ThenInclude(media => media.MediaAsset)
                .OrderBy(item => item.DisplayOrder)
                .ThenBy(item => item.Title)
                .ToListAsync();

            ViewBag.Category = category;
            ViewBag.SubCategories = category.SubCategories;
            ViewBag.ContentItems = contentItems;

            return View();
        }
    }
}
