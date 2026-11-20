
using Microsoft.AspNetCore.Http;
using StefansDrumCorner.Web.Models.Content;

namespace StefansDrumCorner.Web.Services.Media
{
    public interface IMediaStorageService
    {
        Task<MediaAsset> SaveFileAsync(
            IFormFile file,
            MediaType mediaType,
            string? altText = null);

        Task DeleteFileAsync(string storageKey);
    }
}
