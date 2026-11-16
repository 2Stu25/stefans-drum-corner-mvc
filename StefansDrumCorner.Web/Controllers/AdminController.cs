using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StefansDrumCorner.Web.Data;

namespace StefansDrumCorner.Web.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AdminController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var categories = await _context.ContentCategories
                .Where(c => c.IsActive && c.ParentCategoryId == null)
                .Include(c => c.SubCategories.Where(s => s.IsActive))
                .OrderBy(c => c.DisplayOrder)
                .ToListAsync();

            return View(categories);
        }
    }
}