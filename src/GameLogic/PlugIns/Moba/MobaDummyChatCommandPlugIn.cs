// <copyright file="MobaDummyChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.Pathfinding;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Dev command <c>/mobadummy [class] [count]</c>: spawns stationary training dummies on the
/// team opposing the caller. A dummy never moves and never attacks, and keeps itself topped
/// up, so every skill can be fired at it while watching the <c>[MOBA-DMG]</c> log.
/// <c>/mobabotclear</c> removes them (they are <see cref="MobaBotPlayer"/>s).
/// </summary>
[Guid("6C2F91A4-3D80-4B17-9E52-8A1F0C7B6D34")]
[PlugIn]
[Display(Name = "MOBA: spawn training dummies", Description = "Dev command '/mobadummy [class] [count]'.")]
[ChatCommandHelp(Command, "Spawn stationary MOBA training dummies: /mobadummy [class] [count]", typeof(MobaDummyChatCommandArgs))]
public class MobaDummyChatCommandPlugIn : ChatCommandPlugInBase<MobaDummyChatCommandArgs>
{
    private const string Command = "/mobadummy";

    /// <summary>Fixed spot on the carved mid lane where dummies line up.</summary>
    private static readonly Point DummyOrigin = new(120, 128);

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, MobaDummyChatCommandArgs arguments)
    {
        // Dummies go on the team opposing the caller so the caller's skills treat them as
        // valid enemy targets. If the caller has no team yet, default them to Blue and the
        // dummies to Red.
        var callerTeam = MobaTeams.GetTeam(player);
        var dummyTeam = callerTeam == MobaTeam.Blue ? MobaTeam.Red
            : callerTeam == MobaTeam.Red ? MobaTeam.Blue
            : MobaTeam.Red;
        if (callerTeam == MobaTeam.None)
        {
            MobaTeams.Set(player, MobaTeam.Blue);
        }

        var classNumber = MobaBotChatCommandPlugIn.ResolveClassNumber(arguments.Class) ?? (byte)6; // Blade Knight
        var count = Math.Clamp(arguments.Count, 1, 10);

        var config = player.GameContext.Configuration;
        var characterClass = config.CharacterClasses.FirstOrDefault(c => c.Number == classNumber);
        if (characterClass is null)
        {
            await player.ShowBlueMessageAsync("[mobadummy] Clase desconocida. Ej: bk, dw, fe, mg, dl, sum, rf.").ConfigureAwait(false);
            return;
        }

        var spawned = 0;
        for (var i = 0; i < count; i++)
        {
            var name = $"dummy{i}";
            var clone = await MobaCloneFactory.BuildForClassAsync(player, characterClass, name).ConfigureAwait(false);
            var account = player.PersistenceContext.CreateNew<Account>();
            account.LoginName = $"#dummy_{name}_{(DateTime.UtcNow.Ticks / 10000) % 100000}";

            var spawn = new Point(
                (byte)Math.Clamp(DummyOrigin.X + ((i % 5) * 2), 5, 250),
                (byte)Math.Clamp(DummyOrigin.Y + ((i / 5) * 2), 5, 250));

            var dummy = new MobaBotPlayer(player.GameContext, dummyTeam, isDummy: true);
            if (await dummy.StartMobaAsync(account, clone, spawn).ConfigureAwait(false))
            {
                spawned++;
            }
        }

        await player.ShowBlueMessageAsync(
            $"[mobadummy] {spawned} dummy(s) {dummyTeam} en la arena ~({DummyOrigin.X},{DummyOrigin.Y}). Mirá con: /move {player.SelectedCharacter?.Name} 200 {DummyOrigin.X} {DummyOrigin.Y}")
            .ConfigureAwait(false);
    }
}
