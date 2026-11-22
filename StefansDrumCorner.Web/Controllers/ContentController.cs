
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StefansDrumCorner.Web.Data;
using StefansDrumCorner.Web.Models.Content;
using StefansDrumCorner.Web.Services.Media;

namespace StefansDrumCorner.Web.Controllers
{
    [Authorize(Roles = "Admin")]
    public class ContentController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IMediaStorageService _mediaStorage;

        public ContentController(
            ApplicationDbContext context,
            IMediaStorageService mediaStorage)
        {
            _context = context;
            _mediaStorage = mediaStorage;
        }

        // =====================================================
        // CONTENT MANAGER
        // =====================================================


        public async Task<IActionResult> Index(
            int? selectedId,
            int? categoryId,
            ContentStatus? status,
            bool create = false,
            ContentItem? newContentItem = null,
            bool newCategory = false,
            int? editCategoryId = null,
            int? parentCategoryId = null,
            bool showArchived = false,
            bool categoriesView = false)
                    {
            // Load active categories for the workspace.
            var categories = await _context.ContentCategories
                .Where(c => c.IsActive)
                .OrderBy(c => c.ParentCategoryId.HasValue)
                .ThenBy(c => c.ParentCategoryId)
                .ThenBy(c => c.DisplayOrder)
                .ToListAsync();

            // Load archived categories when the archive view is requested.
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

            // Load the category being edited, if any.
            ContentCategory? categoryToEdit = null;

            if (editCategoryId.HasValue)
            {
                categoryToEdit = await _context.ContentCategories
                    .Include(c => c.Image)
                    .FirstOrDefaultAsync(c => c.Id == editCategoryId.Value);

                if (categoryToEdit == null)
                {
                    return NotFound();
                }
            }
            

            // Load content items.
            var query = _context.ContentItems
                .Include(c => c.Category)
                .AsQueryable();

            if (categoryId.HasValue)
            {
                var selectedCategory = categories.FirstOrDefault(
                    c => c.Id == categoryId.Value);

                if (selectedCategory == null)
                {
                    return NotFound();
                }

                if (selectedCategory.ParentCategoryId == null)
                {
                    var subcategoryIds = categories
                        .Where(c => c.ParentCategoryId == selectedCategory.Id)
                        .Select(c => c.Id)
                        .ToList();

                    query = query.Where(c =>
                        c.CategoryId == selectedCategory.Id ||
                        (c.CategoryId.HasValue &&
                         subcategoryIds.Contains(c.CategoryId.Value)));
                }
                else
                {
                    query = query.Where(
                        c => c.CategoryId == selectedCategory.Id);
                }
            }

            if (status.HasValue)
            {
                query = query.Where(c => c.Status == status.Value);
            }

            var contentItems = await query
                .OrderBy(c => c.DisplayOrder)
                .ThenByDescending(c => c.CreatedAtUtc)
                .ToListAsync();

            ContentItem? selectedItem = null;

            if (!create &&
                !newCategory &&
                !editCategoryId.HasValue &&
                !showArchived &&
                !categoriesView)
            {
                var selectedItemId = selectedId;

                if (!selectedItemId.HasValue ||
                    !contentItems.Any(c => c.Id == selectedItemId.Value))
                {
                    selectedItemId = contentItems.FirstOrDefault()?.Id;
                }

                if (selectedItemId.HasValue)
                {
                    selectedItem = await _context.ContentItems
                        .Include(c => c.Category)
                            .ThenInclude(c => c!.ParentCategory)
                        .Include(c => c.ContentMedia)
                            .ThenInclude(m => m.MediaAsset)
                        .FirstOrDefaultAsync(
                            c => c.Id == selectedItemId.Value);
                }
            }

            // Existing content form information.
            ViewBag.CreateMode = create;

            ViewBag.NewContentItem = newContentItem
                ?? new ContentItem
                {
                    CategoryId = categoryId,
                    Status = ContentStatus.Draft
                };

            // Category workspace information.
            ViewBag.NewCategoryMode = newCategory;
            ViewBag.EditCategory = categoryToEdit;
                var newCategoryModel = new ContentCategory
                {
                    IsActive = true
                };

                // Only allow an active main category to be selected as the parent.
                if (parentCategoryId.HasValue &&
                    categories.Any(c =>
                        c.Id == parentCategoryId.Value &&
                        c.ParentCategoryId == null &&
                        c.IsActive))
                {
                    newCategoryModel.ParentCategoryId = parentCategoryId;
                }

            ViewBag.NewCategory = newCategoryModel;
            ViewBag.ShowArchivedCategories = showArchived;
            ViewBag.ArchivedCategories = archivedCategories;
            ViewBag.CategoriesView = categoriesView;

            // Existing workspace information.
            ViewBag.SelectedItem = selectedItem;
            ViewBag.Categories = categories;
            ViewBag.SelectedCategoryId = categoryId;
            ViewBag.SelectedStatus = status?.ToString();

            return View(contentItems);
        }



