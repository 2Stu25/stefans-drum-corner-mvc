
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
            [Bind("Name,Subtitle,ParentCategoryId,IsActive")]
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

            // Assign the next display order within this category level.
            int highestDisplayOrder = await _context.ContentCategories
                .Where(c => c.ParentCategoryId == category.ParentCategoryId && c.IsActive)
                .Select(c => (int?)c.DisplayOrder)
                .MaxAsync() ?? 0;

            category.DisplayOrder = highestDisplayOrder + 1;

           

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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Archive(int id)
        {
            var category = await _context.ContentCategories
                .Include(c => c.SubCategories)
                .FirstOrDefaultAsync(c => c.Id == id && c.IsActive);

            if (category == null)
            {
                return NotFound();
            }

            // Archive the selected category.
            category.IsActive = false;

            // If this is a main category, archive its active subcategories too.
            if (category.ParentCategoryId == null)
            {
                foreach (var subCategory in category.SubCategories)
                {
                    subCategory.IsActive = false;
                }
            }

            // Close the numbering gap among the remaining active categories.
            await RenumberActiveCategories(category.ParentCategoryId);

            await _context.SaveChangesAsync();

            return RedirectToAction("Index", "Admin");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restore(int id, int? position)
        {
            var category = await _context.ContentCategories
                .Include(c => c.SubCategories)
                .FirstOrDefaultAsync(c => c.Id == id && !c.IsActive);

            if (category == null)
            {
                return NotFound();
            }

            // A subcategory can only be restored if its parent is active.
            if (category.ParentCategoryId.HasValue)
            {
                bool parentIsActive = await _context.ContentCategories
                    .AnyAsync(c =>
                        c.Id == category.ParentCategoryId &&
                        c.IsActive);

                if (!parentIsActive)
                {
                    return BadRequest("The parent category must be restored first.");
                }
            }

            // Get the currently active categories at this level.
            var siblings = await _context.ContentCategories
                .Where(c =>
                    c.ParentCategoryId == category.ParentCategoryId &&
                    c.IsActive)
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.Id)
                .ToListAsync();

            // Default to the end if no position is supplied.
            int insertPosition = position.HasValue
                ? Math.Clamp(position.Value, 1, siblings.Count + 1)
                : siblings.Count + 1;

            // Insert the restored category at the selected position.
            siblings.Insert(insertPosition - 1, category);

            category.IsActive = true;

            // Renumber all active categories at this level.
            for (int i = 0; i < siblings.Count; i++)
            {
                siblings[i].DisplayOrder = i + 1;
            }

            // Restore subcategories if this is a main category.
            if (category.ParentCategoryId == null)
            {
                var subCategories = category.SubCategories
                    .OrderBy(s => s.DisplayOrder)
                    .ThenBy(s => s.Id)
                    .ToList();

                for (int i = 0; i < subCategories.Count; i++)
                {
                    subCategories[i].IsActive = true;
                    subCategories[i].DisplayOrder = i + 1;
                }
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("Index", "Admin");
        }

        private async Task RenumberActiveCategories(int? parentCategoryId)
        {
            var categories = await _context.ContentCategories
                .Where(c => c.ParentCategoryId == parentCategoryId && c.IsActive)
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.Id)
                .ToListAsync();

            for (int i = 0; i < categories.Count; i++)
            {
                categories[i].DisplayOrder = i + 1;
            }
        }

        public async Task<IActionResult> Archived()
        {
            var archivedCategories = await _context.ContentCategories
                .Where(c => !c.IsActive &&
                            (c.ParentCategoryId == null || c.ParentCategory!.IsActive))
                .Include(c => c.ParentCategory)
                .Include(c => c.SubCategories.Where(s => !s.IsActive))
                .OrderBy(c => c.ParentCategoryId)
                .ThenBy(c => c.DisplayOrder)
                .ToListAsync();

            return View(archivedCategories);
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
