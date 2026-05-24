using ChatCRM.MVC.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace ChatCRM.MVC.Controllers
{
    public class HomeController : Controller
    {
        /// <summary>
        /// Root URL handler. Authenticated users go straight to the inbox; guests see the
        /// public marketing landing page (see Views/Home/Index.cshtml).
        /// </summary>
        public IActionResult Index()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return Redirect("/dashboard");
            }

            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
