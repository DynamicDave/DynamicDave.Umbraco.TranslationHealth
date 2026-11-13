using DynamicDave.Umbraco.TranslationHealth.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.Attributes;
using Umbraco.Cms.Web.Common.Routing;

namespace DynamicDave.Umbraco.TranslationHealth.Controllers;

// No policy on the class: each action declares its own (dictionary, content browse or content update access).
[ApiController]
[BackOfficeRoute("dynamicdave-translationhealth/api/v{version:apiVersion}")]
[MapToApi(Constants.ApiName)]
public class DynamicDaveUmbracoTranslationHealthApiControllerBase : ControllerBase
{
    protected StatusCodeResult Forbidden() => StatusCode(StatusCodes.Status403Forbidden);

    protected BadRequestObjectResult Error(string code) => BadRequest(new ErrorResponse { Error = code });

    protected NotFoundObjectResult NotFoundError(string code) => NotFound(new ErrorResponse { Error = code });

    protected ConflictObjectResult ConflictError(string code) => Conflict(new ErrorResponse { Error = code });
}
