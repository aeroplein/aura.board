using Microsoft.AspNetCore.Mvc;

namespace DigitalVisionBoard.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