        // =====================================================
        // EDIT CONTENT
        // =====================================================

        public async Task<IActionResult> Edit(int id)
        {
            var contentItem = await _context.ContentItems
                .Include(c => c.Category)
                .Include(c => c.ContentMedia)
                    .ThenInclude(cm => cm.MediaAsset)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (contentItem == null)
            {
                return NotFound();
            }

            ViewBag.Categories = await LoadCategoriesAsync(
                contentItem.CategoryId);

            return View(contentItem);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            [Bind("Title,Description,CategoryId,Status,DisplayOrder,IsFeatured")]
            ContentItem input,
            List<IFormFile>? mediaFiles,
            List<int>? removeMediaIds)
        {
            var contentItem = await _context.ContentItems
                .Include(c => c.ContentMedia)
                    .ThenInclude(cm => cm.MediaAsset)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (contentItem == null)
            {
                return NotFound();
            }

            // Remember the original category.
            int? originalCategoryId = contentItem.CategoryId;

            // Copy editable fields onto the existing database entity.
            contentItem.Title = input.Title?.Trim() ?? string.Empty;
            contentItem.Description = input.Description?.Trim() ?? string.Empty;
            contentItem.CategoryId = input.CategoryId;
            contentItem.Status = input.Status;
            contentItem.DisplayOrder = input.DisplayOrder;
            contentItem.IsFeatured = input.IsFeatured;

            // Validate the submitted values.
            if (string.IsNullOrWhiteSpace(contentItem.Title))
            {
                ModelState.AddModelError(
                    "Title",
                    "A title is required.");
            }

            if (contentItem.CategoryId.HasValue)
            {
                bool categoryExists = await _context.ContentCategories
                    .AnyAsync(c =>
                        c.Id == contentItem.CategoryId &&
                        (c.IsActive || c.Id == originalCategoryId));

                if (!categoryExists)
                {
                    ModelState.AddModelError(
                        "CategoryId",
                        "Please select a valid category.");
                }
            }

            if (!Enum.IsDefined(typeof(ContentStatus), contentItem.Status))
            {
                ModelState.AddModelError(
                    "Status",
                    "Invalid content status.");
            }

            if (contentItem.DisplayOrder < 0)
            {
                ModelState.AddModelError(
                    "DisplayOrder",
                    "Display order cannot be negative.");
            }

            // Validate requested attachment removals.
            var requestedRemovalIds = (removeMediaIds ?? new List<int>())
                .Distinct()
                .ToList();

            var selectedAttachments = contentItem.ContentMedia
                .Where(cm => requestedRemovalIds.Contains(cm.Id))
                .ToList();

            if (selectedAttachments.Count != requestedRemovalIds.Count)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "One or more selected attachments are invalid.");
            }

            if (!ModelState.IsValid)
            {
                // Return to the Content Manager with the submitted values.
                return await Index(
                    selectedId: id,
                    categoryId: contentItem.CategoryId,
                    status: null,
                    create: false);
            }

            // Set server-controlled update information.
            contentItem.UpdatedAtUtc = DateTime.UtcNow;

            if (contentItem.Status == ContentStatus.Published &&
                contentItem.PublishedAtUtc == null)
            {
                contentItem.PublishedAtUtc = DateTime.UtcNow;
            }

            var savedAssets = new List<MediaAsset>();
            var assetsToCheck = new HashSet<int>();

            // Begin the database transaction.
            await using var transaction =
                await _context.Database.BeginTransactionAsync();

            try
            {
                // Start with attachments explicitly selected for removal.
                var attachmentsToRemove = new List<ContentMedia>(
                    selectedAttachments);

                // Remember the assets that may become orphaned.
                foreach (var attachment in attachmentsToRemove)
                {
                    assetsToCheck.Add(attachment.MediaAssetId);
                }

                // Remove the selected attachment relationships.
                _context.ContentMedia.RemoveRange(attachmentsToRemove);

                // Determine the next display order for each media role.
                var nextOrderByRole = contentItem.ContentMedia
                    .Where(cm => !attachmentsToRemove.Contains(cm))
                    .GroupBy(cm => cm.Role)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Max(cm => cm.DisplayOrder) + 1);

                // Process all newly uploaded files.
                foreach (var file in mediaFiles ?? new List<IFormFile>())
                {
                    if (file == null || file.Length == 0)
                    {
                        continue;
                    }

                    var classification = GetMediaClassification(file);

                    if (!nextOrderByRole.TryGetValue(
                        classification.Role,
                        out int displayOrder))
                    {
                        displayOrder = 0;
                    }

                    await AddMediaAsync(
                        file,
                        classification.MediaType,
                        classification.Role,
                        contentItem.Id,
                        displayOrder,
                        savedAssets);

                    nextOrderByRole[classification.Role] = displayOrder + 1;
                }

                // Save content and attachment changes.
                await _context.SaveChangesAsync();

                // Find media assets that no longer have any attachments.
                var orphanedAssets = await _context.MediaAssets
                    .Where(asset =>
                        assetsToCheck.Contains(asset.Id) &&
                        !_context.ContentMedia.Any(
                            cm => cm.MediaAssetId == asset.Id))
                    .ToListAsync();

                // Remove orphaned asset records from the database.
                _context.MediaAssets.RemoveRange(orphanedAssets);

                await _context.SaveChangesAsync();

                await transaction.CommitAsync();


                // Delete uploaded files only after the database
                // transaction succeeds.
                //
                // Legacy files stored directly in wwwroot are deliberately
                // preserved and must not be deleted by Content Manager.
                foreach (var asset in orphanedAssets)
                {
                    if (string.IsNullOrWhiteSpace(asset.StorageKey) ||
                        !asset.StorageKey.Replace('\\', '/')
                            .StartsWith(
                                "uploads/content/",
                                StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        await _mediaStorage.DeleteFileAsync(asset.StorageKey);
                    }
                    catch (IOException)
                    {
                        // The database update succeeded.
                        // A leftover physical file can be cleaned up later.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // The database update succeeded.
                        // A leftover physical file can be cleaned up later.
                    }
                }


                return RedirectToAction(
                    nameof(Index),
                    new { selectedId = contentItem.Id });
            }
            catch (ArgumentException ex)
            {
                await transaction.RollbackAsync();

                // Remove newly uploaded files if the update failed.
                foreach (var asset in savedAssets)
                {
                    await _mediaStorage.DeleteFileAsync(asset.StorageKey);
                }

                // Reload the database state.
                _context.ChangeTracker.Clear();

                ModelState.AddModelError(string.Empty, ex.Message);

                // Return to the Content Manager with the existing item selected.
                return await Index(
                    selectedId: id,
                    categoryId: contentItem.CategoryId,
                    status: null,
                    create: false);
            }
            catch
            {
                await transaction.RollbackAsync();

                // Remove newly uploaded files if the database update failed.
                foreach (var asset in savedAssets)
                {
                    await _mediaStorage.DeleteFileAsync(asset.StorageKey);
                }

                throw;
            }
        }


        // =====================================================
        // REMOVE MEDIA
        // =====================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveMedia(
            int contentItemId,
            int mediaId)
        {
            // Find the attachment and verify that it belongs to this content item.
            var attachment = await _context.ContentMedia
                .Include(cm => cm.MediaAsset)
                .FirstOrDefaultAsync(cm =>
                    cm.Id == mediaId &&
                    cm.ContentItemId == contentItemId);

            if (attachment == null)
            {
                return NotFound();
            }

            // Keep the asset information before removing the attachment.
            int mediaAssetId = attachment.MediaAssetId;
            var mediaAsset = attachment.MediaAsset;

            await using var transaction =
                await _context.Database.BeginTransactionAsync();

            try
            {
                // Remove the attachment relationship.
                _context.ContentMedia.Remove(attachment);
                await _context.SaveChangesAsync();

                // Check whether another content item still uses this asset.
                bool assetIsStillUsed = await _context.ContentMedia
                    .AnyAsync(cm => cm.MediaAssetId == mediaAssetId);

                if (!assetIsStillUsed)
                {
                    // No other content item uses it, so remove its database record.
                    _context.MediaAssets.Remove(mediaAsset);
                    await _context.SaveChangesAsync();
                }

                await transaction.CommitAsync();

                // Delete the physical file only if no other item uses it.
                if (!assetIsStillUsed)
                {
                    try
                    {
                        await _mediaStorage.DeleteFileAsync(mediaAsset.StorageKey);
                    }
                    catch (IOException)
                    {
                        // The database update succeeded.
                        // A leftover physical file can be cleaned up later.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // The database update succeeded.
                        // A leftover physical file can be cleaned up later.
                    }
                }

                return RedirectToAction(
                    nameof(Index),
                    new { selectedId = contentItemId });
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        // =====================================================
        // REPLACE MEDIA
        // =====================================================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReplaceMedia(
    int contentItemId,
    int mediaId,
    IFormFile? replacementFile)
        {
            var media = await _context.ContentMedia
                .Include(m => m.MediaAsset)
                .FirstOrDefaultAsync(m =>
                    m.Id == mediaId &&
                    m.ContentItemId == contentItemId);

            if (media == null || media.MediaAsset == null)
            {
                return NotFound();
            }

            if (replacementFile == null || replacementFile.Length == 0)
            {
                TempData["MediaError"] = "Please select a replacement file.";

                return RedirectToAction(
                    nameof(Index),
                    new { selectedId = contentItemId });
            }

            var oldAsset = media.MediaAsset;

            if (oldAsset.MediaType == MediaType.Other)
            {
                TempData["MediaError"] =
                    "This media type cannot be replaced using this upload form.";

                return RedirectToAction(
                    nameof(Index),
                    new { selectedId = contentItemId });
            }

            MediaAsset? replacementAsset = null;
            bool oldAssetIsOrphaned = false;

            try
            {
                // Save the new physical file using the existing media type.
                // The storage service validates its extension and file signature.
                replacementAsset = await _mediaStorage.SaveFileAsync(
                    replacementFile,
                    oldAsset.MediaType,
                    oldAsset.AltText);

                await using var transaction =
                    await _context.Database.BeginTransactionAsync();

                // Add the new asset to the database.
                _context.MediaAssets.Add(replacementAsset);

                await _context.SaveChangesAsync();

                // Point the existing attachment to the new asset.
                // Its Role, DisplayOrder and Caption remain unchanged.
                media.MediaAssetId = replacementAsset.Id;
                media.MediaAsset = replacementAsset;

                await _context.SaveChangesAsync();

                // Check whether the old asset is still used elsewhere.
                oldAssetIsOrphaned = !await _context.ContentMedia
                    .AnyAsync(m => m.MediaAssetId == oldAsset.Id);

                if (oldAssetIsOrphaned)
                {
                    _context.MediaAssets.Remove(oldAsset);
                    await _context.SaveChangesAsync();
                }

                await transaction.CommitAsync();
            }
            catch
            {
                // The database transaction has failed or rolled back.
                // Remove the newly uploaded file so it is not left unused.
                if (replacementAsset != null)
                {
                    try
                    {
                        await _mediaStorage.DeleteFileAsync(
                            replacementAsset.StorageKey);
                    }
                    catch
                    {
                        // Do not hide the original replacement error.
                    }
                }

                TempData["MediaError"] =
                    "The media replacement failed. The original attachment has been retained.";

                return RedirectToAction(
                    nameof(Index),
                    new { selectedId = contentItemId });
            }

            // Only delete the old physical file after the database transaction
            // has committed successfully.
            if (oldAssetIsOrphaned)
            {
                try
                {
                    await _mediaStorage.DeleteFileAsync(
                        oldAsset.StorageKey);
                }
                catch
                {
                    TempData["MediaError"] =
                        "The replacement succeeded, but the old physical file could not be deleted.";
                }
            }

            return RedirectToAction(
                nameof(Index),
                new { selectedId = contentItemId });
        }

        // =====================================================
        // CREATE CONTENT
        // =====================================================

        public async Task<IActionResult> Create(int? categoryId)
        {
            if (categoryId.HasValue)
            {
                bool categoryExists = await _context.ContentCategories
                    .AnyAsync(c =>
                        c.Id == categoryId.Value &&
                        c.IsActive);

                if (!categoryExists)
                {
                    return NotFound();
                }
            }

            // Open the new content form inside the Content Manager.
            return RedirectToAction(
                nameof(Index),
                new { categoryId, create = true });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            [Bind("Title,Description,CategoryId,ContentType,Status,DisplayOrder,IsFeatured")]
            ContentItem contentItem,
            List<IFormFile>? mediaFiles)
        {
            // Validate required fields.
            if (string.IsNullOrWhiteSpace(contentItem.Title))
            {
                ModelState.AddModelError(
                    "Title",
                    "A title is required.");
            }

            // Check that the selected category exists and is active.
            if (contentItem.CategoryId.HasValue)
            {
                bool categoryExists = await _context.ContentCategories
                    .AnyAsync(c =>
                        c.Id == contentItem.CategoryId &&
                        c.IsActive);

                if (!categoryExists)
                {
                    ModelState.AddModelError(
                        "CategoryId",
                        "Please select a valid category.");
                }
            }

            if (!Enum.IsDefined(typeof(ContentStatus), contentItem.Status))
            {
                ModelState.AddModelError(
                    "Status",
                    "Invalid content status.");
            }

            if (contentItem.DisplayOrder < 0)
            {
                ModelState.AddModelError(
                    "DisplayOrder",
                    "Display order cannot be negative.");
            }

            if (!ModelState.IsValid)
            {
                // Return to the Content Manager with the new content form open.
                return await Index(
                    selectedId: null,
                    categoryId: contentItem.CategoryId,
                    status: null,
                    create: true,
                    newContentItem: contentItem);
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

            // Set server-controlled dates.
            contentItem.CreatedAtUtc = DateTime.UtcNow;

            if (contentItem.Status == ContentStatus.Published)
            {
                contentItem.PublishedAtUtc = DateTime.UtcNow;
            }

            var savedAssets = new List<MediaAsset>();

            await using var transaction =
                await _context.Database.BeginTransactionAsync();

            try
            {
                // Save the content item first to obtain its database ID.
                _context.ContentItems.Add(contentItem);

                await _context.SaveChangesAsync();

                // Determine the next display order for each media role.
                var nextOrderByRole = new Dictionary<MediaRole, int>();

                // Process all uploaded files.
                foreach (var file in mediaFiles ?? new List<IFormFile>())
                {
                    if (file == null || file.Length == 0)
                    {
                        continue;
                    }

                    var classification = GetMediaClassification(file);

                    if (!nextOrderByRole.TryGetValue(
                        classification.Role,
                        out int displayOrder))
                    {
                        displayOrder = 0;
                    }

                    await AddMediaAsync(
                        file,
                        classification.MediaType,
                        classification.Role,
                        contentItem.Id,
                        displayOrder,
                        savedAssets);

                    nextOrderByRole[classification.Role] = displayOrder + 1;
                }

                // Save all media records.
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return RedirectToAction(
                    nameof(Index),
                    new { selectedId = contentItem.Id });
            }
            catch (ArgumentException ex)
            {
                await transaction.RollbackAsync();

                // Remove files already uploaded before the validation error.
                foreach (var asset in savedAssets)
                {
                    await _mediaStorage.DeleteFileAsync(asset.StorageKey);
                }

                // Clear entities from the rolled-back transaction.
                _context.ChangeTracker.Clear();

                ModelState.AddModelError(string.Empty, ex.Message);

                // Return to the Content Manager with the submitted values.
                return await Index(
                    selectedId: null,
                    categoryId: contentItem.CategoryId,
                    status: null,
                    create: true,
                    newContentItem: contentItem);
            }
            catch
            {
                await transaction.RollbackAsync();

                // Clean up uploaded files if the database operation fails.
                foreach (var asset in savedAssets)
                {
                    await _mediaStorage.DeleteFileAsync(asset.StorageKey);
                }

                throw;
            }
        }


        // =====================================================
        // HELPERS
        // =====================================================

        private async Task<List<ContentCategory>> LoadCategoriesAsync(
            int? includeCategoryId = null)
        {
            return await _context.ContentCategories
                .Where(c =>
                    c.IsActive ||
                    (includeCategoryId.HasValue &&
                     c.Id == includeCategoryId.Value))
                .OrderBy(c => c.ParentCategoryId.HasValue)
                .ThenBy(c => c.ParentCategoryId)
                .ThenBy(c => c.DisplayOrder)
                .ToListAsync();
        }


        private static (MediaType MediaType, MediaRole Role)
            GetMediaClassification(IFormFile file)
        {
            string extension = Path.GetExtension(file.FileName)
                .ToLowerInvariant();

            return extension switch
            {
                // Video
                ".mp4" or ".webm" or ".mov" =>
                    (MediaType.Video, MediaRole.MainVideo),

                // Images
                ".jpg" or ".jpeg" or ".png" or ".webp" =>
                    (MediaType.Image, MediaRole.GalleryImage),

                // Audio
                ".mp3" or ".wav" =>
                    (MediaType.Audio, MediaRole.Audio),

                // Documents
                ".pdf" =>
                    (MediaType.Document, MediaRole.Document),

                _ => throw new ArgumentException(
                    $"The file '{file.FileName}' has an unsupported file type.")
            };
        }


        private async Task AddMediaAsync(
            IFormFile file,
            MediaType mediaType,
            MediaRole role,
            int contentItemId,
            int displayOrder,
            List<MediaAsset> savedAssets)
        {
            // Save the physical file.
            MediaAsset asset = await _mediaStorage.SaveFileAsync(
                file,
                mediaType);

            // Keep track of the file for cleanup if something fails.
            savedAssets.Add(asset);

            // Register the media asset in the database.
            _context.MediaAssets.Add(asset);

            // Connect the media asset to its content item.
            _context.ContentMedia.Add(new ContentMedia
            {
                ContentItemId = contentItemId,
                MediaAsset = asset,
                Role = role,
                DisplayOrder = displayOrder
            });
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

            return string.IsNullOrWhiteSpace(slug)
                ? "content"
                : slug;
        }
    }
}
