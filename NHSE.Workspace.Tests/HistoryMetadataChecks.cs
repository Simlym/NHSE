using System;
using System.Linq;
using System.Text.Json;
using NHSE.Core;
using NHSE.WinForms;
internal static class HistoryMetadataChecks
{
    private static void Check(bool ok, string text) { if (!ok) throw new Exception(text); Console.WriteLine("PASS " + text); }
    private static JsonElement State(WorkspaceSession session) => JsonSerializer.SerializeToElement(session.Snapshot());
    public static void Run(MemoryProvider provider)
    {
        var save = new HorizonSave(provider); var session = new WorkspaceSession(save);
        var before = save.Players[0].Personal.Wallet.Value; var initial = State(session);
        session.SetActionContext(new("setPlayer", "player=0"));
        var p = save.Players[0].Personal;
        session.SetPlayer(0, (uint)before + 1, (uint)p.Bank, (uint)p.NookMiles, (uint)p.TotalNookMiles);
        session.SetActionContext(null);
        var applied = State(session);
        Check(applied.GetProperty("undoAction").GetProperty("Command").GetString() == "setPlayer", "history metadata records the actual committed command");
        Check(applied.GetProperty("id").GetString() == initial.GetProperty("id").GetString(), "session identity remains stable after an edit");
        session.SetActionContext(new("inventory", "player=1")); session.GetItems(0, "pocket"); session.SetActionContext(null);
        Check(State(session).GetProperty("undoAction").ToString() == applied.GetProperty("undoAction").ToString(), "read-only requests do not relabel undo history");
        session.Undo(); var undone = State(session);
        Check(save.Players[0].Personal.Wallet.Value == before && undone.GetProperty("redoAction").GetProperty("Command").GetString() == "setPlayer", "undo preserves the description on the redo transaction");
        session.Redo(); Check(State(session).GetProperty("undoAction").ToString() == applied.GetProperty("undoAction").ToString(), "redo restores its original operation description");
        session.SetActionContext(new("setPlayer", "player=1"));
        session.SetPlayer(0, (uint)p.Wallet, (uint)p.Bank, (uint)p.NookMiles, (uint)p.TotalNookMiles); session.SetActionContext(null);
        Check(State(session).GetProperty("undoAction").ToString() == applied.GetProperty("undoAction").ToString(), "no-op edits cannot replace the previous history description");
        Check(State(new WorkspaceSession(save)).GetProperty("id").GetString() != initial.GetProperty("id").GetString(), "reopening the same save receives a fresh UI session identity");
    }
}
