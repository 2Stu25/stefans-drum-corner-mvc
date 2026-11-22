
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StefansDrumCorner.Web.Data;
using StefansDrumCorner.Web.Models.Content;
using System.Text.RegularExpressions;
using StefansDrumCorner.Web.Services.Media;

namespace StefansDrumCorner.Web.Controllers
{
    [Authorize(Roles = "Admin")]
    public class CategoriesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IMediaStorageService _mediaStorage;

        public CategoriesController(
            ApplicationDbContext context,
            IMediaStorageService mediaStorage)
                {
                    _context = context;
                    _mediaStorage = mediaStorage;
                }

        // =====================================================
        // CREATE
        // =====================================================

        public async Task<IActionResult> Create()
        {
            await LoadParentCategories();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind("Name,Subtitle,Description,ExploreLinkText,ParentCategoryId,IsActive")]
            ContentCategory category,
            IFormFile? imageFile)
                {
                    if (string.IsNullOrWhiteSpace(category.Name))
                    {
                        ModelState.AddModelError(
                            "Name",
                            "A category name is required.");
                    }

                    if (category.ParentCategoryId == null &&
                        string.IsNullOrWhiteSpace(category.Subtitle))
                    {
                        ModelState.AddModelError(
                            "Subtitle",
                            "A subtitle is required for main categories.");
                    }

                    if (category.ParentCategoryId == null &&
                        string.IsNullOrWhiteSpace(category.Description))
                    {
                        ModelState.AddModelError(
                            "Description",
                            "A description is required for main categories.");
                    }

            if (category.ParentCategoryId.HasValue)
                    {
                        bool parentExists = await _context.ContentCategories
                            .AnyAsync(c =>
                                c.Id == category.ParentCategoryId.Value &&
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
                        return await ReturnToWorkspaceAsync(
                            category,
                            newCategoryMode: true);
                    }

            int highestDisplayOrder = await _context.ContentCategories
                        .Where(c =>
                            c.ParentCategoryId == category.ParentCategoryId &&
                            c.IsActive)
                        .Select(c => (int?)c.DisplayOrder)
                        .MaxAsync() ?? 0;

                    category.DisplayOrder = highestDisplayOrder + 1;

                    category.Name = category.Name.Trim();
                    category.Subtitle = category.Subtitle?.Trim() ?? string.Empty;
                    category.ExploreLinkText = category.ExploreLinkText?.Trim() ?? string.Empty;
                    category.Slug = await GenerateUniqueSlug(category.Name);

                    MediaAsset? uploadedImage = null;

                    try
                    {
                        if (imageFile != null && imageFile.Length > 0)
                        {
                            uploadedImage = await _mediaStorage.SaveFileAsync(
                                imageFile,
                                MediaType.Image);

                            _context.MediaAssets.Add(uploadedImage);
                            category.Image = uploadedImage;
                        }

                        _context.ContentCategories.Add(category);

                        await _context.SaveChangesAsync();
                    }
                    catch
                    {
                        if (uploadedImage != null)
                        {
                            await _mediaStorage.DeleteFileAsync(
                                uploadedImage.StorageKey);
                        }

                        throw;
                    }

                    return RedirectToContentManager();
                }

        // =====================================================
        // EDIT
        // =====================================================

        public async Task<IActionResult> Edit(int id)
        {
            var category = await _context.ContentCategories
                .Include(c => c.Image)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (category == null)
            {
                return NotFound();
            }

            await LoadParentCategories();

            return View(category);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            [Bind("Id,Name,Subtitle,Description,ExploreLinkText,ParentCategoryId,IsActive")]
            ContentCategory updatedCategory,
            IFormFile? imageFile,
            bool removeImage = false)
        {
            if (id != updatedCategory.Id)
            {
                return BadRequest();
            }

            var category = await _context.ContentCategories
                .Include(c => c.Image)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (category == null)
            {
                return NotFound();
            }

            if (string.IsNullOrWhiteSpace(updatedCategory.Name))
            {
                ModelState.AddModelError(
                    "Name",
                    "A category name is required.");
            }

            if (updatedCategory.ParentCategoryId == null &&
                string.IsNullOrWhiteSpace(updatedCategory.Subtitle))
            {
                ModelState.AddModelError(
                    "Subtitle",
                    "A subtitle is required for main categories.");
            }

            if (updatedCategory.ParentCategoryId == null &&
                string.IsNullOrWhiteSpace(updatedCategory.Description))
            {
                ModelState.AddModelError(
                    "Description",
                    "A description is required for main categories.");
            }

            if (updatedCategory.ParentCategoryId == category.Id)
            {
                ModelState.AddModelError(
                    "ParentCategoryId",
                    "A category cannot be its own parent.");
            }

            if (updatedCategory.ParentCategoryId.HasValue)
            {
                bool parentExists = await _context.ContentCategories
                    .AnyAsync(c =>
                        c.Id == updatedCategory.ParentCategoryId.Value &&
                        c.ParentCategoryId == null &&
                        c.IsActive);

                if (!parentExists)
                {
                    ModelState.AddModelError(
                        "ParentCategoryId",
                        "Please select a valid main category.");
                }

                bool hasSubCategories = await _context.ContentCategories
                    .AnyAsync(c => c.ParentCategoryId == id);

                if (category.ParentCategoryId == null &&
                    hasSubCategories)
                {
                    ModelState.AddModelError(
                        "ParentCategoryId",
                        "A main category with subcategories cannot be moved.");
                }
            }

            if (!ModelState.IsValid)
            {
                updatedCategory.Image = category.Image;

                return await ReturnToWorkspaceAsync(
                    updatedCategory,
                    newCategoryMode: false,
                    editCategoryMode: true);
            }

            string newName = updatedCategory.Name.Trim();

            if (!string.Equals(
                category.Name,
                newName,
                StringComparison.OrdinalIgnoreCase))
            {
                category.Slug = await GenerateUniqueSlug(newName, id);
            }

            category.Name = newName;
            category.Subtitle = updatedCategory.Subtitle?.Trim() ?? string.Empty;
            category.Description = updatedCategory.Description?.Trim() ?? string.Empty;
            category.ParentCategoryId = updatedCategory.ParentCategoryId;
            category.IsActive = updatedCategory.IsActive;
            category.ExploreLinkText = updatedCategory.ExploreLinkText?.Trim() ?? string.Empty;

            MediaAsset? previousImage = category.Image;
            MediaAsset? uploadedImage = null;

            try
            {
                if (imageFile != null && imageFile.Length > 0)
                {
                    uploadedImage = await _mediaStorage.SaveFileAsync(
                        imageFile,
                        MediaType.Image);

                    _context.MediaAssets.Add(uploadedImage);
                    category.Image = uploadedImage;
                }
                else if (removeImage)
                {
                    category.Image = null;
                }

                await _context.SaveChangesAsync();
            }
            catch
            {
                if (uploadedImage != null)
                {
                    await _mediaStorage.DeleteFileAsync(
                        uploadedImage.StorageKey);
                }

                throw;
            }

            if (previousImage != null &&
                (uploadedImage != null || removeImage))
            {
                await DeleteImageIfUnusedAsync(previousImage);
            }

            return RedirectToContentManager();
        }

        // =====================================================
        // DETAILS
        // =====================================================

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

            ViewBag.HasSubCategories = await _context.ContentCategories
                .AnyAsync(c => c.ParentCategoryId == id);

            return View(category);
        }

        // =====================================================
        // ARCHIVE
        // =====================================================

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

            category.IsActive = false;

            // Archive active subcategories when archiving a main category.
            if (category.ParentCategoryId == null)
            {
                foreach (var subCategory in category.SubCategories)
                {
                    subCategory.IsActive = false;
                }
            }

            await RenumberActiveCategories(category.ParentCategoryId);

            await _context.SaveChangesAsync();

            return RedirectToContentManager();
        }

        // =====================================================
        // RESTORE
        // =====================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restore(
            int id,
            int? position)
        {
            var category = await _context.ContentCategories
                .Include(c => c.SubCategories)
                .FirstOrDefaultAsync(c => c.Id == id && !c.IsActive);

            if (category == null)
            {
                return NotFound();
            }

            if (category.ParentCategoryId.HasValue)
            {
                bool parentIsActive = await _context.ContentCategories
                    .AnyAsync(c =>
                        c.Id == category.ParentCategoryId.Value &&
                        c.IsActive);

                if (!parentIsActive)
                {
                    return BadRequest(
                        "The parent category must be restored first.");
                }
            }

            var siblings = await _context.ContentCategories
                .Where(c =>
                    c.ParentCategoryId == category.ParentCategoryId &&
                    c.IsActive)
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.Id)
                .ToListAsync();

            int insertPosition = position.HasValue
                ? Math.Clamp(position.Value, 1, siblings.Count + 1)
                : siblings.Count + 1;

            siblings.Insert(insertPosition - 1, category);

            category.IsActive = true;

            for (int i = 0; i < siblings.Count; i++)
            {
                siblings[i].DisplayOrder = i + 1;
            }

            // Restore subcategories when restoring a main category.
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

            return RedirectToContentManager();
        }

        // =====================================================
        // DELETE
        // =====================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var category = await _context.ContentCategories
                .FirstOrDefaultAsync(c => c.Id == id);

            if (category == null)
            {
                return NotFound();
            }

            bool hasContentItems = await _context.ContentItems
                .AnyAsync(c => c.CategoryId == id);

            if (hasContentItems)
            {
                return BadRequest(
                    "This category cannot be deleted because it contains content items.");
            }

            bool hasSubCategories = await _context.ContentCategories
                .AnyAsync(c => c.ParentCategoryId == id);

            if (hasSubCategories)
            {
                return BadRequest(
                    "This category cannot be deleted because it has subcategories.");
            }

            int? parentCategoryId = category.ParentCategoryId;

            _context.ContentCategories.Remove(category);

            await RenumberActiveCategories(parentCategoryId);

            await _context.SaveChangesAsync();

            return RedirectToContentManager();
        }

        // =====================================================
        // ARCHIVED CATEGORIES
        // =====================================================

        public async Task<IActionResult> Archived()
        {
            var archivedCategories = await _context.ContentCategories
                .Where(c =>
                    !c.IsActive &&
                    (c.ParentCategoryId == null ||
                     c.ParentCategory!.IsActive))
                .Include(c => c.ParentCategory)
                .Include(c => c.SubCategories.Where(s => !s.IsActive))
                .OrderBy(c => c.ParentCategoryId)
                .ThenBy(c => c.DisplayOrder)
                .ToListAsync();

            return View(archivedCategories);
        }

        // =====================================================
        // HELPERS
        // =====================================================

        private async Task DeleteImageIfUnusedAsync(MediaAsset image)
        {
            bool usedByContent = await _context.ContentMedia
                .AnyAsync(m => m.MediaAssetId == image.Id);

            bool usedByCategory = await _context.ContentCategories
                .AnyAsync(c => c.ImageId == image.Id);

            if (usedByContent || usedByCategory)
            {
                return;
            }

            _context.MediaAssets.Remove(image);

            await _context.SaveChangesAsync();

            try
            {
                await _mediaStorage.DeleteFileAsync(image.StorageKey);
            }
            catch (IOException)
            {
                // The database reference is removed.
                // A failed physical cleanup can be handled separately.
            }
            catch (UnauthorizedAccessException)
            {
                // Avoid failing the category update because of file permissions.
            }
        }

        private async Task LoadParentCategories()
        {
            ViewBag.ParentCategories = await _context.ContentCategories
                .Where(c => c.IsActive && c.ParentCategoryId == null)
                .OrderBy(c => c.DisplayOrder)
                .ToListAsync();
        }

        private async Task RenumberActiveCategories(int? parentCategoryId)
        {
            var categories = await _context.ContentCategories
                .Where(c =>
                    c.ParentCategoryId == parentCategoryId &&
                    c.IsActive)
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.Id)
                .ToListAsync();

            for (int i = 0; i < categories.Count; i++)
            {
                categories[i].DisplayOrder = i + 1;
            }
        }

        private async Task<string> GenerateUniqueSlug(
            string name,
            int? excludeId = null)
        {
            string slug = name.ToLowerInvariant()
                .Replace("å", "a")
                .Replace("ä", "a")
                .Replace("ö", "o");

            slug = Regex.Replace(slug, @"[^a-z0-9]+", "-")
                .Trim('-');

            string baseSlug = slug;
            int counter = 2;

            while (await _context.ContentCategories.AnyAsync(c =>
                c.Slug == slug &&
                (!excludeId.HasValue || c.Id != excludeId.Value)))
            {
                slug = $"{baseSlug}-{counter}";
                counter++;
            }

            return slug;
        }

        private async Task<IActionResult> ReturnToWorkspaceAsync(
            ContentCategory categoryForm,
            bool newCategoryMode,
            bool editCategoryMode = false)
                {
                    var categories = await _context.ContentCategories
                        .Where(c => c.IsActive)
                        .OrderBy(c => c.ParentCategoryId.HasValue)
                        .ThenBy(c => c.ParentCategoryId)
                        .ThenBy(c => c.DisplayOrder)
                        .ToListAsync();

                    var archivedCategories = await _context.ContentCategories
                        .Where(c =>
                            !c.IsActive &&
                            (c.ParentCategoryId == null ||
                             c.ParentCategory!.IsActive))
                        .Include(c => c.ParentCategory)
                        .Include(c => c.SubCategories.Where(s => !s.IsActive))
                        .OrderBy(c => c.ParentCategoryId)
                        .ThenBy(c => c.DisplayOrder)
                        .ToListAsync();

                    var contentItems = await _context.ContentItems
                        .Include(c => c.Category)
                        .OrderBy(c => c.DisplayOrder)
                        .ThenByDescending(c => c.CreatedAtUtc)
                        .ToListAsync();

                    ViewBag.CreateMode = false;
                    ViewBag.NewContentItem = new ContentItem();

                    ViewBag.NewCategoryMode = newCategoryMode;
                    ViewBag.NewCategory = newCategoryMode ? categoryForm : null;

                    ViewBag.EditCategory = editCategoryMode ? categoryForm : null;

                    ViewBag.ShowArchivedCategories = false;
                    ViewBag.ArchivedCategories = archivedCategories;

                    ViewBag.SelectedItem = null;
                    ViewBag.Categories = categories;

                    ViewBag.SelectedCategoryId = editCategoryMode
                        ? categoryForm.Id
                        : categoryForm.ParentCategoryId;

                    ViewBag.SelectedStatus = "";

                    return View(
                        "~/Views/Content/Index.cshtml",
                        contentItems);
                }

        private IActionResult RedirectToContentManager()
        {
            return RedirectToAction("Index", "Content");
        }
    }
}
