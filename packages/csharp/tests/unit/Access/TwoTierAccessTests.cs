using Broker.Services;

namespace Unit.Tests.Access;

public class TwoTierAccessTests
{
    [Fact]
    public void Default_permissions_are_tier1_only()
    {
        var p = HighLevelUserPermissions.CreateDefault();

        p.AllowQuery.Should().BeTrue();
        p.AllowTransport.Should().BeTrue();
        p.AllowProduction.Should().BeFalse();
        p.AllowBrowserDelegated.Should().BeFalse();
        p.AllowDeployment.Should().BeFalse();
    }

    [Fact]
    public void EffectiveForTier_basic_masks_tier2_even_when_assigned()
    {
        var p = new HighLevelUserPermissions
        {
            AllowQuery = true,
            AllowTransport = true,
            AllowProduction = true,
            AllowBrowserDelegated = true,
            AllowDeployment = true,
        };

        var eff = p.EffectiveForTier(HighLevelAccessTier.Basic);

        eff.AllowQuery.Should().BeTrue();
        eff.AllowTransport.Should().BeTrue();
        eff.AllowProduction.Should().BeFalse();
        eff.AllowBrowserDelegated.Should().BeFalse();
        eff.AllowDeployment.Should().BeFalse();
    }

    [Fact]
    public void EffectiveForTier_member_passes_assigned_tier2()
    {
        var p = new HighLevelUserPermissions { AllowProduction = true, AllowDeployment = true };

        var eff = p.EffectiveForTier(HighLevelAccessTier.Member);

        eff.AllowProduction.Should().BeTrue();
        eff.AllowDeployment.Should().BeTrue();
        eff.AllowBrowserDelegated.Should().BeFalse(); // not assigned
    }

    [Fact]
    public void EffectiveForTier_member_does_not_auto_grant_unassigned_tier2()
    {
        var p = HighLevelUserPermissions.CreateDefault();

        var eff = p.EffectiveForTier(HighLevelAccessTier.Member);

        eff.AllowQuery.Should().BeTrue();
        eff.AllowProduction.Should().BeFalse();
        eff.AllowBrowserDelegated.Should().BeFalse();
        eff.AllowDeployment.Should().BeFalse();
    }

    [Fact]
    public void EffectiveForTier_null_or_unknown_tier_is_basic()
    {
        var p = new HighLevelUserPermissions { AllowProduction = true };

        p.EffectiveForTier(null).AllowProduction.Should().BeFalse();
        p.EffectiveForTier("nonsense").AllowProduction.Should().BeFalse();
    }

    [Fact]
    public void Normalize_maps_member_else_basic()
    {
        HighLevelAccessTier.Normalize("member").Should().Be(HighLevelAccessTier.Member);
        HighLevelAccessTier.Normalize("MEMBER").Should().Be(HighLevelAccessTier.Member);
        HighLevelAccessTier.Normalize(null).Should().Be(HighLevelAccessTier.Basic);
        HighLevelAccessTier.Normalize("x").Should().Be(HighLevelAccessTier.Basic);
    }

    [Fact]
    public void New_user_profile_defaults_to_basic_tier()
    {
        new HighLevelUserProfile().AccessTier.Should().Be(HighLevelAccessTier.Basic);
    }
}
