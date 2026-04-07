using Microsoft.AspNetCore.Mvc;

namespace ChatCRM.MVC.Controllers
{
    public class AccountController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
