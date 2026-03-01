using System;
using Dalamud.Plugin.Services;

namespace WTP.Services;

public class PlayerIdentityService
{
    private readonly IClientState clientState;
    private readonly IPlayerState playerState;
    private readonly IPluginLog log;

    public PlayerIdentityService(IClientState clientState, IPlayerState playerState, IPluginLog log)
    {
        this.clientState = clientState;
        this.playerState = playerState;
        this.log = log;
    }

    public string? GetCharacterName()
    {
        try
        {
            // LocalPlayer is the only valid source of the character's SeString name
            return clientState.LocalPlayer?.Name?.ToString();
        }
        catch (Exception ex)
        {
            log.Debug(ex, "Failed to get character name");
            return null;
        }
    }

    public string? GetWorldName()
    {
        try
        {
            var player = clientState.LocalPlayer;
            if (player == null)
                return null;

            // HomeWorld.Value.Name is a ReadOnlySeString → ToString() works on all builds
            return player.HomeWorld.Value.Name.ToString();
        }
        catch (Exception ex)
        {
            log.Debug(ex, "Failed to get world name");
            return null;
        }
    }

    public string? GetCharacterId()
    {
        try
        {
            var id = playerState.ContentId;
            return id != 0 ? id.ToString() : null;
        }
        catch (Exception ex)
        {
            log.Debug(ex, "Failed to get character ID");
            return null;
        }
    }
}
