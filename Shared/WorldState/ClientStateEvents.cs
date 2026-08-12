using System;

namespace SphServer.Shared.WorldState;

/// <summary>
///     Cheap fan-out when connected clients or a character's visible state change.
///     Admin UI (and similar) subscribe; gameplay code raises.
/// </summary>
public static class ClientStateEvents
{
    public static event Action? RosterChanged;
    public static event Action<ushort>? CharacterChanged;

    public static void RaiseRosterChanged() => RosterChanged?.Invoke();

    public static void RaiseCharacterChanged(ushort clientId)
    {
        if (clientId == 0)
        {
            return;
        }

        CharacterChanged?.Invoke(clientId);
    }
}
