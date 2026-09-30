using System.Text.Json;
using Argoscope.Application.Packages;
using Argoscope.Domain.Common;
using Argoscope.Domain.Packages;
using Argoscope.Domain.Portfolios;
using Argoscope.Domain.Repositories;
using Argoscope.Domain.Snapshots;
using Xunit;

namespace Argoscope.UnitTests;

public class IdJsonConverterTests
{
    private static JsonSerializerOptions NewOptions() => new(JsonSerializerDefaults.Web)
    {
        Converters = { new IdJsonConverter() },
    };

    [Fact]
    public void Serialises_Id_T_As_Bare_Guid_String()
    {
        var id = Id<PackageAssociation>.From(Guid.NewGuid());
        var options = NewOptions();

        var json = JsonSerializer.Serialize(id, options);

        Assert.Equal($"\"{id.Value}\"", json);
    }

    [Fact]
    public void Serialises_Record_With_Id_T_As_Bare_Guid_String()
    {
        var id = Id<PackageAssociation>.From(Guid.NewGuid());
        var record = new PackageCollectionRunResult(
            id, 0, 0, ProviderResultStatus.Available, ProviderResultStatus.Available, null, DateTimeOffset.UtcNow);
        var options = NewOptions();

        var json = JsonSerializer.Serialize(record, options);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(id.Value, doc.RootElement.GetProperty("associationId").GetGuid());
    }

    [Fact]
    public void Deserialises_Bare_Guid_String_Back_To_Id_T()
    {
        var id = Id<Repository>.From(Guid.NewGuid());
        var options = NewOptions();

        var json = $"\"{id.Value}\"";
        var parsed = JsonSerializer.Deserialize<Id<Repository>>(json, options);

        Assert.Equal(id, parsed);
    }

    [Fact]
    public void Deserialises_Record_With_Id_T_From_Bare_Guid()
    {
        var id = Id<Portfolio>.From(Guid.NewGuid());
        var json = $"{{\"portfolioId\":\"{id.Value}\",\"name\":\"x\"}}";
        var options = NewOptions();

        var parsed = JsonSerializer.Deserialize<Sample>(json, options);

        Assert.NotNull(parsed);
        Assert.Equal(id, parsed!.PortfolioId);
    }

    public sealed record Sample(Id<Portfolio> PortfolioId, string Name);
}
