using System;

namespace SphServer.Shared.WorldState;

/// <summary>
/// Roster and character-change fan-out with no gameplay dependency
/// </summary>
public static class ClientStateEvents
{
    public static event Action? RosterChanged;
    public static event Action<Guid>? CharacterChanged;

    public static void RaiseRosterChanged () => RosterChanged?.Invoke ();

    public static void RaiseCharacterChanged (Guid clientId)
    {
        if (clientId == Guid.Empty)
        {
            return;
        }

        CharacterChanged?.Invoke (clientId);
    }
}
