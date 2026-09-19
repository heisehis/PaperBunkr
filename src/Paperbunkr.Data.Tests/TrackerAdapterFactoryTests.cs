using Microsoft.EntityFrameworkCore;
using Paperbunkr.Data.Credentials;
using Paperbunkr.Data.Entities;
using Paperbunkr.Data.Tracking;
using Xunit;

namespace Paperbunkr.Data.Tests;

/// <summary>
/// Exercises <see cref="TrackerAdapterFactory"/>/<see cref="TrackerProviderMap"/> - the single
/// source of truth that replaced four duplicated per-service switches whose silent omissions
/// caused a real "MangaDex never synced" bug.
/// </summary>
public class TrackerAdapterFactoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DbContextOptions<PaperbunkrDbContext> _dbOptions;

    public TrackerAdapterFactoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"paperbunkr_traderfactory_test_{Guid.NewGuid():N}.db");
        _dbOptions = new DbContextOptionsBuilder<PaperbunkrDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var context = new PaperbunkrDbContext(_dbOptions);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch (IOException) { }
    }

    [Fact]
    public void EverySupportedService_HasAnAdapter_WithMatchingServiceId()
    {
        foreach (var service in TrackerAdapterFactory.SupportedServices)
        {
            var adapter = TrackerAdapterFactory.CreateAdapter(service);
            Assert.NotNull(adapter);
            Assert.Equal(service, adapter!.Service);
            Assert.True(TrackerAdapterFactory.IsSupported(service));
        }
    }

    [Theory]
    [InlineData(TrackingService.Metron)]
    [InlineData(TrackingService.ComicVine)]
    public void UnsupportedServices_HaveNoAdapter_AndAreNeverConnected(TrackingService service)
    {
        using var context = new PaperbunkrDbContext(_dbOptions);

        Assert.Null(TrackerAdapterFactory.CreateAdapter(service));
        Assert.False(TrackerAdapterFactory.IsSupported(service));
        Assert.False(TrackerAdapterFactory.IsConnected(context, service));
    }

    [Fact]
    public void IsConnected_IsFalseUntilACredentialIsStored_ForEverySupportedService()
    {
        using var context = new PaperbunkrDbContext(_dbOptions);
        var kinds = new Dictionary<TrackingService, CredentialKind>
        {
            [TrackingService.Bangumi] = CredentialKind.ApiKey,
            [TrackingService.MangaBaka] = CredentialKind.ApiKey,
        };

        foreach (var service in TrackerAdapterFactory.SupportedServices)
        {
            Assert.False(TrackerAdapterFactory.IsConnected(context, service));
            CredentialStore.Set(context, service.ToString(), kinds.GetValueOrDefault(service, CredentialKind.OAuthAccessToken), "tok");
            Assert.True(TrackerAdapterFactory.IsConnected(context, service));
        }
    }

    [Fact]
    public void ProviderMap_RoundTripsTheSevenSharedServices_AndNothingElse()
    {
        foreach (var service in TrackerAdapterFactory.SupportedServices)
        {
            var provider = TrackerProviderMap.ToMetadataProvider(service);
            if (service == TrackingService.Bangumi)
            {
                Assert.Null(provider);
                continue;
            }

            Assert.NotNull(provider);
            Assert.Equal(service, TrackerProviderMap.ToTrackingService(provider!.Value));
        }

        Assert.Null(TrackerProviderMap.ToTrackingService(ExternalMetadataProvider.AnimePlanet));
    }
}
