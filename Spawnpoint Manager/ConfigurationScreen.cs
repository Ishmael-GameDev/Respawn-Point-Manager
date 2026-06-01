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