using ChatCRM.Application.Chats.DTOs;
using ChatCRM.Application.Interfaces;
using ChatCRM.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ChatCRM.MVC.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/instances")]
    public class InstancesController : ControllerBase
    {
        private readonly IWhatsAppInstanceService _service;
        private readonly UserManager<User> _userManager;
        private readonly ILogger<InstancesController> _logger;

        public InstancesController(IWhatsAppInstanceService service, UserManager<User> userManager, ILogger<InstancesController> logger)
        {
            _service = service;
            _userManager = userManager;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken cancellationToken)
        {
            var items = await _service.GetAllAsync(cancellationToken);
            return Ok(items);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
        {
            var item = await _service.GetAsync(id, cancellationToken);
            return item is null ? NotFound() : Ok(item);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateInstanceDto dto, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(dto.DisplayName))
                return BadRequest(new { error = "Display name is required." });

            try
            {
                var userId = _userManager.GetUserId(User);
                var created = await _service.CreateAsync(dto, userId, cancellationToken);
                return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
            }
            catch (DuplicateInstanceException ex)
            {
                return Conflict(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create instance {Name}", dto.DisplayName);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("{id:int}/qr")]
        public async Task<IActionResult> GetQr(int id, CancellationToken cancellationToken)
        {
            try
            {
                var info = await _service.GetConnectInfoAsync(id, cancellationToken);
                return Ok(info);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { error = ex.Message });
            }
        }

        [HttpGet("{id:int}/status")]
        public async Task<IActionResult> RefreshStatus(int id, CancellationToken cancellationToken)
        {
            try
            {
                var dto = await _service.RefreshStatusAsync(id, cancellationToken);
                return Ok(dto);
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { error = ex.Message });
            }
        }

        [HttpPost("{id:int}/disconnect")]
        public async Task<IActionResult> Disconnect(int id, CancellationToken cancellationToken)
        {
            try
            {
                await _service.DisconnectAsync(id, cancellationToken);
                return Ok(new { disconnected = true });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { error = ex.Message });
            }
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
        {
            try
            {
                await _service.DeleteAsync(id, cancellationToken);
                return Ok(new { deleted = true });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { error = ex.Message });
            }
        }
    }
}
