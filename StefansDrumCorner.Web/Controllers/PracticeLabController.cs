using Microsoft.AspNetCore.Mvc;

namespace StefansDrumCorner.Web.Controllers
{
    public class PracticeLabController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}