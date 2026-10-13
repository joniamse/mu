namespace MuCustom.PlugIns.Common;

using MuCustom.PlugIns.Data;
using MUnique.OpenMU.GameLogic;

/// <summary>
/// Helpers for the chat commands.
/// </summary>
internal static class ChatCommands
{
    /// <summary>
    /// Gets the arguments of a chat command, without the command itself.
    /// </summary>
    /// <param name="command">The whole chat message, e.g. <c>/vip comprar 2</c>.</param>
    /// <returns>The arguments, e.g. <c>comprar</c> and <c>2</c>.</returns>
    public static string[] Arguments(string command)
    {
        return command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1).ToArray();
    }

    /// <summary>
    /// Checks that the features can be used now, and tells the player if not.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns><c>true</c> if the database is ready and the player is in the game.</returns>
    public static async ValueTask<bool> CheckReadyAsync(Player player)
    {
        if (!CustomDatabase.IsReady)
        {
            await player.ShowBlueMessageAsync("El sistema aun se esta iniciando; intentalo en unos segundos.").ConfigureAwait(false);
            return false;
        }

        return player.Account is not null && player.SelectedCharacter is not null;
    }

    /// <summary>
    /// Runs a command and tells the player about unexpected errors instead of failing silently.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="command">The command name for the log.</param>
    /// <param name="action">The action.</param>
    /// <returns>The task.</returns>
    public static async ValueTask RunAsync(Player player, string command, Func<ValueTask> action)
    {
        try
        {
            if (await CheckReadyAsync(player).ConfigureAwait(false))
            {
                await action().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            player.Logger.LogError(ex, "MuCustom: error in the command {Command} of {Player}.", command, player.Name);
            await player.ShowBlueMessageAsync("Ocurrio un error; intentalo de nuevo mas tarde.").ConfigureAwait(false);
        }
    }
}
