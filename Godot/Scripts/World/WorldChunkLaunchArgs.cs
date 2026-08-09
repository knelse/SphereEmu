using Godot;

namespace SphServer.Godot.Scripts.World;

/// <summary>
///     Shared CLI user-args for world chunk load-all launch mode.
///     Pass after <c>--</c>, e.g. <c>godot --path . -- --load-all-world-chunks</c>.
/// </summary>
public static class WorldChunkLaunchArgs
{
	public const string LoadAllWorldChunks = "--load-all-world-chunks";

	public static bool WantsLoadAllWorldChunks()
	{
		foreach (var arg in OS.GetCmdlineUserArgs())
		{
			if (arg == LoadAllWorldChunks)
			{
				return true;
			}
		}

		return false;
	}
}
