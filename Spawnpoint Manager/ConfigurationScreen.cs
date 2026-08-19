using System.Collections.Generic;
using Satchel.BetterMenus;
using UnityEngine;

namespace RespawnPointManager;

public static class ConfigurationScreen
{
    private static Menu _menuRef;

    public static MenuScreen GetScreen(MenuScreen modListMenu, GlobalSettings settings)
    {
        var elements = new List<Element>
        {
            new TextPanel("Respawn Point Manager"),

            new HorizontalOption(
                "Show Counter",
                "",
                new[] { "On", "Off" },
                i =>
                {
                    settings.ShowCounter = i == 0;
                    RespawnPointManager.Instance.RedrawCounters();
                },
                () => settings.ShowCounter ? 0 : 1
            ),

            new HorizontalOption(
                "HUD Position",
                "",
                new[] { "Screen Edge", "Beside Geo", "Far From Geo" },
                i =>
                {
                    settings.PositionIndex = i;
                    RespawnPointManager.Instance.RedrawCounters();
                },
                () => settings.PositionIndex
            ),

            new HorizontalOption(
                "Teleport Mode",
                "Multi Scene keeps points when you change rooms and can teleport across scenes",
                new[] { "Single Scene", "Multi Scene" },
                i =>
                {
                    settings.MultiSceneMode = i == 1;
                    RespawnPointManager.Instance.OnTeleportModeChanged();
                },
                () => settings.MultiSceneMode ? 1 : 0
            ),

            new HorizontalOption(
                "Checkpoint Mode",
                "Manual: game's own checkpoints (benches, hazard triggers, room entry) are ignored",
                new[] { "Auto", "Manual" },
                i =>
                {
                    settings.ManualCheckpointMode = i == 1;
                },
                () => settings.ManualCheckpointMode ? 1 : 0
            ),

            new HorizontalOption(
                "Ignore Entry Checkpoint",
                "Entry Checkpoint doesn't count as a point. Always works in Manual mode",
                new[] { "On", "Off" },
                i =>
                {
                    settings.IgnoreEntryCheckpoint = i == 0;
                },
                () => settings.IgnoreEntryCheckpoint ? 0 : 1
            ),

            new TextPanel("Keybinds"),
            new KeyBind("Previous Point", settings.Keybinds.Teleport),
            new KeyBind("Next Point", settings.Keybinds.Next),
            new KeyBind("Delete Point (hold)\n\n\nCreate Point (tap)", settings.Keybinds.Spawn),
            new KeyBind("Clear all (hold)", settings.Keybinds.Clear),
        };

        _menuRef ??= new Menu(
            "Respawn Point Manager",
            elements.ToArray()
        );

        return _menuRef.GetMenuScreen(modListMenu);
    }
}
