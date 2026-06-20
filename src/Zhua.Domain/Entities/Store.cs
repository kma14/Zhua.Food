using Zhua.Domain.Enums;

namespace Zhua.Domain.Entities;

/// <summary>A physical supermarket location. Prices are modelled per physical store.</summary>
public class Store
{
    public Guid Id { get; set; }

    public Chain Chain { get; set; }

    public required string Name { get; set; }

    public required string Suburb { get; set; }

    /// <summary>Geolocation used to select this physical store on the source site (plan D2/§10).</summary>
    public double Latitude { get; set; }

    public double Longitude { get; set; }

    /// <summary>Source-site store id where one exists (e.g. Woolworths, via its locator API). Null until known.</summary>
    public string? ExternalStoreId { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<StoreProduct> StoreProducts { get; } = new List<StoreProduct>();

    public ICollection<CrawlRun> CrawlRuns { get; } = new List<CrawlRun>();
}
