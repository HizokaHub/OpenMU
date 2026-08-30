// <copyright file="MobaClonePreviewChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands.Arguments;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// GM chat command which builds a MOBA match clone from the caller's real character,
/// reports what it would look like, then discards it - without swapping into it.
/// </summary>
/// <remarks>
/// Test/verification tool for the clone factory (see <see cref="MobaCloneFactory"/>).
/// After running it, relog and confirm the real character (inventory, master level,
/// stats) is untouched.
/// </remarks>
[Guid("9E4B2A17-6D8C-4F03-A1E5-8C2B7F0D4E96")]
[PlugIn]
[Display(Name = "MOBA: clone preview command", Description = "GM command '/mobaclonepreview' - build + report + discard a match clone.")]
[ChatCommandHelp(Command, "Build, report and discard a MOBA match clone of your character.", typeof(EmptyChatCommandArgs))]
public class MobaClonePreviewChatCommandPlugIn : ChatCommandPlugInBase<EmptyChatCommandArgs>
{
    private const string Command = "/mobaclonepreview";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, EmptyChatCommandArgs arguments)
    {
        if (player.SelectedCharacter is not { } real)
        {
            return;
        }

        var realInventoryCount = real.Inventory?.Items.Count ?? 0;
        var realSkillCount = real.LearnedSkills?.Count ?? 0;
        var realMasterLevelPoints = real.MasterLevelUpPoints;

        var clone = await MobaCloneFactory.BuildCloneAsync(player, real).ConfigureAwait(false);

        var cloneLevel = clone.Attributes.FirstOrDefault(a => a.Definition.Id == Stats.Level.Id)?.Value ?? 0;
        var cloneInventoryCount = clone.Inventory?.Items.Count ?? 0;
        var cloneSkillCount = clone.LearnedSkills?.Count ?? 0;

        await player.ShowBlueMessageAsync($"[MOBA] Clone: class={clone.CharacterClass?.Name}, level={cloneLevel}, items={cloneInventoryCount}, skills={cloneSkillCount}, masterLvlPts={clone.MasterLevelUpPoints}, map={clone.CurrentMap?.Number} @ {clone.PositionX},{clone.PositionY}").ConfigureAwait(false);

        MobaCloneFactory.DetachClone(player, clone);

        var realInventoryAfter = real.Inventory?.Items.Count ?? 0;
        var realSkillAfter = real.LearnedSkills?.Count ?? 0;
        var untouched = realInventoryAfter == realInventoryCount
            && realSkillAfter == realSkillCount
            && real.MasterLevelUpPoints == realMasterLevelPoints
            && ReferenceEquals(player.SelectedCharacter, real);

        await player.ShowBlueMessageAsync($"[MOBA] Real char after: items={realInventoryAfter}, skills={realSkillAfter}, masterLvlPts={real.MasterLevelUpPoints} -> {(untouched ? "UNTOUCHED" : "CHANGED (!)")}").ConfigureAwait(false);
    }
}
