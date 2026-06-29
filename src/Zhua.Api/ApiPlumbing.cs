using Microsoft.AspNetCore.Mvc;
using Zhua.Application.Common;
using Zhua.Domain.Enums;

namespace Zhua.Api;

/// <summary>Base controller that maps an Application <see cref="Result{T}"/> to the matching HTTP response (D27).</summary>
public abstract class ZhuaController : ControllerBase
{
    protected IActionResult Respond<T>(Result<T> r) => r.Status switch
    {
        ResultStatus.Ok => Ok(r.Value),
        ResultStatus.Created => StatusCode(StatusCodes.Status201Created, r.Value),
        ResultStatus.NotFound => NotFound(new { error = r.Error }),
        ResultStatus.Conflict => Conflict(new { error = r.Error }),
        ResultStatus.BadRequest => BadRequest(new { error = r.Error }),
        _ => StatusCode(StatusCodes.Status500InternalServerError),
    };
}

/// <summary>Parses the <c>?supermarket=</c> query param to a <see cref="Chain"/> (a transport concern — the use case takes the enum).</summary>
internal static class SupermarketFilter
{
    /// <returns><c>false</c> only when a non-null value didn't parse (the caller returns 400).</returns>
    public static bool TryParse(string? supermarket, out Chain? chain)
    {
        if (supermarket is null) { chain = null; return true; }
        if (Enum.TryParse<Chain>(supermarket, ignoreCase: true, out var c)) { chain = c; return true; }
        chain = null;
        return false;
    }
}
