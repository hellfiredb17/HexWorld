namespace ClanIronSim.Player;

public sealed class PlayerService
{
    public List<PlayerProfile> Profiles { get; set; } = new();

    public List<PlayerRuntime> Runtimes { get; set; } = new();

    public bool TryRegisterPlayer(PlayerProfile profile)
    {
        if (profile is null || Profiles.Any(candidate => candidate.Id == profile.Id))
        {
            return false;
        }

        Profiles.Add(profile);
        if (!Runtimes.Any(runtime => runtime.PlayerId == profile.Id))
        {
            Runtimes.Add(new PlayerRuntime { PlayerId = profile.Id });
        }

        return true;
    }

    public bool TryRemovePlayer(int playerId)
    {
        PlayerProfile? profile = Profiles.FirstOrDefault(candidate => candidate.Id == playerId);
        if (profile is null)
        {
            return false;
        }

        Profiles.Remove(profile);
        Runtimes.RemoveAll(runtime => runtime.PlayerId == playerId);
        return true;
    }

    public bool TryGetProfile(int playerId, out PlayerProfile? profile)
    {
        profile = Profiles.FirstOrDefault(candidate => candidate.Id == playerId);
        return profile is not null;
    }

    public bool TryGetRuntime(int playerId, out PlayerRuntime? runtime)
    {
        runtime = Runtimes.FirstOrDefault(candidate => candidate.PlayerId == playerId);
        return runtime is not null;
    }

    public bool TryAssignNavigator(int playerId, string navigatorId)
    {
        if (string.IsNullOrWhiteSpace(navigatorId)
            || !TryGetProfile(playerId, out _)
            || TryGetPlayerIdForNavigator(navigatorId, out int existingPlayerId) && existingPlayerId != playerId)
        {
            return false;
        }

        if (!TryGetRuntime(playerId, out PlayerRuntime? runtime))
        {
            runtime = new PlayerRuntime { PlayerId = playerId };
            Runtimes.Add(runtime);
        }

        runtime!.NavigatorId = navigatorId;
        return true;
    }

    public bool TryUnassignNavigator(int playerId)
    {
        if (!TryGetRuntime(playerId, out PlayerRuntime? runtime)
            || string.IsNullOrWhiteSpace(runtime!.NavigatorId))
        {
            return false;
        }

        runtime.NavigatorId = string.Empty;
        return true;
    }

    public bool TryGetNavigatorId(int playerId, out string navigatorId)
    {
        navigatorId = string.Empty;
        if (!TryGetRuntime(playerId, out PlayerRuntime? runtime)
            || string.IsNullOrWhiteSpace(runtime!.NavigatorId))
        {
            return false;
        }

        navigatorId = runtime.NavigatorId;
        return true;
    }

    public bool TryGetPlayerIdForNavigator(string navigatorId, out int playerId)
    {
        playerId = default;
        if (string.IsNullOrWhiteSpace(navigatorId))
        {
            return false;
        }

        PlayerRuntime? runtime = Runtimes.FirstOrDefault(candidate =>
            string.Equals(candidate.NavigatorId, navigatorId, StringComparison.OrdinalIgnoreCase));
        if (runtime is null)
        {
            return false;
        }

        playerId = runtime.PlayerId;
        return true;
    }
}
