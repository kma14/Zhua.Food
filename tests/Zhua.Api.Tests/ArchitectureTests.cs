using NetArchTest.Rules;
using Zhua.Api.Controllers;
using Zhua.Domain.Entities;
using Zhua.Infrastructure.Persistence;

namespace Zhua.Api.Tests;

/// <summary>
/// Executable enforcement of the Clean Architecture boundary (D27) — the first-class guard so the rule can't be
/// silently re-violated. If one of these fails, fix the layering, don't relax the test.
/// </summary>
public class ArchitectureTests
{
    private static readonly System.Reflection.Assembly Api = typeof(ProductsController).Assembly;
    private static readonly System.Reflection.Assembly Application = typeof(IProductService).Assembly;
    private static readonly System.Reflection.Assembly Domain = typeof(Product).Assembly;

    [Fact]
    public void Api_controllers_do_not_depend_on_Infrastructure_or_EF()
    {
        var result = Types.InAssembly(Api)
            .That().ResideInNamespace("Zhua.Api.Controllers")
            .ShouldNot().HaveDependencyOnAny(
                "Zhua.Infrastructure", "Microsoft.EntityFrameworkCore", typeof(ZhuaDbContext).FullName)
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Controllers must depend on Application use cases only (no EF/DbContext): " + Names(result));
    }

    [Fact]
    public void Application_does_not_depend_on_Infrastructure_or_EF()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot().HaveDependencyOnAny("Zhua.Infrastructure", "Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(result.IsSuccessful, "Application must not depend on Infrastructure/EF: " + Names(result));
    }

    [Fact]
    public void Domain_depends_on_nothing_outward()
    {
        var result = Types.InAssembly(Domain)
            .ShouldNot().HaveDependencyOnAny(
                "Zhua.Application", "Zhua.Infrastructure", "Zhua.Api",
                "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore")
            .GetResult();

        Assert.True(result.IsSuccessful, "Domain must depend on nothing outward: " + Names(result));
    }

    private static string Names(TestResult r) =>
        r.FailingTypeNames is { } names ? string.Join(", ", names) : "(none)";
}
