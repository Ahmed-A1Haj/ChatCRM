using ChatCRM.MVC.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace ChatCRM.MVC.Controllers
{
    public class HomeController : Controller
    {
        /// <summary>
        /// Root URL handler. Authenticated users go straight to the inbox; everyone else is
        /// punted to the sign-in page. There is intentionally no view — the legacy marketing
        /// landing page was removed in favour of taking the user to the actual product.
        /// </summary>
        public IActionResult Index()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return Redirect("/dashboard");
            }

            return RedirectToAction(nameof(AccountController.Login), "Account");
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
