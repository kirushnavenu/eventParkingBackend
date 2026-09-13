using EventParking.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using static EventParking.API.DTOs.CustomerDTOs;

namespace EventParking.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class CustomersController : ControllerBase
    {
        private readonly CustomerService _customerService;

        public CustomersController(CustomerService customerService)
        {
            _customerService = customerService;
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> SearchCustomers([FromQuery] string? search)
        {
            return Ok(await _customerService.SearchCustomersAsync(search));
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetProfile(int id)
        {
            if (!CanAccessCustomer(id)) return Forbid();
            try { return Ok(await _customerService.GetProfileAsync(id)); }
            catch (Exception ex) { return NotFound(new { Message = ex.Message }); }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProfile(int id, [FromBody] UpdateProfileDto dto)
        {
            if (!CanAccessCustomer(id)) return Forbid();
            try
            {
                await _customerService.UpdateProfileAsync(id, dto);
                return NoContent();
            }
            catch (Exception ex) { return BadRequest(new { Message = ex.Message }); }
        }

        [Authorize(Roles = "Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeactivateCustomer(int id)
        {
            try
            {
                await _customerService.DeactivateCustomerAsync(id);
                return NoContent();
            }
            catch (Exception ex) { return BadRequest(new { Message = ex.Message }); }
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("{id}/reactivate")]
        public async Task<IActionResult> ReactivateCustomer(int id)
        {
            try
            {
                await _customerService.ReactivateCustomerAsync(id);
                return NoContent();
            }
            catch (Exception ex) { return BadRequest(new { Message = ex.Message }); }
        }

        private bool CanAccessCustomer(int id)
        {
            if (User.IsInRole("Admin")) return true;

            var rawId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? User.FindFirstValue("sub");

            return int.TryParse(rawId, out var customerId) && customerId == id;
        }
    }
}
