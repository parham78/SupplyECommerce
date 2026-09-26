using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/me/checkout")]
[Authorize(Roles = AppRoles.Customer)]
public class MeCheckoutController : ControllerBase
{
    private readonly ICheckoutService _checkoutService;

    public MeCheckoutController(
        ICheckoutService checkoutService)
    {
        _checkoutService = checkoutService;
    }

    [HttpPost]
    public async Task<ActionResult<CheckoutResponseDto>> Checkout(
        CheckoutRequestDto dto)
    {
        var result =
            await _checkoutService.Checkout(dto);

        return Ok(result);
    }
}