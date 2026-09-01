// <copyright file="MakeCharChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Dev/test chat command <c>/makechar &lt;class&gt; &lt;name&gt;</c>: adds a character of
/// any class to the caller's own account, ignoring the normal "can this class be created"
/// gate (so a Rage Fighter / 3rd-class char can be made for MOBA testing). The character
/// shows up at the character-selection screen after a relog.
/// </summary>
[Guid("7F3C1A96-2E84-4D0B-9C57-6A1B8E2F4D30")]
[PlugIn]
[Display(Name = "MOBA: make character command", Description = "Dev command '/makechar <class> <name>' - create a character of any class on your account.")]
[ChatCommandHelp(Command, "Create a character of any class on your account: /makechar <rf|sum|dw|...> <name>", typeof(MakeCharChatCommandArgs))]
public class MakeCharChatCommandPlugIn : ChatCommandPlugInBase<MakeCharChatCommandArgs>
{
    private const string Command = "/makechar";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, MakeCharChatCommandArgs arguments)
    {
        if (player.IsMobaClone)
        {
            await player.ShowBlueMessageAsync("[makechar] Salí del MOBA primero (/mobaleave).").ConfigureAwait(false);
            return;
        }

        var account = player.Account;
        if (account is null)
        {
            return;
        }

        var name = arguments.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            await player.ShowBlueMessageAsync("[makechar] Uso: /makechar <clase> <nombre>").ConfigureAwait(false);
            return;
        }

        var classNumber = arguments.ResolveClassNumber();
        if (classNumber is null)
        {
            await player.ShowBlueMessageAsync("[makechar] Clase desconocida. Ej: rf, sum, dw, dk, fe, mg, dl.").ConfigureAwait(false);
            return;
        }

        var config = player.GameContext.Configuration;
        var characterClass = config.CharacterClasses.FirstOrDefault(c => c.Number == classNumber.Value);
        if (characterClass is null)
        {
            await player.ShowBlueMessageAsync($"[makechar] La config no tiene la clase {classNumber}.").ConfigureAwait(false);
            return;
        }

        if (account.Characters.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            await player.ShowBlueMessageAsync("[makechar] Ya tenés un personaje con ese nombre.").ConfigureAwait(false);
            return;
        }

        var usedSlots = account.Characters.Select(c => (int)c.CharacterSlot).ToHashSet();
        byte? freeSlot = null;
        for (var i = 0; i < config.MaximumCharactersPerAccount; i++)
        {
            if (!usedSlots.Contains(i))
            {
                freeSlot = (byte)i;
                break;
            }
        }

        if (freeSlot is null)
        {
            await player.ShowBlueMessageAsync("[makechar] No hay slots de personaje libres.").ConfigureAwait(false);
            return;
        }

        var ctx = player.PersistenceContext;
        var character = ctx.CreateNew<Character>();
        character.CharacterClass = characterClass;
        character.Name = name;
        character.CharacterSlot = freeSlot.Value;
        character.CreateDate = DateTime.UtcNow;
        character.LevelUpPoints = 0;

        // Bind Q to healing potion, W to mana potion; E/R unbound (see CreateCharacterAction).
        var keyConfiguration = new byte[30];
        keyConfiguration[21] = 1;
        keyConfiguration[22] = 4;
        keyConfiguration[23] = 0xFF;
        keyConfiguration[25] = 0xFF;
        character.KeyConfiguration = keyConfiguration;

        foreach (var statAttribute in characterClass.StatAttributes)
        {
            character.Attributes.Add(ctx.CreateNew<StatAttribute>(statAttribute.Attribute, statAttribute.BaseValue));
        }

        var homeMap = characterClass.HomeMap ?? config.Maps.FirstOrDefault(m => m.Number == 0);
        character.CurrentMap = homeMap;
        var spawnGate = homeMap?.ExitGates.FirstOrDefault(g => g.IsSpawnGate) ?? homeMap?.ExitGates.FirstOrDefault();
        if (spawnGate is not null)
        {
            character.PositionX = (byte)((spawnGate.X1 + spawnGate.X2) / 2);
            character.PositionY = (byte)((spawnGate.Y1 + spawnGate.Y2) / 2);
        }

        character.Inventory = ctx.CreateNew<ItemStorage>();
        account.Characters.Add(character);

        // Lets the normal "new character" plug-ins add the class starting items / skills.
        player.GameContext.PlugInManager.GetPlugInPoint<ICharacterCreatedPlugIn>()?.CharacterCreated(player, character);

        try
        {
            await player.SaveProgressAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            account.Characters.Remove(character);
            ctx.Detach(character);
            await player.ShowBlueMessageAsync($"[makechar] No se pudo guardar: {ex.InnerException?.Message ?? ex.Message}").ConfigureAwait(false);
            return;
        }

        await player.ShowBlueMessageAsync($"[makechar] '{name}' ({characterClass.Name}) creado en el slot {freeSlot}. Relogueá para verlo.").ConfigureAwait(false);
    }
}
