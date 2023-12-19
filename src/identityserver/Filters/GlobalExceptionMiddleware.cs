﻿using System;
using System.Text;
using System.Threading.Tasks;
using IdentityServer.STS.Admin.Models;
using IdentityServer4.Configuration;
using IdentityServer4.Endpoints.Results;
using IdentityServer4.Extensions;
using IdentityServer4.ResponseHandling;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace IdentityServer.STS.Admin.Filters;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;

    public GlobalExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        try
        {
            context.SetIdentityServerOrigin(context.RequestServices.GetService<IConfiguration>().GetSection("BackendBaseUrl").Value);
            //if (context.Request.Path.HasValue && context.Request.Path.Value.Contains(".well-known/openid-configuration"))
            //{
            //    var baseUrl = context.GetIdentityServerBaseUrl();

            //    var issuerUri = context.GetIdentityServerIssuerUri();

            //    var responseGenerator = context.RequestServices.GetService<IDiscoveryResponseGenerator>();
            //    var options = context.RequestServices.GetService<IdentityServerOptions>();

            //    // generate response
            //    var response = await responseGenerator.CreateDiscoveryDocumentAsync(baseUrl, issuerUri);

            //    var result = new DiscoveryDocumentResult(response, options.Discovery.ResponseCacheInterval).Entries;

            //    context.Response.StatusCode = 200;
            //    byte[] content = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(result));
            //    context.Response.ContentType = "application/json";
            //    context.Response.ContentLength = content.Length;
            //    await context.Response.BodyWriter.WriteAsync(new ReadOnlyMemory<byte>(content));
            //}
            //else
            //{
              
            //}
            await _next(context);
        }
        catch (Exception ex)
        {
            if (ex.Source != "IdentityServer4")
            {
                if (!context.Response.HasStarted && context.Response.StatusCode != 302)
                {
                    string msg = ex.GetBaseException().Message;

                    context.Response.StatusCode = 200;
                    byte[] content = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new ApiResult<object>()
                    {
                        Code = 500,
                        Msg = msg
                    }, new JsonSerializerSettings
                    {
                        ContractResolver = new CamelCasePropertyNamesContractResolver()
                    }));

                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength = content.Length;
                    await context.Response.BodyWriter.WriteAsync(new ReadOnlyMemory<byte>(content));
                }
            }
        }
    }
}