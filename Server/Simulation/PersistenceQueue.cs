using LiteDB;
using SphServer.Client;
using SphServer.Shared.Db;
using SphServer.Shared.WorldState;

namespace SphServer.Server.Simulation;

public enum WriterActionKind
{
	SaveCharacter = 1,
	Audit = 2
}

public readonly record struct WriterAction (WriterActionKind Kind, Guid ActorId, string Detail);

/// <summary>
/// Economy and audit rows, applied on the main thread in one LiteDB transaction
/// </summary>
public sealed class PersistenceQueue
{
	private readonly List<WriterAction> pending = new ();

	public void Enqueue (WriterAction action)
	{
		pending.Add (action);
	}

	public List<WriterAction> Take ()
	{
		if (pending.Count == 0)
		{
			return new List<WriterAction> ();
		}

		var batch = new List<WriterAction> (pending);
		pending.Clear ();
		return batch;
	}

	public static void ApplyBatch (IReadOnlyList<WriterAction> actions, IReadOnlyList<CombatResult> hits, ulong tick)
	{
		if (actions.Count == 0 && hits.Count == 0)
		{
			return;
		}

		if (DbConnection.Db is null)
		{
			return;
		}

		var started = DbConnection.Db.BeginTrans ();
		try
		{
			for (var i = 0; i < actions.Count; i++)
			{
				Apply (actions[i]);
			}

			if (hits.Count > 0)
			{
				var audits = DbConnection.Db.GetCollection ("SimAudit");
				for (var i = 0; i < hits.Count; i++)
				{
					InsertHit (audits, tick, hits[i]);
				}
			}

			if (started)
			{
				DbConnection.Db.Commit ();
			}
		}
		catch
		{
			if (started)
			{
				DbConnection.Db.Rollback ();
			}

			throw;
		}
	}

	private static void Apply (WriterAction action)
	{
		if (action.Kind == WriterActionKind.SaveCharacter)
		{
			SaveCharacter (action.ActorId);
			return;
		}

		var audits = DbConnection.Db.GetCollection ("SimAudit");
		audits.Insert (new BsonDocument
		{
			["actor"] = action.ActorId.ToString (),
			["detail"] = action.Detail
		});
	}

	private static void SaveCharacter (Guid actorId)
	{
		var character = ActiveClients.Get (actorId)?.CurrentCharacter;
		if (character is null || character.Id == 0)
		{
			return;
		}

		if (!DbConnection.Characters.Update (character))
		{
			DbConnection.Characters.Upsert (character);
		}
	}

	private static void InsertHit (ILiteCollection<BsonDocument> audits, ulong tick, CombatResult hit)
	{
		audits.Insert (new BsonDocument
		{
			["tick"] = (long) tick,
			["actor"] = hit.AttackerId.ToString (),
			["target"] = hit.TargetId.ToString (),
			["applied"] = hit.Applied,
			["hp"] = hit.RemainingHp,
			["dead"] = hit.BecameDead,
			["miss"] = hit.IsMiss
		});
	}
}
