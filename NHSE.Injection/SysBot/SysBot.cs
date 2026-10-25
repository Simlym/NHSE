using System;
using System.Net.Sockets;
using System.IO;
using System.Threading;

namespace NHSE.Injection;

public sealed class SysBot : IRAMReadWriter
{
    public string IP = "192.168.1.65";
    public int Port = 6000;
    public Socket Connection = new(SocketType.Stream, ProtocolType.Tcp);
    public bool Connected { get; private set; }

    private readonly Lock _sync = new();

    public void Connect(string ip, int port)
    {
        IP = ip;
        Port = port;
        lock (_sync)
        {
            Connected = false; Connection.Dispose();
            Connection = new Socket(SocketType.Stream, ProtocolType.Tcp) { SendTimeout = 5000, ReceiveTimeout = 5000 };
            try
            {
                if (!Connection.ConnectAsync(IP, Port).Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
                Connected = true;
            }
            catch { Connection.Dispose(); throw; }
        }
    }

    public void Disconnect()
    {
        lock (_sync)
        {
            Connection.Dispose();
            Connected = false;
        }
    }

    private int ReadInternal(byte[] buffer)
    {
        var count = 0;
        while (count < buffer.Length)
        {
            if (Connection.Receive(buffer, count, 1, SocketFlags.None) == 0) throw new EndOfStreamException();
            if (buffer[count++] == (byte)'\n') return count;
        }
        throw new IOException("Unexpected response length.");
    }

    private int SendInternal(byte[] buffer)
    {
        var sent = 0;
        while (sent < buffer.Length)
        {
            var count = Connection.Send(buffer, sent, buffer.Length - sent, SocketFlags.None);
            if (count == 0) throw new EndOfStreamException(); sent += count;
        }
        return sent;
    }

    public int Read(byte[] buffer)
    {
        lock (_sync)
            return ReadInternal(buffer);
    }

    public byte[] ReadBytes(uint offset, int length)
    {
        lock (_sync)
        {
            var cmd = SwitchCommand.Peek(offset, length);
            SendInternal(cmd);

            // give it time to push data back
            Thread.Sleep((length / 256) + 100);
            var buffer = new byte[(length * 2) + 1];
            if (ReadInternal(buffer) != buffer.Length) throw new IOException("Unexpected response length.");
            return Convert.FromHexString(buffer.AsSpan(0, buffer.Length - 1));
        }
    }

    public void WriteBytes(byte[] data, uint offset)
    {
        lock (_sync)
        {
            SendInternal(SwitchCommand.Poke(offset, data));

            // give it time to push data back
            Thread.Sleep((data.Length / 256) + 100);
        }
    }
}

public enum InjectionType
{
    Generic,
    Pouch,
}
