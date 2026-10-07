using System;

namespace SphServer.Shared.WorldState;

/// <summary>
/// Roster and character-change fan-out with no gameplay dependency
/// </summary>
public static class ClientStateEvents
{
    public static event Action? RosterChanged;
    public static event Action<ushort>? CharacterChanged;

    public static void RaiseRosterChanged () => RosterChanged?.Invoke ();

    public static void RaiseCharacterChanged (ushort clientId)
    {
        if (clientId == 0)
        {
            return;
        }

        CharacterChanged?.Invoke (clientId);
    }
}
