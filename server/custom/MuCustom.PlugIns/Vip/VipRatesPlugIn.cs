namespace MuCustom.PlugIns.Vip;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Applies the bonuses of the VIP membership: experience, item drop chance, chaos machine
/// success rate and the MU helper discount.
/// </summary>
[PlugIn]
[Display(Name = "MuCustom: bonus VIP", Description = "Multiplicadores de experiencia, drop, exito en la Chaos Machine y descuento del MU Helper segun el nivel VIP.")]
[Guid("C2A97E15-4B3D-4F60-8D1A-5E7C3B9F2D06")]
public class VipRatesPlugIn : IExperienceCalculationPlugIn, IGameplayRateModifierPlugIn
{
    /// <inheritdoc />
    public ValueTask CalculateExperienceAsync(Player player, ExperienceCalculationArgs args)
    {
        var level = VipService.GetLevel(player);
        if (level.ExperienceBonus > 0)
        {
            args.Experience *= 1 + level.ExperienceBonus;
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void ModifyRate(Player player, GameplayRateArgs args)
    {
        var level = VipService.GetLevel(player);
        if (level.Level == 0)
        {
            return;
        }

        switch (args.Kind)
        {
            case GameplayRateKind.ItemDropChanceMultiplier:
                args.Value *= 1 + level.DropBonus;
                break;
            case GameplayRateKind.CraftingSuccessRate:
                // Only a crafting which can fail gets the bonus; a sure one stays as it is.
                if (args.Value is > 0 and < 100)
                {
                    args.Value += level.ChaosBonus;
                }

                break;
            case GameplayRateKind.MuHelperZenCostMultiplier:
                args.Value *= Math.Max(0, 1 - level.HelperDiscount);
                break;
        }
    }
}
