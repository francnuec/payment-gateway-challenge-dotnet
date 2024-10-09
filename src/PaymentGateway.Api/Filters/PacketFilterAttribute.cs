using System;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Filters
{
    public class PacketFilterAttribute: Attribute, IActionFilter
    {
        public PacketFilterAttribute()
        {
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
            if (context.Exception == null
                && context.Result is IStatusCodeActionResult statusCodeActionResult)
            {
                var packet = new Packet<object>();

                if (!context.ModelState.IsValid)
                {
                    JsonObject errorsObject = new JsonObject();

                    foreach(var entry in context.ModelState)
                    {
                        if (entry.Value.Errors.Count > 0)
                        {
                            errorsObject[entry.Key] = new JsonArray(entry.Value.Errors.Select(e => (JsonValue)e.ErrorMessage).ToArray());
                        }
                    }

                    packet.Meta = errorsObject;
                }

                if (statusCodeActionResult is ObjectResult objectResult)
                {
                    packet.Data = objectResult.Value;
                    objectResult.Value = packet;
                }
                else
                {
                    objectResult = new ObjectResult(packet)
                    {
                        StatusCode = statusCodeActionResult.StatusCode
                    };
                }

                context.Result = objectResult;
            }
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            
        }
    }
}

