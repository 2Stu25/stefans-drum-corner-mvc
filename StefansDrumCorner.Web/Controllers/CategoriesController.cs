
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StefansDrumCorner.Web.Data;
using StefansDrumCorner.Web.Models.Content;
using System.Text.RegularExpressions;

namespace StefansDrumCorner.Web.Controllers
{
    [Authorize(Roles = "Admin")]
    public class CategoriesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CategoriesController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Create()
        {
            await LoadParentCategories();

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind("Name,Subtitle,ParentCategoryId,DisplayOrder,IsActive")]
            ContentCategory category)
        {
            if (string.IsNullOrWhiteSpace(category.Name))
            {
                ModelState.AddModelError("Name", "A category name is required.");
            }

            if (category.ParentCategoryId == null &&
                string.IsNullOrWhiteSpace(category.Subtitle))
            {
                ModelState.AddModelError("Subtitle", "A subtitle is required for main categories.");
            }

            if (category.DisplayOrder < 1)
            {
                ModelState.AddModelError("DisplayOrder", "Display order must be at least 1.");
            }

            if (category.ParentCategoryId.HasValue)
            {
                bool parentExists = await _context.ContentCategories
                    .AnyAsync(c =>
                        c.Id == category.ParentCategoryId &&
                        c.ParentCategoryId == null &&
                        c.IsActive);

                if (!parentExists)
                {
                    ModelState.AddModelError(
                        "ParentCategoryId",
                        "Please select a valid main category.");
                }
            }

            if (!ModelState.IsValid)
            {
                await LoadParentCategories();

                return View(category);
            }

            category.Name = category.Name.Trim();
            category.Subtitle = category.Subtitle?.Trim() ?? string.Empty;
            category.Slug = GenerateSlug(category.Name);

            string baseSlug = category.Slug;
            int counter = 2;

            while (await _context.ContentCategories.AnyAsync(c => c.Slug == category.Slug))
            {
                category.Slug = $"{baseSlug}-{counter}";
                counter++;
            }

            _context.ContentCategories.Add(category);

            await _context.SaveChangesAsync();

            return RedirectToAction("Index", "Admin");
        }

        public async Task<IActionResult> Details(int id)
        {
            var category = await _context.ContentCategories
                .Include(c => c.SubCategories.Where(s => s.IsActive))
                .FirstOrDefaultAsync(c => c.Id == id && c.IsActive);

            if (category == null)
            {
                return NotFound();
            }

            var contentItems = await _context.ContentItems
                .Where(c => c.CategoryId == id)
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.Title)
                .ToListAsync();

            ViewBag.ContentItems = contentItems;

            return View(category);
        }

        private async Task LoadParentCategories()
        {
            ViewBag.ParentCategories = await _context.ContentCategories
                .Where(c => c.IsActive && c.ParentCategoryId == null)
                .OrderBy(c => c.DisplayOrder)
                .ToListAsync();
        }

        private string GenerateSlug(string name)
        {
            string slug = name.ToLowerInvariant();

            slug = slug
                .Replace("å", "a")
                .Replace("ä", "a")
                .Replace("ö", "o");

            slug = Regex.Replace(slug, @"[^a-z0-9]+", "-");

            return slug.Trim('-');
        }
    }
}
