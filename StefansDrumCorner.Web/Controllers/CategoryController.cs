
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StefansDrumCorner.Web.Data;
using StefansDrumCorner.Web.Models.Content;

namespace StefansDrumCorner.Web.Controllers
{
    public class CategoryController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CategoryController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Details(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                return NotFound();
            }

            var category = await _context.ContentCategories
                .Include(c => c.Image)
                .Include(c => c.SubCategories
                    .Where(s => s.IsActive))
                .FirstOrDefaultAsync(c =>
                    c.Slug == slug &&
                    c.IsActive);

            if (category == null)
            {
                return NotFound();
            }

            var contentItems = await _context.ContentItems
                .Where(item =>
                    item.CategoryId == category.Id &&
                    item.Status == ContentStatus.Published)
                .Include(item => item.ContentMedia)
                    .ThenInclude(media => media.MediaAsset)
                .OrderBy(item => item.DisplayOrder)
                .ThenBy(item => item.Title)
                .ToListAsync();

            ViewBag.ContentItems = contentItems;

            return View(category);
        }
    }
}


