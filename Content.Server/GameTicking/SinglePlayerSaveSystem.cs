using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Server.GameTicking;

/// <summary>
/// Persistence helpers for the offline single-player build.
/// Saves are Robust map YAML stored in user data so the station survives
/// application restarts without a database or external server.
/// </summary>
public sealed class SinglePlayerSaveSystem : EntitySystem
{
    public const string SavePath = "/SinglePlayer/Save.yml";
    public const float AutosaveSeconds = 60f;

    private float _autosaveTimer;

    [Dependency] private MapLoaderSystem _loader = default!;

    public static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("SS14_SINGLEPLAYER"), "1", StringComparison.Ordinal);

    public override void Initialize()
    {
        base.Initialize();
        _loader.OnIsSerializable += OnIsSerializable;
    }

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

    private void OnIsSerializable(Entity<MetaDataComponent> entity, ref bool serializable)
    {
        // ActorComponent is session state and is explicitly marked unsaved by RobustToolbox.
        // Do not serialize the live session attachment; the character entity itself remains persistable.
        if (TryComp<ActorComponent>(entity.Owner, out _))
            serializable = false;
    }

    public bool Save()
    {
        var ticker = EntityManager.System<ServerGameTicker>();
        if (ticker.DummyTicker || !ticker.DefaultMap.IsValid())
            return false;

        return _loader.TrySaveMap(ticker.DefaultMap, new ResPath(SavePath));
    }

    public bool TryLoadSavedMap(out MapId mapId)
    {
        mapId = default;

        if (!Enabled || !_loader.TryLoadMap(
                new ResPath(SavePath),
                out var map,
                out _,
                DeserializationOptions.Default with { InitializeMaps = false }))
        {
            return false;
        }

        mapId = map.Value.Comp.MapId;
        return true;
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

        _entityManager.System<ServerGameTicker>().RestartRound();
        shell.WriteLine("Single-player save will be loaded after the current round is reset.");
    }
}
