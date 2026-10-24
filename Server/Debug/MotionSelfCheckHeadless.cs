using Godot;
using SphServer.Server.Simulation.Motion;
using SphServer.Shared.Logger;

namespace SphServer.Server.Debug;

/// <summary>
///     Headless: stream real .lnd/.mdl geometry and print the solver's measurements.
///     Run: <c>.\Tools\motion_self_check.ps1</c>
/// </summary>
public partial class MotionSelfCheckHeadless : Node
{
	private const int ExitSuccess = 0;
	private const int ExitFailure = 1;

	public override void _Ready ()
	{
		var code = ExitSuccess;
		try
		{
			var stream = new MotionWorldStream { Name = "MotionWorldStream" };
			AddChild (stream);
			if (stream.MapCellCount == 0)
			{
				SphLogger.Info ("MotionSelfCheck: map not loaded, nothing to measure");
				code = ExitFailure;
			}
			else
			{
				MotionSelfCheck.Run (stream);
			}
		}
		finally
		{
			GetTree ().Quit (code);
		}
	}
}
