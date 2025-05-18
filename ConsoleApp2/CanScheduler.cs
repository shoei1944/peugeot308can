using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Timers;
using ConsoleApp2;

public class CanScheduler : IDisposable
{
    private readonly LawicelCanAdapter _adapter;
    private readonly List<CanPacket> _packets = new List<CanPacket>();
    private readonly Dictionary<uint, CanPacket> _packetMap = new Dictionary<uint, CanPacket>();
    private readonly List<Timer> _timers = new List<Timer>();

    public CanScheduler(LawicelCanAdapter adapter)
    {
        _adapter = adapter;
    }

    public void AddPacket(CanPacket packet)
    {
        _packets.Add(packet);
        _packetMap[packet.Id] = packet;
    }

    public CanPacket GetPacket(uint id)
    {
        return _packetMap.TryGetValue(id, out var packet) ? packet : null;
    }

    public void StartSending()
    {
        foreach (var packet in _packets)
        {
            var timer = new Timer(packet.Period);
            timer.Elapsed += (s, e) => SendPacket(packet);
            timer.AutoReset = true;
            timer.Enabled = true;
            _timers.Add(timer);
        }
    }

    public void StopSending()
    {
        foreach (var timer in _timers)
        {
            timer.Stop();
            timer.Dispose();
        }
        _timers.Clear();
    }

    private void SendPacket(CanPacket packet)
    {
        try
        {
            if (packet.IsExtended)
                _adapter.SendExtendedFrame(packet.Id, packet.Data);
            else
                _adapter.SendStandardFrame(packet.Id, packet.Data);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending packet {packet.Id:X}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        StopSending();
    }
}

public class CanPacket
{
    public uint Id { get; set; }
    public byte[] Data { get; set; }
    public double Period { get; set; }
    public bool IsExtended { get; set; }

    public static CanPacket Parse(string id, string data, double period)
    {
        return new CanPacket
        {
            Id = Convert.ToUInt32(id.TrimStart('0'), 16),
            Data = data.Split(' ')
                      .Select(s => Convert.ToByte(s, 16))
                      .ToArray(),
            Period = period,

            // IsExtended = id.TrimStart('0').Length > 3 // Определяем тип фрейма по длине ID
            IsExtended = Convert.ToUInt32(id, 16) > 0x7FF
        };
    }
}