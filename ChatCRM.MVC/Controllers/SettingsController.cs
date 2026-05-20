using ChatCRM.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatCRM.MVC.Controllers
{
    [Authorize]
    public class SettingsController : Controller
    {
        private readonly IWhatsAppInstanceService _instanceService;

        public SettingsController(IWhatsAppInstanceService instanceService)
        {
            _instanceService = instanceService;
        }

        [HttpGet("/dashboard/settings")]
        public IActionResult Index()
        {
            return RedirectToAction(nameof(Channels));
        }

        [HttpGet("/dashboard/settings/channels")]
        public async Task<IActionResult> Channels(CancellationToken cancellationToken)
        {
            var instances = await _instanceService.GetAllAsync(cancellationToken);
            ViewBag.WhatsAppCount = instances.Count;
            return View();
        }
    }
}
