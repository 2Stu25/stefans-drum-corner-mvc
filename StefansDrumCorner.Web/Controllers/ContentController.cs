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

        public async Task<IActionResult> Index()
        {
            var contentItems = await _context.ContentItems
                .Include(c => c.Category)
                .OrderBy(c => c.DisplayOrder)
                .ThenByDescending(c => c.CreatedAtUtc)
                .ToListAsync();

            return View(contentItems);
        }

        

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

            ViewBag.Categories = await _context.ContentCategories
                .Where(c => c.IsActive)
                .OrderBy(c => c.ParentCategoryId.HasValue)
                .ThenBy(c => c.ParentCategoryId)
                .ThenBy(c => c.DisplayOrder)
                .ToListAsync();

            return View(contentItem);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int id,
            [Bind("Title,Description,CategoryId,ContentType,Status,DisplayOrder,IsFeatured")]
    ContentItem input,
            IFormFile? mainVideo,
            IFormFile? thumbnail,
            List<IFormFile>? documents,
            List<IFormFile>? galleryImages,
            List<IFormFile>? audioFiles,
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
            contentItem.ContentType = input.ContentType;
            contentItem.Status = input.Status;
            contentItem.DisplayOrder = input.DisplayOrder;
            contentItem.IsFeatured = input.IsFeatured;

            // Validate the submitted values.
            if (string.IsNullOrWhiteSpace(contentItem.Title))
            {
                ModelState.AddModelError("Title", "A title is required.");
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
                ViewBag.Categories = await _context.ContentCategories
                    .Where(c => c.IsActive || c.Id == contentItem.CategoryId)
                    .OrderBy(c => c.ParentCategoryId.HasValue)
                    .ThenBy(c => c.ParentCategoryId)
                    .ThenBy(c => c.DisplayOrder)
                    .ToListAsync();

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

                // A new main video replaces the existing main video.
                if (mainVideo != null && mainVideo.Length > 0)
                {
                    attachmentsToRemove.AddRange(
                        contentItem.ContentMedia.Where(
                            cm => cm.Role == MediaRole.MainVideo));
                }

                // A new thumbnail replaces the existing thumbnail.
                if (thumbnail != null && thumbnail.Length > 0)
                {
                    attachmentsToRemove.AddRange(
                        contentItem.ContentMedia.Where(
                            cm => cm.Role == MediaRole.Thumbnail));
                }

                // Avoid processing the same attachment more than once.
                attachmentsToRemove = attachmentsToRemove
                    .DistinctBy(cm => cm.Id)
                    .ToList();

                foreach (var attachment in attachmentsToRemove)
                {
                    assetsToCheck.Add(attachment.MediaAssetId);
                }

                // Remove the old attachment relationships.
                _context.ContentMedia.RemoveRange(attachmentsToRemove);

                // Add replacement main video.
                if (mainVideo != null && mainVideo.Length > 0)
                {
                    await AddMediaAsync(
                        mainVideo,
                        MediaType.Video,
                        MediaRole.MainVideo,
                        contentItem.Id,
                        0,
                        savedAssets);
                }

                // Add replacement thumbnail.
                if (thumbnail != null && thumbnail.Length > 0)
                {
                    await AddMediaAsync(
                        thumbnail,
                        MediaType.Image,
                        MediaRole.Thumbnail,
                        contentItem.Id,
                        0,
                        savedAssets);
                }

                // Add new documents after existing documents.
                int documentOrder = contentItem.ContentMedia
                    .Where(cm => cm.Role == MediaRole.Document)
                    .Select(cm => cm.DisplayOrder)
                    .DefaultIfEmpty(-1)
                    .Max() + 1;

                foreach (var document in documents ?? new List<IFormFile>())
                {
                    if (document.Length > 0)
                    {
                        await AddMediaAsync(
                            document,
                            MediaType.Document,
                            MediaRole.Document,
                            contentItem.Id,
                            documentOrder++,
                            savedAssets);
                    }
                }

                // Add new gallery images.
                int imageOrder = contentItem.ContentMedia
                    .Where(cm => cm.Role == MediaRole.GalleryImage)
                    .Select(cm => cm.DisplayOrder)
                    .DefaultIfEmpty(-1)
                    .Max() + 1;

                foreach (var image in galleryImages ?? new List<IFormFile>())
                {
                    if (image.Length > 0)
                    {
                        await AddMediaAsync(
                            image,
                            MediaType.Image,
                            MediaRole.GalleryImage,
                            contentItem.Id,
                            imageOrder++,
                            savedAssets);
                    }
                }

                // Add new audio files.
                int audioOrder = contentItem.ContentMedia
                    .Where(cm => cm.Role == MediaRole.Audio)
                    .Select(cm => cm.DisplayOrder)
                    .DefaultIfEmpty(-1)
                    .Max() + 1;

                foreach (var audio in audioFiles ?? new List<IFormFile>())
                {
                    if (audio.Length > 0)
                    {
                        await AddMediaAsync(
                            audio,
                            MediaType.Audio,
                            MediaRole.Audio,
                            contentItem.Id,
                            audioOrder++,
                            savedAssets);
                    }
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

                // Delete physical files only after the database transaction succeeds.
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

                return RedirectToAction(nameof(Index));
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

                ViewBag.Categories = await _context.ContentCategories
                    .Where(c => c.IsActive || c.Id == contentItem.CategoryId)
                    .OrderBy(c => c.ParentCategoryId.HasValue)
                    .ThenBy(c => c.ParentCategoryId)
                    .ThenBy(c => c.DisplayOrder)
                    .ToListAsync();

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
            ContentItem contentItem,
            IFormFile? mainVideo,
            IFormFile? thumbnail,
            List<IFormFile>? documents,
            List<IFormFile>? galleryImages,
            List<IFormFile>? audioFiles)
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

            var savedAssets = new List<MediaAsset>();

            await using var transaction =
                await _context.Database.BeginTransactionAsync();

            try
            {
                // Save the content item first to obtain its database ID.
                _context.ContentItems.Add(contentItem);

                await _context.SaveChangesAsync();

                // Main video.
                if (mainVideo != null && mainVideo.Length > 0)
                {
                    await AddMediaAsync(
                        mainVideo,
                        MediaType.Video,
                        MediaRole.MainVideo,
                        contentItem.Id,
                        0,
                        savedAssets);
                }

                // Thumbnail.
                if (thumbnail != null && thumbnail.Length > 0)
                {
                    await AddMediaAsync(
                        thumbnail,
                        MediaType.Image,
                        MediaRole.Thumbnail,
                        contentItem.Id,
                        0,
                        savedAssets);
                }

                // Documents.
                int documentOrder = 0;

                foreach (var document in documents ?? new List<IFormFile>())
                {
                    if (document.Length > 0)
                    {
                        await AddMediaAsync(
                            document,
                            MediaType.Document,
                            MediaRole.Document,
                            contentItem.Id,
                            documentOrder++,
                            savedAssets);
                    }
                }

                // Gallery images.
                int imageOrder = 0;

                foreach (var image in galleryImages ?? new List<IFormFile>())
                {
                    if (image.Length > 0)
                    {
                        await AddMediaAsync(
                            image,
                            MediaType.Image,
                            MediaRole.GalleryImage,
                            contentItem.Id,
                            imageOrder++,
                            savedAssets);
                    }
                }

                // Audio files.
                int audioOrder = 0;

                foreach (var audio in audioFiles ?? new List<IFormFile>())
                {
                    if (audio.Length > 0)
                    {
                        await AddMediaAsync(
                            audio,
                            MediaType.Audio,
                            MediaRole.Audio,
                            contentItem.Id,
                            audioOrder++,
                            savedAssets);
                    }
                }

                // Save all media records.
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return RedirectToAction(nameof(Index));
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

                ViewBag.Categories = await _context.ContentCategories
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.ParentCategoryId.HasValue)
                    .ThenBy(c => c.ParentCategoryId)
                    .ThenBy(c => c.DisplayOrder)
                    .ToListAsync();

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

            return string.IsNullOrWhiteSpace(slug) ? "content" : slug;
        }

    }
}