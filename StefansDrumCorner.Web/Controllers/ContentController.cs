
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StefansDrumCorner.Web.Data;
using StefansDrumCorner.Web.Models.Content;
using StefansDrumCorner.Web.Services.Media;
using Microsoft.AspNetCore.Http;

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
            ContentStatus? status)
        {
            // Load active categories for the workspace browser.
            var categories = await _context.ContentCategories
                .Where(c => c.IsActive)
                .OrderBy(c => c.ParentCategoryId.HasValue)
                .ThenBy(c => c.ParentCategoryId)
                .ThenBy(c => c.DisplayOrder)
                .ToListAsync();

            // Start with all content items.
            var query = _context.ContentItems
                .Include(c => c.Category)
                .AsQueryable();

            // Filter by category when one is selected.
            if (categoryId.HasValue)
            {
                bool categoryExists = categories.Any(
                    c => c.Id == categoryId.Value);

                if (!categoryExists)
                {
                    return NotFound();
                }

                query = query.Where(
                    c => c.CategoryId == categoryId.Value);
            }

            // Filter by publication status.
            if (status.HasValue)
            {
                query = query.Where(
                    c => c.Status == status.Value);
            }

            // Get the filtered content list.
            var contentItems = await query
                .OrderBy(c => c.DisplayOrder)
                .ThenByDescending(c => c.CreatedAtUtc)
                .ToListAsync();

            // Keep the requested selection if it belongs
            // to the current filtered list.
            var selectedItemId = selectedId;

            if (!selectedItemId.HasValue ||
                !contentItems.Any(c => c.Id == selectedItemId.Value))
            {
                selectedItemId = contentItems.FirstOrDefault()?.Id;
            }

            ContentItem? selectedItem = null;

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
                        (c.IsActive || c.Id == input.CategoryId));

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
                ViewBag.Categories = await LoadCategoriesAsync(
                    contentItem.CategoryId);

                return View(contentItem);
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
                        continue;

                    var classification = GetMediaClassification(file);

                    if (!nextOrderByRole.TryGetValue(
                        classification.Role, out int displayOrder))
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

                // Delete physical files only after the database
                // transaction succeeds.
                foreach (var asset in orphanedAssets)
                {
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

                // Reload the content and its existing attachments.
                _context.ChangeTracker.Clear();

                contentItem = await _context.ContentItems
                    .Include(c => c.ContentMedia)
                        .ThenInclude(cm => cm.MediaAsset)
                    .FirstAsync(c => c.Id == id);

                ModelState.AddModelError(string.Empty, ex.Message);

                ViewBag.Categories = await LoadCategoriesAsync(
                    contentItem.CategoryId);

                return View(contentItem);
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
        // CREATE CONTENT
        // =====================================================

        public async Task<IActionResult> Create(int? categoryId)
        {
            ViewBag.Categories = await LoadCategoriesAsync();

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

            ViewBag.SelectedCategoryId = categoryId;

            return View();
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
                ViewBag.Categories = await LoadCategoriesAsync();
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

                ModelState.AddModelError(string.Empty, ex.Message);

                ViewBag.Categories = await LoadCategoriesAsync();

                return View(contentItem);
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
