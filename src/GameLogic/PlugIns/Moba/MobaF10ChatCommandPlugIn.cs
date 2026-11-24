// <copyright file="MobaF10ChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>Arguments for <c>/mobaf10 [action] [key] [value]</c>.</summary>
public class MobaF10ChatCommandArgs : ArgumentsBase
{
    /// <summary>Gets or sets the action: on, off, status (default), dmg, hp or psi.</summary>
    [Argument("action", false)]
    public string? Action { get; set; }

    /// <summary>Gets or sets the key (the family alias for psi: wiz, kn, elf, mg, dl, sum, rf) or the value of dmg / hp.</summary>
    [Argument("key", false)]
    public string? Key { get; set; }

    /// <summary>Gets or sets the value of psi.</summary>
    [Argument("value", false)]
    public string? Value { get; set; }
}

/// <summary>
/// GM command <c>/mobaf10 [on|off|status|dmg v|hp v|psi family v]</c>: switches the F10 balance model (<see cref="MobaF10"/>) and tunes its global scales and
/// class offsets at runtime, so the duel calibration does not need a rebuild per step.
/// </summary>
[Guid("7B1C4E92-3D58-4A6F-9E21-5C8A0D7F3B64")]
[PlugIn]
[Display(Name = "MOBA: F10 balance model (GM)", Description = "'/mobaf10 [on|off|status|dmg v|hp v|psi family v]' - switches and tunes the F10 balance model.")]
[ChatCommandHelp(Command, "GM test: switches and tunes the F10 balance model: /mobaf10 [on|off|status|dmg v|hp v|psi family v].", typeof(MobaF10ChatCommandArgs))]
public class MobaF10ChatCommandPlugIn : ChatCommandPlugInBase<MobaF10ChatCommandArgs>
{
    private const string Command = "/mobaf10";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, MobaF10ChatCommandArgs arguments)
    {
        var action = arguments.Action?.Trim().ToLowerInvariant() ?? "status";
        string message;
        switch (action)
        {
            case "on":
                MobaF10.Enabled = true;
                message = "F10 ACTIVADO (daño, mitigación y vida del modelo de conservación).";
                break;
            case "off":
                MobaF10.Enabled = false;
                message = "F10 apagado (mezcla 45/20/35 de siempre).";
                break;
            case "rush" when arguments.Key is "on" or "off":
                MobaRush.Enabled = arguments.Key == "on";
                message = $"Rush (skill 638, el 813 de S21; acercamiento del Blade Knight): {(MobaRush.Enabled ? "ACTIVO" : "apagado")}";
                break;
            case "rushblow" when double.TryParse(arguments.Key, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rb):
                MobaRush.BlowFactor = Math.Clamp(rb, 0.1, 10);
                message = FormattableString.Invariant($"Rush: factor del golpe = {MobaRush.BlowFactor}");
                break;
            case "rushcd" when double.TryParse(arguments.Key, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rc):
                MobaRush.CooldownSeconds = Math.Clamp(rc, 1, 120);
                message = FormattableString.Invariant($"Rush: enfriamiento = {MobaRush.CooldownSeconds} s");
                break;
            case "caps" when arguments.Key is "on" or "off":
                MobaF10.HitCapAndBrake = arguments.Key == "on";
                message = $"tope por golpe y freno de ráfaga: {(MobaF10.HitCapAndBrake ? "ACTIVOS" : "APAGADOS")} bajo F10";
                break;
            case "canales" when arguments.Key is "on" or "off":
                MobaF10Ledger.Enabled = arguments.Key == "on";
                message = $"libro de canales (maná, sostén, SD, anticura): {(MobaF10Ledger.Enabled ? "ACTIVO" : "apagado")}";
                break;
            case "horizonte" when double.TryParse(arguments.Key, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t):
                MobaF10Ledger.Horizon = Math.Clamp(t, 1, 120);
                message = FormattableString.Invariant($"horizonte de pelea T = {MobaF10Ledger.Horizon} s");
                break;
            case "audit":
                var (orphans, pending) = MobaF10Ledger.Audit();
                message = FormattableString.Invariant($"auditoría: {orphans.Count} huérfanos {(orphans.Count > 0 ? string.Join(", ", orphans) : string.Empty)} | {pending.Count} pendientes de peso: {string.Join("; ", pending.Select(p => p.Split(" -> ")[0]))}");
                break;
            case "psit" when ResolveFamily(arguments.Key) is { } tierFamily && double.TryParse(arguments.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var vt):
                MobaF10.SetPsiTier(tierFamily, Math.Clamp(vt, -3, 3));
                message = FormattableString.Invariant($"psi con ítems {tierFamily} = {MobaF10.PsiTier(tierFamily)}");
                break;
            case "dmg" when double.TryParse(arguments.Key, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d):
                MobaF10.DamageScale = Math.Clamp(d, 0.01, 100);
                message = FormattableString.Invariant($"escala global de daño = {MobaF10.DamageScale}");
                break;
            case "hp" when double.TryParse(arguments.Key, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h):
                MobaF10.HealthScale = Math.Clamp(h, 0.01, 100);
                message = FormattableString.Invariant($"escala global de vida = {MobaF10.HealthScale}");
                break;
            case "psi" when ResolveFamily(arguments.Key) is { } family && double.TryParse(arguments.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v):
                MobaF10.SetPsi(family, Math.Clamp(v, -3, 3));
                message = FormattableString.Invariant($"psi {family} = {MobaF10.Psi(family)}");
                break;
            default:
                var psis = string.Join(' ', Enum.GetValues<MobaFamily>().Select(f => FormattableString.Invariant($"{f}={MobaF10.Psi(f):F2}")));
                message = FormattableString.Invariant($"F10 {(MobaF10.Enabled ? "ACTIVO" : "apagado")} | dmg x{MobaF10.DamageScale} hp x{MobaF10.HealthScale} | tope+freno {(MobaF10.HitCapAndBrake ? "on" : "off")} | psi {psis} | tú: {MobaF10.Describe(player)}");
                break;
        }

        await player.ShowBlueMessageAsync($"[mobaf10] {message}").ConfigureAwait(false);
    }

    private static MobaFamily? ResolveFamily(string? alias) => alias?.Trim().ToLowerInvariant() switch
    {
        "wiz" or "gm" or "sm" or "dw" => MobaFamily.Wizard,
        "kn" or "bk" or "dk" => MobaFamily.Knight,
        "elf" or "fe" => MobaFamily.Elf,
        "mg" => MobaFamily.MagicGladiator,
        "dl" => MobaFamily.DarkLord,
        "sum" => MobaFamily.Summoner,
        "rf" => MobaFamily.RageFighter,
        _ => null,
    };
}
