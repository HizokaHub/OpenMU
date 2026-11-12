// <copyright file="MobaStressChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// GM command <c>/mobastress &lt;n&gt; [seconds] [aoe=K]</c>: fills a 50x50 window of the arena with <c>n</c> real lane creeps (half
/// per team), keeps them alive for the given time and logs how the server copes (see <see cref="MobaStress"/>).
/// <c>/mobastress stop</c> ends it, <c>/mobastress estado</c> describes it.
/// </summary>
[Guid("D4C81B7A-6E25-4F93-A0B1-7C3E92F5D816")]
[PlugIn]
[Display(Name = "MOBA: stress test", Description = "GM command '/mobastress <n> [segundos] [aoe=K] | stop | estado' - creep stress test in a 50x50 window of the arena.")]
[ChatCommandHelp(Command, "Prueba de estrés de criaturas en una arena de 50x50: /mobastress <n 10-400> [segundos 5-600] [aoe=K] | stop | estado", null, CharacterStatus.GameMaster)]
public class MobaStressChatCommandPlugIn : IChatCommandPlugIn
{
    private const string Command = "/mobastress";

    private const string Usage = "Uso: /mobastress <criaturas 10-400> [segundos 5-600] [aoe=0-8] | stop | estado. Entra primero con /moba y sin partida en curso.";

    /// <inheritdoc />
    public string Key => Command;

    /// <inheritdoc />
    public CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    public async ValueTask HandleCommandAsync(Player player, string command)
    {
        var args = MobaStressPlan.Parse(command);
        if (args.Verb == MobaStressVerb.Help)
        {
            await Tell(player, Usage).ConfigureAwait(false);
            return;
        }

        if (args.Verb == MobaStressVerb.Invalid)
        {
            await Tell(player, args.Error ?? Usage).ConfigureAwait(false);
            return;
        }

        if (!(player.SelectedCharacter?.CharacterStatus >= CharacterStatus.GameMaster))
        {
            await Tell(player, "Solo un GM puede usar la prueba de estrés.").ConfigureAwait(false);
            return;
        }

        switch (args.Verb)
        {
            case MobaStressVerb.Stop:
                await Tell(player, await MobaStress.StopAsync().ConfigureAwait(false)).ConfigureAwait(false);
                return;
            case MobaStressVerb.Status:
                await Tell(player, MobaStress.Describe()).ConfigureAwait(false);
                return;
        }

        if (player.CurrentMap is not { } map || map.MapId != MobaCloneFactory.ArenaMapNumber)
        {
            await Tell(player, "Solo en el mapa MOBA (200). Entra con /moba.").ConfigureAwait(false);
            return;
        }

        if (MobaStress.IsRunning)
        {
            await Tell(player, "Ya hay una prueba en marcha: /mobastress stop").ConfigureAwait(false);
            return;
        }

        var world = await MobaStress.BuildWorldAsync(player, map).ConfigureAwait(false);
        if (MobaStressPlan.BlockReason(world) is { } reason)
        {
            await Tell(player, reason).ConfigureAwait(false);
            return;
        }

        if (!player.IsMobaClone)
        {
            await Tell(player, "Sin /moba no recibes el tráfico del mapa completo; la medición de red no será representativa.").ConfigureAwait(false);
        }

        foreach (var line in await MobaStress.StartAsync(player, map, args).ConfigureAwait(false))
        {
            await Tell(player, line).ConfigureAwait(false);
        }
    }

    private static ValueTask Tell(Player player, string text) => player.ShowBlueMessageAsync($"[STRESS] {text}");
}
