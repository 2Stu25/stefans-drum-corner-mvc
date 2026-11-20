
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StefansDrumCorner.Web.Data;
using StefansDrumCorner.Web.Models;
using StefansDrumCorner.Web.Models.Content;
using System.Diagnostics;

namespace StefansDrumCorner.Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HomeController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var categories = await _context.ContentCategories
                .Include(c => c.Image)
                .Where(c => c.IsActive && c.ParentCategoryId == null)
                .OrderBy(c => c.DisplayOrder)
                .Take(4)
                .ToListAsync();

            return View(categories);
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(
            Duration = 0,
            Location = ResponseCacheLocation.None,
            NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel
            {
                RequestId = Activity.Current?.Id
                    ?? HttpContext.TraceIdentifier
            });
        }
    }
}
