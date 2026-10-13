namespace MuCustom.PlugIns.Common;

using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.Persistence;

/// <summary>
/// The database identifiers of OpenMU entities. The data model doesn't expose them; the
/// persistent classes implement <see cref="IIdentifiable"/>.
/// </summary>
public static class EntityIds
{
    /// <summary>Gets the identifier of the account.</summary>
    /// <param name="account">The account.</param>
    /// <returns>The identifier.</returns>
    public static Guid GetId(this Account account) => GetIdOf(account);

    /// <summary>Gets the identifier of the character.</summary>
    /// <param name="character">The character.</param>
    /// <returns>The identifier.</returns>
    public static Guid GetId(this Character character) => GetIdOf(character);

    /// <summary>Gets the identifier of the item storage.</summary>
    /// <param name="storage">The item storage.</param>
    /// <returns>The identifier.</returns>
    public static Guid GetId(this ItemStorage storage) => GetIdOf(storage);

    private static Guid GetIdOf(object entity)
    {
        return entity is IIdentifiable identifiable
            ? identifiable.Id
            : throw new InvalidOperationException($"{entity.GetType().Name} has no database identifier.");
    }
}
