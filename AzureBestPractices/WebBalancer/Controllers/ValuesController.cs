using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace WebBalancer
{
    [Route("api/[controller]")]
    [ApiController]
    public class ValuesController : ControllerBase
    {

        [HttpGet]
        public async Task<string> DoIt()
        {
            await Task.Delay(10000);

            return "done";
        }
    }
}
