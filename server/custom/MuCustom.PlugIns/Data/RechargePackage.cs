namespace MuCustom.PlugIns.Data;

/// <summary>
/// A package of fichas which can be bought (row of <c>shop_recharge_packages</c>).
/// </summary>
/// <param name="Id">The identifier.</param>
/// <param name="Code">The code.</param>
/// <param name="Name">The display name.</param>
/// <param name="PriceUsd">The price in US dollars.</param>
/// <param name="BaseFichas">The fichas without bonus.</param>
/// <param name="BonusFichas">The bonus fichas.</param>
/// <param name="TotalFichas">The credited fichas.</param>
public sealed record RechargePackage(int Id, string Code, string Name, decimal PriceUsd, int BaseFichas, int BonusFichas, int TotalFichas);
