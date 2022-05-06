using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Web.Http;

namespace Daenet.WebBalancer
{
    public class HttpResponseExceptionFilter : IActionFilter, IOrderedFilter
    {
        public int Order => int.MaxValue - 10;

        public void OnActionExecuting(ActionExecutingContext context) { }

        public void OnActionExecuted(ActionExecutedContext context)
        {
            if (context.Exception is HttpResponseException httpResponseException)
            {
                context.Result = new ObjectResult(httpResponseException.Response.Content.ReadAsStringAsync().Result)
                {
                    StatusCode = ((int)httpResponseException.Response.StatusCode)
                    
                };

                context.ExceptionHandled = true;
            }
        }
    }
}
