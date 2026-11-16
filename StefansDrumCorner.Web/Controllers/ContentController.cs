using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StefansDrumCorner.Web.Data;

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
    }
}