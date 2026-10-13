namespace MuCustom.PlugIns.Vip;

using MuCustom.PlugIns.Data;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlayerActions.MiniGames;
using MUnique.OpenMU.GameLogic.Views.Inventory;
using Npgsql;
using NpgsqlTypes;

/// <summary>
/// The daily Blood Castle and Devil Square entries of a membership (<c>/entradas</c>).
/// An entry is a ticket of each event (Invisibility Cloak and Devil's Invitation) for the
/// level of the character, so the events work exactly as with bought tickets.
/// </summary>
public static class EventEntryService
{
    /// <summary>
    /// Gives the entries of today which weren't claimed yet.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>The message for the player.</returns>
    public static async ValueTask<string> ClaimAsync(Player player)
    {
        if (player.SelectedCharacter is not { } character || player.Inventory is not { } inventory)
        {
            return "No hay un personaje seleccionado.";
        }

        var allowed = VipService.GetLevel(player).DailyEventEntries;
        if (allowed <= 0)
        {
            return "Las entradas diarias a Blood Castle y Devil Square son un beneficio VIP (/vip).";
        }

        var bloodCastle = player.GetSuitableMiniGameDefinition(MiniGameType.BloodCastle, 1);
        var devilSquare = player.GetSuitableMiniGameDefinition(MiniGameType.DevilSquare, 1);
        var events = new[] { bloodCastle, devilSquare }
            .Where(d => d?.TicketItem is not null && d.IsInLevelRange(player))
            .Cast<MiniGameDefinition>()
            .ToList();
        if (events.Count == 0)
        {
            return "Tu nivel aun no permite entrar a Blood Castle ni a Devil Square.";
        }

        var period = DateOnly.FromDateTime(DateTime.UtcNow);
        var given = 0;
        while (given < allowed && await TryTakeEntryAsync(character.GetId(), period, allowed).ConfigureAwait(false))
        {
            var items = new List<Item>();
            foreach (var definition in events)
            {
                var item = player.PersistenceContext.CreateNew<Item>();
                item.Definition = definition.TicketItem;
                item.Level = (byte)definition.TicketItemLevel;
                item.Durability = 1;
                if (!await inventory.AddItemAsync(item).ConfigureAwait(false))
                {
                    await player.PersistenceContext.DeleteAsync(item).ConfigureAwait(false);
                    foreach (var added in items)
                    {
                        await inventory.RemoveItemAsync(added).ConfigureAwait(false);
                        await player.PersistenceContext.DeleteAsync(added).ConfigureAwait(false);
                    }

                    await GiveBackEntryAsync(character.GetId(), period).ConfigureAwait(false);
                    var full = "Tu inventario esta lleno; libera espacio y vuelve a usar /entradas.";
                    return given > 0 ? $"Recibiste {given} entrada(s). {full}" : full;
                }

                items.Add(item);
            }

            foreach (var item in items)
            {
                await player.InvokeViewPlugInAsync<IItemAppearPlugIn>(p => p.ItemAppearAsync(item)).ConfigureAwait(false);
            }

            given++;
        }

        var names = string.Join(" y ", events.Select(e => e.Type == MiniGameType.BloodCastle ? "Blood Castle" : "Devil Square"));
        return given > 0
            ? $"Recibiste {given} entrada(s) de {names}. Vuelven a estar disponibles a las 00:00 UTC."
            : "Ya reclamaste las entradas de hoy. Vuelven a estar disponibles a las 00:00 UTC.";
    }

    private static async ValueTask<bool> TryTakeEntryAsync(Guid characterId, DateOnly period, int allowed)
    {
        await using var connection = await CustomDatabase.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO vip_event_entry_claims (character_id, period_date, claimed) VALUES (@c, @p, 1)
            ON CONFLICT (character_id, period_date)
            DO UPDATE SET claimed = vip_event_entry_claims.claimed + 1 WHERE vip_event_entry_claims.claimed < @max
            RETURNING claimed;
            """,
            connection);
        command.Parameters.AddWithValue("c", characterId);
        command.Parameters.AddWithValue("p", NpgsqlDbType.Date, period);
        command.Parameters.AddWithValue("max", (short)allowed);
        return await command.ExecuteScalarAsync().ConfigureAwait(false) is short claimed && claimed <= allowed;
    }

    private static async ValueTask GiveBackEntryAsync(Guid characterId, DateOnly period)
    {
        await using var connection = await CustomDatabase.OpenAsync().ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "UPDATE vip_event_entry_claims SET claimed = GREATEST(claimed - 1, 0) WHERE character_id = @c AND period_date = @p;",
            connection);
        command.Parameters.AddWithValue("c", characterId);
        command.Parameters.AddWithValue("p", NpgsqlDbType.Date, period);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
