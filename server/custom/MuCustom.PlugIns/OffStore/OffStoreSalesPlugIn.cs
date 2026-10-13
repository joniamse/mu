namespace MuCustom.PlugIns.OffStore;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// After a sale in a personal store: saves seller and buyer at once and counts the sale of an offline store.
/// </summary>
/// <remarks>
/// OpenMU writes the sold item right away, but the zen of both players only with their next save.
/// Without saving now, a crash right after a sale would give the buyer the item and keep its zen.
/// </remarks>
[PlugIn]
[Display(Name = "MuCustom: ventas en tiendas", Description = "Guarda vendedor y comprador tras cada venta en una tienda personal y cuenta las ventas de las tiendas offline.")]
[Guid("E2C47B91-6A3D-4F58-9B10-3D8E5C7A2F64")]
public class OffStoreSalesPlugIn : IItemSoldToOtherPlayerPlugIn
{
    /// <inheritdoc />
    public void ItemSold(Player seller, Item item, Player buyer)
    {
        // Called inside the store lock of the sale: the saving runs after it.
        _ = Task.Run(async () =>
        {
            try
            {
                await OffStoreService.OnItemSoldAsync(seller, buyer).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                seller.Logger.LogError(ex, "MuCustom: could not save after the sale of {Item} by {Seller}.", item, seller.Name);
            }
        });
    }
}
