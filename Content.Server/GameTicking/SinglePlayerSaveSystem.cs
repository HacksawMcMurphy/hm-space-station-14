using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.Server.GameTicking;

/// <summary>
/// Persistence helpers for the offline single-player build.
/// The save is a normal Robust map YAML stored in user data, so it survives
/// application restarts without requiring a database or remote server.
/// </summary>
public sealed class SinglePlayerSaveSystem : EntitySystem
{
    public const string SavePath = "/SinglePlayer/Save.yml";
    public const float AutosaveSeconds = 60f;

    private float _autosaveTimer;

    [Dependency] private MapLoaderSystem _loader = default!;

    public static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("SS14_SINGLEPLAYER"), "1", StringComparison.Ordinal);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!Enabled)
            return;

        _autosaveTimer += frameTime;
        if (_autosaveTimer < AutosaveSeconds)
            return;

        _autosaveTimer = 0f;
        Save();
    }

    public override void Shutdown()
    {
        if (Enabled)
            Save();

        base.Shutdown();
    }

    public bool Save()
    {
        var ticker = EntityManager.System<ServerGameTicker>();
        if (ticker.DummyTicker || !ticker.DefaultMap.IsValid())
            return false;

        return _loader.TrySaveMap(ticker.DefaultMap, new ResPath(SavePath));
    }
}

[AdminCommand(AdminFlags.Host)]
public sealed class SinglePlayerSaveCommand : LocalizedCommands
{
    [Dependency] private SinglePlayerSaveSystem _save = default!;

    public override string Command => "sp_save";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!SinglePlayerSaveSystem.Enabled)
        {
            shell.WriteError("Single-player mode is not enabled.");
            return;
        }

        shell.WriteLine(_save.Save()
            ? $"Single-player game saved to {SinglePlayerSaveSystem.SavePath}."
            : "Single-player save failed.");
    }
}

[AdminCommand(AdminFlags.Host)]
public sealed class SinglePlayerLoadCommand : LocalizedCommands
{
    [Dependency] private IEntityManager _entityManager = default!;

    public override string Command => "sp_load";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (!SinglePlayerSaveSystem.Enabled)
        {
            shell.WriteError("Single-player mode is not enabled.");
            return;
        }

        var loader = _entityManager.System<MapLoaderSystem>();
        if (!loader.TryReadFile(new ResPath(SinglePlayerSaveSystem.SavePath), out _))
        {
            shell.WriteError("No single-player save exists.");
            return;
        }

        // RestartRound performs the normal cleanup and then LoadMaps() will
        // restore the persisted map before the new round starts.
        _entityManager.System<ServerGameTicker>().RestartRound();
        shell.WriteLine("Single-player save loaded by restarting the current round.");
    }
}
