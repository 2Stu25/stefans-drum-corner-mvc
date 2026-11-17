
using StefansDrumCorner.Web.Models.Content;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace StefansDrumCorner.Web.Services.Media
{
    public class LocalMediaStorageService : IMediaStorageService
    {
        private readonly IWebHostEnvironment _environment;

        private const long MaxVideoSize = 250 * 1024 * 1024;
        private const long MaxImageSize = 15 * 1024 * 1024;
        private const long MaxDocumentSize = 30 * 1024 * 1024;
        private const long MaxAudioSize = 100 * 1024 * 1024;

        public LocalMediaStorageService(IWebHostEnvironment environment)
        {
            _environment = environment;
        }

        public async Task<MediaAsset> SaveFileAsync(
            IFormFile file,
            MediaType mediaType,
            string? altText = null)
        {
            if (file == null || file.Length == 0)
            {
                throw new ArgumentException("The uploaded file is empty.");
            }

            string extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            string folderName = mediaType switch
            {
                MediaType.Video => "videos",
                MediaType.Image => "images",
                MediaType.Document => "documents",
                MediaType.Audio => "audio",
                _ => throw new ArgumentException("Unsupported media type.")
            };

            long maxSize = mediaType switch
            {
                MediaType.Video => MaxVideoSize,
                MediaType.Image => MaxImageSize,
                MediaType.Document => MaxDocumentSize,
                MediaType.Audio => MaxAudioSize,
                _ => 0
            };

            if (file.Length > maxSize)
            {
                throw new ArgumentException(
                    $"The file exceeds the maximum allowed size of {maxSize / (1024 * 1024)} MB.");
            }

            string[] allowedExtensions = mediaType switch
            {
                MediaType.Video => [".mp4", ".webm", ".mov"],
                MediaType.Image => [".jpg", ".jpeg", ".png", ".webp"],
                MediaType.Document => [".pdf"],
                MediaType.Audio => [".mp3", ".wav"],
                _ => []
            };

            if (!allowedExtensions.Contains(extension))
            {
                throw new ArgumentException(
                    "This file extension is not allowed for the selected media type.");
            }

            if (!await HasValidSignatureAsync(file, extension))
            {
                throw new ArgumentException(
                    "The file contents do not match the selected file format.");
            }

            string contentType = GetContentType(extension);

            string uploadFolder = Path.Combine(
                _environment.WebRootPath,
                "uploads",
                "content",
                folderName);

            Directory.CreateDirectory(uploadFolder);

            string storedFileName = $"{Guid.NewGuid()}{extension}";

            string physicalPath = Path.Combine(
                uploadFolder,
                storedFileName);

            await using (var stream = new FileStream(
                physicalPath,
                FileMode.CreateNew,
                FileAccess.Write))
            {
                await file.CopyToAsync(stream);
            }

            return new MediaAsset
            {
                OriginalFileName = Path.GetFileName(file.FileName),
                StorageKey = $"uploads/content/{folderName}/{storedFileName}",
                StorageProvider = "Local",
                ContentType = contentType,
                FileSizeBytes = file.Length,
                MediaType = mediaType,
                AltText = altText,
                UploadedAtUtc = DateTime.UtcNow
            };
        }

        private async Task<bool> HasValidSignatureAsync(
            IFormFile file,
            string extension)
        {
            byte[] header = new byte[12];

            await using var stream = file.OpenReadStream();

            int bytesRead = await stream.ReadAsync(header.AsMemory(0, header.Length));

            if (bytesRead < 4)
            {
                return false;
            }

            return extension switch
            {
                ".jpg" or ".jpeg" =>
                    header[0] == 0xFF &&
                    header[1] == 0xD8 &&
                    header[2] == 0xFF,

                ".png" =>
                    header[0] == 0x89 &&
                    header[1] == 0x50 &&
                    header[2] == 0x4E &&
                    header[3] == 0x47,

                ".webp" =>
                    bytesRead >= 12 &&
                    header[0] == 'R' &&
                    header[1] == 'I' &&
                    header[2] == 'F' &&
                    header[3] == 'F' &&
                    header[8] == 'W' &&
                    header[9] == 'E' &&
                    header[10] == 'B' &&
                    header[11] == 'P',

                ".pdf" =>
                    header[0] == '%' &&
                    header[1] == 'P' &&
                    header[2] == 'D' &&
                    header[3] == 'F',

                ".mp4" or ".mov" =>
                    bytesRead >= 8 &&
                    header[4] == 'f' &&
                    header[5] == 't' &&
                    header[6] == 'y' &&
                    header[7] == 'p',

                ".webm" =>
                    bytesRead >= 4 &&
                    header[0] == 0x1A &&
                    header[1] == 0x45 &&
                    header[2] == 0xDF &&
                    header[3] == 0xA3,

                ".mp3" =>
                    (bytesRead >= 3 &&
                     header[0] == 'I' &&
                     header[1] == 'D' &&
                     header[2] == '3') ||
                    (header[0] == 0xFF &&
                     (header[1] & 0xE0) == 0xE0),

                ".wav" =>
                    bytesRead >= 12 &&
                    header[0] == 'R' &&
                    header[1] == 'I' &&
                    header[2] == 'F' &&
                    header[3] == 'F' &&
                    header[8] == 'W' &&
                    header[9] == 'A' &&
                    header[10] == 'V' &&
                    header[11] == 'E',

                _ => false
            };
        }

        private string GetContentType(string extension)
        {
            return extension switch
            {
                ".mp4" => "video/mp4",
                ".mov" => "video/quicktime",
                ".webm" => "video/webm",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".pdf" => "application/pdf",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                _ => "application/octet-stream"
            };
        }

        public Task DeleteFileAsync(string storageKey)
        {
            if (string.IsNullOrWhiteSpace(storageKey))
            {
                throw new ArgumentException("Storage key cannot be empty.");
            }

            string webRoot = Path.GetFullPath(_environment.WebRootPath);

            string uploadRoot = Path.GetFullPath(
                Path.Combine(webRoot, "uploads", "content"));

            string filePath = Path.GetFullPath(
                Path.Combine(
                    webRoot,
                    storageKey.Replace('/', Path.DirectorySeparatorChar)));

            if (!filePath.StartsWith(
                uploadRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Invalid storage key.");
            }

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            return Task.CompletedTask;
        }
    }
}
