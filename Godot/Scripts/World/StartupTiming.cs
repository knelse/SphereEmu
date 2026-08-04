using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using SphServer.Shared.Logger;

namespace SphServer.Godot.Scripts.World;

/// <summary>
///     Lightweight startup / open timing for MainServer cold-start baselines (Phase 0).
/// </summary>
public static class StartupTiming
{
	private static readonly Lock Gate = new();
	private static readonly Stopwatch ProcessWatch = Stopwatch.StartNew();
	private static readonly List<(string Label, long ElapsedMs)> Marks = [];
	private static bool Enabled = true;

	public static void Mark(string label)
	{
		if (!Enabled)
		{
			return;
		}

		var elapsedMs = ProcessWatch.ElapsedMilliseconds;
		lock (Gate)
		{
			Marks.Add((label, elapsedMs));
		}

		var message = $"[StartupTiming] {label} @ {elapsedMs} ms";
		GD.Print(message);
		TrySphLog(message);
	}

	public static void MarkSpan(string label, long durationMs)
	{
		if (!Enabled)
		{
			return;
		}

		var message = $"[StartupTiming] {label} took {durationMs} ms (at {ProcessWatch.ElapsedMilliseconds} ms)";
		lock (Gate)
		{
			Marks.Add((label, ProcessWatch.ElapsedMilliseconds));
		}

		GD.Print(message);
		TrySphLog(message);
	}

	private static void TrySphLog(string message)
	{
		try
		{
			if (!Engine.IsEditorHint())
			{
				SphLogger.Info(message);
			}
		}
		catch
		{
			// Logger may not be initialized during headless tooling.
		}
	}

	public static IReadOnlyList<(string Label, long ElapsedMs)> Snapshot()
	{
		lock (Gate)
		{
			return Marks.ToArray();
		}
	}
}
