namespace MuCustom.PlugIns.Common;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// The settings of the MuCustom features. They are the custom configuration of the
/// <see cref="MuCustomCorePlugIn"/> and can be edited in the admin panel (Plugins).
/// </summary>
public class MuCustomSettings
{
    /// <summary>
    /// Gets the settings which are in use.
    /// </summary>
    public static MuCustomSettings Current { get; internal set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether <c>/recargar</c> credits fichas without a payment.
    /// Only for testing: disable it on a public server.
    /// </summary>
    [Display(Name = "Recarga simulada (/recargar)", Description = "Acredita fichas sin pago real. SOLO PARA PRUEBAS: desactivar en un servidor publico.")]
    public bool SimulatedRechargeEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether chaos machine results of +10 and higher are announced.
    /// </summary>
    [Display(Name = "Anunciar Chaos Machine", Description = "Anuncia a todo el servidor las mejoras +10 a +15 (exito y destruccion).")]
    public bool AnnounceChaosUpgrades { get; set; } = true;

    /// <summary>
    /// Gets or sets the minimum target level of an announced item upgrade.
    /// </summary>
    [Display(Name = "Nivel minimo anunciado", Description = "Nivel objetivo minimo de una mejora para anunciarla (por defecto +10).")]
    public int AnnouncedMinimumUpgradeLevel { get; set; } = 10;

    /// <summary>
    /// Gets or sets a value indicating whether kills of world bosses are announced.
    /// </summary>
    [Display(Name = "Anunciar jefes de mundo", Description = "Anuncia a todo el servidor quien mato a un jefe de mundo y en que mapa.")]
    public bool AnnounceWorldBosses { get; set; } = true;

    /// <summary>
    /// Gets or sets the numbers of the monsters which are world bosses.
    /// </summary>
    [Display(Name = "Jefes de mundo", Description = "Numeros de los monstruos que se anuncian al morir.")]
    public List<short> WorldBossMonsterNumbers { get; set; } =
    [
        38, // Balrog
        43, // Golden Budge Dragon
        44, // Red Dragon
        53, // Golden Titan
        54, // Golden Soldier
        78, // Golden Goblin
        79, // Golden Dragon
        80, // Golden Lizard King
        81, // Golden Vepar
        82, // Golden Tantallos
        83, // Golden Wheel
        236, // Golden Archer
        275, // Illusion of Kundun 7 (Kundun)
        295, // Erohim
        459, // Selupan
    ];

    /// <summary>
    /// Gets or sets a value indicating whether the VIP status is shown when a character enters the world.
    /// </summary>
    [Display(Name = "Mostrar estado VIP al entrar", Description = "Muestra nivel VIP, vencimiento y saldo de fichas al entrar al juego.")]
    public bool ShowVipStatusOnEnter { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the daily quests count the kills of the party members on the same map.
    /// </summary>
    [Display(Name = "Diarias en party", Description = "Las muertes cuentan para los miembros de la party que estan en el mismo mapa.")]
    public bool DailyQuestsShareWithParty { get; set; } = true;
}
