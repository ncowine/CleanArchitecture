using Equipment.Domain;
using Xunit;

namespace CleanArch.UnitTests;

public class EquipmentAssetTests
{
    private static EquipmentAsset Create() =>
        EquipmentAsset.Create(" ThinkPad X1 ", EquipmentCategory.Laptop, " LAP-001 ");

    [Fact]
    public void Create_trims_input_and_starts_available()
    {
        var asset = Create();

        Assert.Equal("ThinkPad X1", asset.Name);
        Assert.Equal("LAP-001", asset.AssetTag);
        Assert.Equal(EquipmentCategory.Laptop, asset.Category);
        Assert.Equal(EquipmentStatus.Available, asset.Status);
        Assert.Null(asset.ReservedForOnboardingRequestId);
    }

    [Theory]
    [InlineData("", "TAG-1")]
    [InlineData("  ", "TAG-1")]
    [InlineData("Name", "")]
    [InlineData("Name", "  ")]
    public void Create_with_invalid_input_throws(string name, string assetTag) =>
        Assert.Throws<DomainException>(() => EquipmentAsset.Create(name, EquipmentCategory.Laptop, assetTag));

    [Fact]
    public void Update_changes_fields()
    {
        var asset = Create();

        asset.Update("ThinkPad X1 Carbon", EquipmentCategory.Other, "LAP-002");

        Assert.Equal("ThinkPad X1 Carbon", asset.Name);
        Assert.Equal(EquipmentCategory.Other, asset.Category);
        Assert.Equal("LAP-002", asset.AssetTag);
    }

    [Fact]
    public void Reserve_marks_reserved_and_records_the_holder()
    {
        var asset = Create();
        var onboardingRequestId = Guid.NewGuid();

        asset.Reserve(onboardingRequestId);

        Assert.Equal(EquipmentStatus.Reserved, asset.Status);
        Assert.Equal(onboardingRequestId, asset.ReservedForOnboardingRequestId);
    }

    [Fact]
    public void Reserving_an_already_reserved_asset_throws()
    {
        var asset = Create();
        asset.Reserve(Guid.NewGuid());

        Assert.Throws<DomainException>(() => asset.Reserve(Guid.NewGuid()));
    }

    [Fact]
    public void Release_returns_the_asset_to_available()
    {
        var asset = Create();
        asset.Reserve(Guid.NewGuid());

        asset.Release();

        Assert.Equal(EquipmentStatus.Available, asset.Status);
        Assert.Null(asset.ReservedForOnboardingRequestId);
    }
}
