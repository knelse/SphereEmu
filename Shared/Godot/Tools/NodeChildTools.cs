using System;
using Godot;

namespace SphServer.Shared.Godot.Tools;

public static class NodeChildTools
{
    public static T? FindFirstChildOfType<T> (Node current, string? name = null)
        where T : Node
    {
        ArgumentNullException.ThrowIfNull (current);

        foreach (var child in current.GetChildren ())
        {
            if (child is not T match)
            {
                continue;
            }

            if (name is not null && child.Name != name)
            {
                continue;
            }

            return match;
        }

        return null;
    }
}
