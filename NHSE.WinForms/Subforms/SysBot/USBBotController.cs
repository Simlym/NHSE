using System;
using NHSE.Injection;

namespace NHSE.WinForms;

public sealed class USBBotController
{
    public readonly USBBot Bot = new();

    public bool Connect()
    {
        try
        {
            return Bot.Connect();
        }
        catch (Exception ex)
        {
            WinFormsUtil.Error(ex.Message);
            return false;
        }
    }

    public void Disconnect() => Bot.Disconnect();

    public uint GetDefaultOffset() => Program.Settings.SysBot.PouchOffset;
    public void SetOffset(uint offset) => Program.Settings.SysBot.PouchOffset = offset;
    public void PopPrompt()
    {
        if (Program.Settings.SysBot.Prompted) return;
        WinFormsUtil.Alert(MessageStrings.MsgSysBotInfo, MessageStrings.MsgSysBotRequired);
        Program.Settings.SysBot.Prompted = true;
    }

    public void WriteBytes(byte[] data, uint offset)
    {
        Bot.WriteBytes(data, offset);
        SetOffset(offset);
    }

    public byte[] ReadBytes(uint offset, int length)
    {
        var result = Bot.ReadBytes(offset, length);
        SetOffset(offset);
        return result;
    }
}
