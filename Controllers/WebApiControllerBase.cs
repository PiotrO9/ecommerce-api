using Microsoft.AspNetCore.Mvc;

namespace E_commerce_backend.Controllers
{
    [ApiController]
    [Route("api/v1/web/[controller]")]
    public abstract class WebApiControllerBase : ControllerBase
    {
    }
}
