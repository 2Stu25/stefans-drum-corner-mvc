using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StefansDrumCorner.Web.Data;
using StefansDrumCorner.Web.Models.Content;

namespace StefansDrumCorner.Web.Controllers
{
    [Authorize(Roles = "Admin")]
    public class ContentController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ContentController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var contentItems = await _context.ContentItems
                .Include(c => c.Category)
                .OrderBy(c => c.DisplayOrder)
                .ThenByDescending(c => c.CreatedAtUtc)
                .ToListAsync();

            return View(contentItems);
        }

        public async Task<IActionResult> Create(int? categoryId)
        {
            ViewBag.Categories = await _context.ContentCategories
                .Where(c => c.IsActive)
                .OrderBy(c => c.ParentCategoryId.HasValue)
                .ThenBy(c => c.ParentCategoryId)
                .ThenBy(c => c.DisplayOrder)
                .ToListAsync();

            if (categoryId.HasValue)
            {
                bool categoryExists = await _context.ContentCategories
                    .AnyAsync(c => c.Id == categoryId && c.IsActive);

                if (!categoryExists)
                {
                    return NotFound();
                }
            }

            ViewBag.SelectedCategoryId = categoryId;

            return View();
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind("Title,Description,CategoryId,ContentType,Status,DisplayOrder,IsFeatured")]
            ContentItem contentItem)
        {
            // Validate required fields.
            if (string.IsNullOrWhiteSpace(contentItem.Title))
            {
                ModelState.AddModelError("Title", "A title is required.");
            }

            // Check that the selected category exists and is active.
            if (contentItem.CategoryId.HasValue)
            {
                bool categoryExists = await _context.ContentCategories
                    .AnyAsync(c => c.Id == contentItem.CategoryId && c.IsActive);

                if (!categoryExists)
                {
                    ModelState.AddModelError("CategoryId", "Please select a valid category.");
                }
            }

            if (!Enum.IsDefined(typeof(ContentType), contentItem.ContentType))
            {
                ModelState.AddModelError("ContentType", "Invalid content type.");
            }

            if (!Enum.IsDefined(typeof(ContentStatus), contentItem.Status))
            {
                ModelState.AddModelError("Status", "Invalid content status.");
            }

            if (contentItem.DisplayOrder < 0)
            {
                ModelState.AddModelError("DisplayOrder", "Display order cannot be negative.");
            }

            if (!ModelState.IsValid)
            {
                ViewBag.Categories = await _context.ContentCategories
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.ParentCategoryId.HasValue)
                    .ThenBy(c => c.ParentCategoryId)
                    .ThenBy(c => c.DisplayOrder)
                    .ToListAsync();

                return View(contentItem);
            }

            // Clean up submitted values.
            contentItem.Title = contentItem.Title.Trim();
            contentItem.Description = contentItem.Description?.Trim() ?? string.Empty;

            // Generate a unique URL slug from the title.
            string baseSlug = GenerateSlug(contentItem.Title);
            string slug = baseSlug;
            int counter = 2;

            while (await _context.ContentItems.AnyAsync(c => c.Slug == slug))
            {
                slug = $"{baseSlug}-{counter}";
                counter++;
            }

            contentItem.Slug = slug;

            // Set server controlled dates.
            contentItem.CreatedAtUtc = DateTime.UtcNow;

            if (contentItem.Status == ContentStatus.Published)
            {
                contentItem.PublishedAtUtc = DateTime.UtcNow;
            }

            _context.ContentItems.Add(contentItem);

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }


        private string GenerateSlug(string title)
        {
            var slug = new string(
                title.ToLowerInvariant()
                    .Select(c => char.IsLetterOrDigit(c) ? c : '-')
                    .ToArray());

            slug = System.Text.RegularExpressions.Regex
                .Replace(slug, "-+", "-")
                .Trim('-');

            return string.IsNullOrWhiteSpace(slug) ? "content" : slug;
        }

    }
}