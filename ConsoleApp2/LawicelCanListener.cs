using System;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;

public class LawicelCanAdapter : IDisposable
{
    private SerialPort _serialPort;
    private StringBuilder _buffer = new StringBuilder();
    private bool _timestampEnabled;
    private CanMode _currentMode = CanMode.Reset;

    public event Action<CanMessage> MessageReceived;
    public event Action<string> ErrorOccurred;

    public bool IsConnected => _serialPort?.IsOpen ?? false;

    public enum CanMode
    {
        Reset,
        Operational,
        ReadOnly
    }

    public bool Connect(string portName, int serialBaudRate, int canBaudRate)
    {
        try
        {
            _serialPort = new SerialPort(portName, serialBaudRate)
            {
                Parity = Parity.None,
                DataBits = 8,
                StopBits = StopBits.One,
                Handshake = Handshake.None,
                ReadTimeout = 50,
                WriteTimeout = 50
            };

            _serialPort.DataReceived += SerialPort_DataReceived;
            _serialPort.Open();

            SetCanBaudRate(canBaudRate);
            OpenChannel();

            return true;
        }
        catch (Exception ex)
        {
            OnError($"Connection error: {ex.Message}");
            return false;
        }
    }

    public void SetCanBaudRate(int rateCode)
    {
        ValidateMode(CanMode.Reset);
        SendCommand($"S{rateCode}");
    }

    public void OpenChannel()
    {
        ValidateMode(CanMode.Reset);
        SendCommand("O");
        _currentMode = CanMode.Operational;
    }

    public void CloseChannel()
    {
        ValidateMode(CanMode.Operational);
        SendCommand("C");
        _currentMode = CanMode.Reset;
    }

    public void EnableReadOnlyMode()
    {
        SendCommand("L");
        _currentMode = CanMode.ReadOnly;
    }

    public void SetFilter(uint filter)
    {
        ValidateMode(CanMode.Reset);
        SendCommand($"M{filter:X8}");
    }

    public void SetMask(uint mask)
    {
        ValidateMode(CanMode.Reset);
        SendCommand($"m{mask:X8}");
    }

    public string GetSerialNumber()
    {
        return SendCommandWithResponse("N", "N");
    }

    public string GetVersion()
    {
        return SendCommandWithResponse("V", "V");
    }

    public string GetDetailedVersion()
    {
        return SendCommandWithResponse("v", "v");
    }

    public void ToggleTimestamp(bool enable)
    {
        SendCommand($"Z{(enable ? 1 : 0)}");
        _timestampEnabled = enable;
    }

    public void SendStandardFrame(uint id, byte[] data)
    {
        ValidateMode(CanMode.Operational);
        if (id > 0x7FF) throw new ArgumentException("Invalid Standard ID");

        var cmd = new StringBuilder("t");
        cmd.Append(id.ToString("X3"));
        cmd.Append(data.Length.ToString("X1"));
        foreach (var b in data) cmd.Append(b.ToString("X2"));
        SendCommand(cmd.ToString());
    }

    public void SendExtendedFrame(uint id, byte[] data)
    {
        ValidateMode(CanMode.Operational);
        if (id > 0x1FFFFFFF) throw new ArgumentException("Invalid Extended ID");

        var cmd = new StringBuilder("T");
        cmd.Append(id.ToString("X8"));
        cmd.Append(data.Length.ToString("X1"));
        foreach (var b in data) cmd.Append(b.ToString("X2"));
        SendCommand(cmd.ToString());
    }

    private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            _buffer.Append(_serialPort.ReadExisting());
            ProcessBuffer();
        }
        catch (Exception ex)
        {
            OnError($"Data receive error: {ex.Message}");
        }
    }

    private void ProcessBuffer()
    {
        while (true)
        {
            int crIndex = _buffer.ToString().IndexOf('\r');
            if (crIndex < 0) break;

            string message = _buffer.ToString(0, crIndex);
            _buffer.Remove(0, crIndex + 1);

            if (message.Length == 0) continue;

            switch (message[0])
            {
                case 't':
                case 'T':
                case 'r':
                case 'R':
                    ProcessCanMessage(message);
                    break;
                case 'N':
                case 'V':
                case 'v':
                    // Обработка ответов на команды
                    break;
                case '\x07': // BEL
                    OnError("Device returned error");
                    break;
                default:
                    OnError($"Unknown message: {message}");
                    break;
            }
        }
    }

    private void ProcessCanMessage(string raw)
    {
        try
        {
            var msg = new CanMessage();
            int pos = 0;
            bool isRemote = false;

            // Определение типа сообщения
            switch (raw[pos++])
            {
                case 't':
                    msg.IsExtended = false;
                    break;
                case 'T':
                    msg.IsExtended = true;
                    break;
                case 'r':
                    msg.IsExtended = false;
                    isRemote = true;
                    break;
                case 'R':
                    msg.IsExtended = true;
                    isRemote = true;
                    break;
            }

            // Парсинг ID
            int idLength = msg.IsExtended ? 8 : 3;
            msg.Id = Convert.ToUInt32(raw.Substring(pos, idLength), 16);
            pos += idLength;

            // Парсинг длины данных
            int dataLength = Convert.ToInt32(raw.Substring(pos++, 1), 16);
            msg.Data = new byte[dataLength];

            if (!isRemote)
            {
                // Парсинг данных
                for (int i = 0; i < dataLength * 2; i += 2)
                {
                    msg.Data[i / 2] = Convert.ToByte(raw.Substring(pos + i, 2), 16);
                }
                pos += dataLength * 2;
            }

            // Парсинг временной метки
            if (_timestampEnabled && pos + 4 <= raw.Length)
            {
                msg.Timestamp = Convert.ToUInt16(raw.Substring(pos, 4), 16);
            }

            MessageReceived?.Invoke(msg);
        }
        catch (Exception ex)
        {
            OnError($"Message parsing error: {ex.Message}");
        }
    }

    private void SendCommand(string command)
    {
        _serialPort.Write($"{command}\r");
        Thread.Sleep(50); // Даем время на обработку
    }

    private string SendCommandWithResponse(string command, string expectedPrefix)
    {
        _buffer.Clear();
        _serialPort.Write($"{command}\r");

        DateTime timeout = DateTime.Now.AddSeconds(1);
        while (DateTime.Now < timeout)
        {
            if (_buffer.ToString().Contains('\r'))
            {
                string response = _buffer.ToString().Split('\r')[0];
                if (response.StartsWith(expectedPrefix))
                    return response;
            }
            Thread.Sleep(10);
        }
        throw new TimeoutException("No response received");
    }

    private void ValidateMode(CanMode requiredMode)
    {
        if (_currentMode != requiredMode)
            throw new InvalidOperationException(
                $"Command requires {requiredMode} mode, current mode is {_currentMode}");
    }

    private void OnError(string message)
    {
        ErrorOccurred?.Invoke(message);
    }

    public void Dispose()
    {
        CloseChannel();
        _serialPort?.Dispose();
    }
}

public class CanMessage
{
    public uint Id { get; set; }
    public bool IsExtended { get; set; }
    public byte[] Data { get; set; }
    public ushort Timestamp { get; set; }

    public override string ToString()
    {
        //return $"ID: {Id:X} | Extended: {IsExtended} | Data: {BitConverter.ToString(Data)} | " +
         //      $"Timestamp: {Timestamp}ms";

        return $"{Id:X};{BitConverter.ToString(Data)}";
    }
}