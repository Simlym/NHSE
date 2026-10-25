using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using NHSE.Core;
using NHSE.Injection;

namespace NHSE.WinForms;

public sealed partial class WorkspaceForm
{
    private readonly SysBot ConsoleNetwork = new();
    private readonly USBBot ConsoleUsb = new();
    private string ConsoleMode = "network";
    private byte[] ConsoleBytes = [];
    private uint ConsoleOffset;
    private IRAMReadWriter ConsoleBot => ConsoleMode == "usb" ? ConsoleUsb : ConsoleNetwork;
    private object ConsoleStatus() => new { connected = ConsoleBot.Connected, transport = ConsoleMode,
        host = Program.Settings.SysBot.IP, port = Program.Settings.SysBot.Port,
        offset = ConsoleOffset == 0 ? Program.Settings.SysBot.PouchOffset.ToString("X8") : ConsoleOffset.ToString("X8"),
        data = Convert.ToHexString(ConsoleBytes), state = State() };
    private void DisconnectConsole()
    {
        ConsoleNetwork.Disconnect();
        if (ConsoleUsb.Connected) ConsoleUsb.Disconnect();
        ConsoleBytes = []; ConsoleOffset = 0;
    }
    private object ConsoleCommand(string command, JsonElement p)
    {
        if (command == "consoleState") return ConsoleStatus();
        if (command == "consoleDisconnect") { DisconnectConsole(); return ConsoleStatus(); }
        if (command == "consoleConnect")
        {
            var mode = Str(p, "transport");
            if (mode is not ("network" or "usb")) throw new ArgumentException("consoleInvalid");
            if (mode == "network" && (!IPAddress.TryParse(Str(p, "host"), out _) || Int(p, "port") is < 1 or > 65535)) throw new ArgumentException("consoleInvalid");
            DisconnectConsole(); ConsoleMode = mode;
            try
            {
                if (mode == "usb") ConsoleUsb.Connect();
                else { ConsoleNetwork.Connect(Str(p, "host"), Int(p, "port")); Program.Settings.SysBot.IP = Str(p, "host"); Program.Settings.SysBot.Port = Int(p, "port"); }
            }
            catch { DisconnectConsole(); throw new ArgumentException("consoleConnectionError"); }
            return ConsoleStatus();
        }
        if (!uint.TryParse(Str(p, "offset"), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var offset) || offset == 0) throw new ArgumentException("consoleInvalid");
        if (!ConsoleBot.Connected) throw new ArgumentException("consoleDisconnected");
        try
        {
            if (command == "consoleRead") ConsoleBytes = ConsoleTransfer.Read(ConsoleBot, offset, Int(p, "length"));
            else if (command == "consoleWrite")
            {
                var hex = Str(p, "data");
                if (hex.Length is < 2 or > ConsoleTransfer.MaxLength * 2 || hex.Length % 2 != 0 || hex.Any(c => !Uri.IsHexDigit(c))) throw new ArgumentException("consoleInvalid");
                var bytes = Convert.FromHexString(hex); ConsoleTransfer.Write(ConsoleBot, offset, bytes); ConsoleBytes = bytes;
            }
            else if (command is "consolePocketRead" or "consolePocketWrite")
            {
                var session = RequireSession(); var player = Int(p, "player");
                var items = session.GetItems(player, "pocket").Select(i => new Item(i.RawValue)).ToArray();
                var injector = new PocketInjector(items, ConsoleBot) { WriteOffset = offset, ValidateEnabled = p.GetProperty("validate").GetBoolean(), SpoofInventoryWrite = p.GetProperty("spoof").GetBoolean() };
                var result = command == "consolePocketRead" ? injector.Read() : injector.Write();
                if (result is not (InjectionResult.Success or InjectionResult.Same)) throw new ArgumentException("consoleValidationError");
                if (command == "consolePocketRead") session.ImportAdvancedCollection(new("item", Player: player), Item.SetArray(items));
            }
            else throw new ArgumentException("invalidRequest");
            ConsoleOffset = offset; Program.Settings.SysBot.PouchOffset = offset;
        }
        catch (ArgumentException) { throw; }
        catch { DisconnectConsole(); throw new ArgumentException("consoleConnectionError"); }
        return ConsoleStatus();
    }
}
