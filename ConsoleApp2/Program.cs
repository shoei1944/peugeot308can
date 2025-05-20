using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConsoleApp2;
using Newtonsoft.Json;
using WebSocketSharp;
using WebSocketSharp.Server;
using CommandLine;
using System.Net.Sockets;
using System.Net;

namespace ConsoleApp2
{
    public static class CarData
    {
        public static int Rpm { get; set; }
        public static int Speed { get; set; }
        public static int Temp { get; set; }
        public static int Gas { get; set; }
        public static float Odo { get; set; }
        public static bool RTurn { get; set; }
        public static bool LTurn { get; set; }
        public static bool LBeam { get; set; }
        public static bool HBeam { get; set; }
        public static bool Stop { get; set; }
        public static bool Esp { get; set; }
        public static bool OilWarn { get; set; }
        public static bool Parking { get; set; }
        public static bool Check { get; set; }
        public static bool Abs { get; set; }
        public static bool Battery { get; set; }
        public static int Gear { get; set; }
        public static bool Fog { get; set; }
        public static bool GearSport { get; set; }
        public static bool GearAuto { get; set; }
    }

    class Program
    {
        public static volatile bool _running = true;
        public static DateTime _lastUpdateTime = DateTime.Now;
        public static object _lock = new object();
        private static LawicelCanAdapter _adapter;
        private static CanScheduler _scheduler;
        private static byte mileage1;
        private static byte mileage2;
        private static byte mileage3;
        private static byte displaycontrast;
        public static float dailymileage = 0.0f;

        public static String Port;
        public static String Mode;
        public static int Baud;
        public static bool Selftest = false;

        public static bool isData = false;

        public class Options
        {
            [Option('p', "port", Required = true, HelpText = "ComPort (COM14 for example)")]
            public String Port { get; set; }
            [Option('b', "baud", Required = false, Default = 115200, HelpText = "Baud rate (115200 for example)")]
            public int Baud { get; set; }
            [Option('m', "mode", Required = false, Default = "udp", HelpText = "Mode for input data (ws or udp for example)")]
            public String Mode { get; set; }
            [Option('s', "self-test", Required = false, HelpText = "Self-test panel")]
            public bool Selftest { get; set; }

        }

        static void Main(string[] args)
        {
            _adapter = new LawicelCanAdapter();
            _scheduler = new CanScheduler(_adapter);

            Parser.Default.ParseArguments<Options>(args).WithParsed<Options>(o => {
                Port = o.Port;
                Mode = o.Mode;
                if (o.Baud != 0) Baud = o.Baud;  
            });

            var packets = new[]
            {
            CanPacket.Parse("036", "FF FF 00 00 01 FF FF FF", 100), // eco, display, on/off
            CanPacket.Parse("0F6", "88 00 FF FF FF FF FF FF", 100), // temp ct, on/off (2 byte is temp - 70 is set)
            CanPacket.Parse("0B6", "00 00 00 77 FF FF FF FF", 10), // taho and speed (1 byte is taho - FF is set, 3 byte is speed - 10 is set)
            // CanPacket.Parse("257", "1E 84 80 E1 7B 7F", 500), // odo
            CanPacket.Parse("161", "FF FF FF 00 FF FF FF", 500), // gas (4 byte is gas count - 55 is set)
            CanPacket.Parse("128", "00 00 00 00 00 00 00 00", 100), // indicators on panel (1 byte: coil, low gas, unfastened; 2 byte: warning, stop, opened doors; 3 byte: esp; 4 byte: pls gas down; 5 byte: right/left turnes, low/high beam; 6 byte: none; 7 byte: transmission; 8 byte: none;)
            CanPacket.Parse("168", "00 00 00 00 00 00 00 00", 200),
            CanPacket.Parse("1A8", "00 00 00 00 00 00 00 00", 200),
            // CanPacket.Parse("361", "00 00 FF 00 00 00", 100)
            // 3F6 - miles or km - 6 bit
            // 0x00 - KM, 0xFF - Miles
        };

            foreach (var packet in packets)
            {
                _scheduler.AddPacket(packet);
            }

            _adapter.ErrorOccurred += err => Console.WriteLine($"Error: {err}");
            _adapter.MessageReceived += msg =>
            {
                switch (msg.Id.ToString("X3"))
                {
                    case "217" when msg.Data.Length >= 2:
                        displaycontrast = msg.Data[0];
                        if (msg.Data[1] == 0x82) dailymileage = 0.0f;
                        // if (!isData) UpdateCluster(CarData.Rpm, CarData.Speed, CarData.Temp, CarData.Gas, CarData.RTurn, CarData.LTurn, CarData.LBeam, CarData.HBeam, CarData.Stop, CarData.Esp, 0.0f, CarData.OilWarn, CarData.Parking, CarData.Check, CarData.Abs, CarData.Battery, CarData.Gear, CarData.Fog, false, false, true);
                        break;

                    case "257" when msg.Data.Length >= 3:
                        mileage1 = msg.Data[0];
                        mileage2 = msg.Data[1];
                        mileage3 = msg.Data[2];
                        break;
                }
            };

            var wssv = new WebSocketServer("ws://127.0.0.1:1212");
            Socket udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            var localIP = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5555);
            Console.WriteLine("Peugeot 308 CAN Bus Panel (git: none)");

            try
            {
                if (_adapter.Connect(Port, Baud, 4))
                {
                    Console.WriteLine($"Connecting to {Port} at {Baud}");
                    if (Mode == "ws")
                    {
                        wssv.AddWebSocketService<Laputa>("/");
                        wssv.Start();
                    }
                    if (Mode == "udp")
                    {
                        udpSocket.Bind(localIP);
                        
                    }

                    Console.WriteLine("Connected to ComPort!");
                    if (Mode == "ws") Console.WriteLine($"Started at {Mode}://{wssv.Address}:{wssv.Port}/");
                    if (Mode == "udp") Console.WriteLine($"Started at {Mode}://{localIP.Address}:{localIP.Port}/");

                    _scheduler.StartSending();

                    // UpdateCluster(CarData.Rpm, CarData.Speed, CarData.Temp, CarData.Gas, CarData.RTurn, CarData.LTurn, CarData.LBeam, CarData.HBeam, CarData.Stop, CarData.Esp, 0.0f, CarData.OilWarn, CarData.Parking, CarData.Check, CarData.Abs, CarData.Battery, CarData.Gear, CarData.Fog, false, false, false);
                    InitCluster();
                    Console.WriteLine("Press any key to stop...");

                    var odometerThread = new Thread(UpdateOdometers);
                    odometerThread.IsBackground = true;
                    odometerThread.Start();

                    if (Mode == "udp")
                    {
                        // udpSocket.BeginReceive(data, 0, data.Length, SocketFlags.None, ReceiveCallback, udpSocket);
                        byte[] data = new byte[1024];
                        EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                        udpSocket.BeginReceiveFrom(data, 0, data.Length, SocketFlags.None, ref remoteEndPoint, ReceiveCallback, udpSocket);

                        void ReceiveCallback(IAsyncResult ar)
                        {
                            try
                            {
                                EndPoint tempRemoteEP = new IPEndPoint(IPAddress.Any, 0);
                                Socket socket = (Socket)ar.AsyncState;

                                int bytesReceived = socket.EndReceiveFrom(ar, ref tempRemoteEP);

                                byte[] receivedData = new byte[bytesReceived];
                                Array.Copy(data, receivedData, bytesReceived);

                                string jsonString = Encoding.UTF8.GetString(receivedData);
                                dynamic datajs = JsonConvert.DeserializeObject(jsonString);

                                CarData.Rpm = datajs.rpm;
                                CarData.Speed = datajs.speed;
                                CarData.Temp = datajs.temp;
                                CarData.Gas = datajs.gas;
                                // CarData.Odo = data.odo;
                                CarData.RTurn = datajs.rturn;
                                CarData.LTurn = datajs.lturn;
                                CarData.LBeam = datajs.lbeam;
                                CarData.HBeam = datajs.hbeam;
                                CarData.Stop = datajs.stop;
                                CarData.OilWarn = datajs.oilwarn;
                                CarData.Parking = datajs.parking;
                                CarData.Check = datajs.check;
                                CarData.Abs = datajs.abs;
                                CarData.Battery = datajs.battery;
                                CarData.Esp = datajs.esp;
                                CarData.Gear = datajs.gear;
                                CarData.Fog = datajs.fog;
                                CarData.GearSport = datajs.gearsport;
                                CarData.GearAuto = datajs.gearauto;

                                Program.UpdateCluster(
                                    CarData.Rpm,
                                    CarData.Speed,
                                    CarData.Temp,
                                    CarData.Gas,
                                    CarData.RTurn,
                                    CarData.LTurn,
                                    CarData.LBeam,
                                    CarData.HBeam,
                                    CarData.Stop,
                                    CarData.Esp,
                                    Program.dailymileage,
                                    CarData.OilWarn,
                                    CarData.Parking,
                                    CarData.Check,
                                    CarData.Abs,
                                    CarData.Battery,
                                    CarData.Gear,
                                    CarData.Fog,
                                    CarData.GearSport,
                                    CarData.GearAuto
                            );

                                tempRemoteEP = new IPEndPoint(IPAddress.Any, 0);
                                socket.BeginReceiveFrom(data, 0, data.Length, SocketFlags.None, ref tempRemoteEP, ReceiveCallback, socket);
                            }
                            catch (JsonReaderException jex)
                            {
                                Console.WriteLine($"Error while getting from UDP JSON: {jex.Message}");
                                Console.WriteLine($"{Encoding.UTF8.GetString(data)}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error: {ex.Message}");
                            }
                        }
                    }

                    Console.ReadKey();

                    _running = false;

                    Console.WriteLine("Stoping...");
                    
                }
            }
            finally
            {
                //UpdateCluster(0, 0, 0, 0, false, false, false, false, false, false, 0.0f, false, false, false, false, false, 0, false, false, false, true);
                InitCluster();
                _running = false;
                _scheduler.StopSending();
                _adapter.CloseChannel();
                if (Mode == "ws") wssv.Stop();
            }
        }

        
        private static void UpdateOdometers()
        {
            while (Program._running)
            {
                lock (Program._lock)
                {
                    var now = DateTime.Now;
                    var deltaTime = (now - Program._lastUpdateTime).TotalHours;
                    int currentSpeed = CarData.Speed;

                    Program.dailymileage += (float)(currentSpeed * deltaTime);

                    Program._lastUpdateTime = now;
                }

                Thread.Sleep(1000); 
            }
        }

        public static void InitCluster()
        {
            var packet = _scheduler.GetPacket(0x036);
            packet.Data[0] = 0b00001110; // неизвестно
            packet.Data[1] = 0b00000000; // неизвестно
            packet.Data[2] = 0x00; // eco, 0b10000000 for on

            packet.Data[3] = (byte)(displaycontrast >> 4); // подсветка приборки и торпеды (0b00100000, dark mode - 0b00010000)
            packet.Data[3] |= 0b00100000;

            packet.Data[4] = 0b00000001;
            // зажигаение вкл (выкл -  0b00000010)

            packet.Data[5] = 0b00000000; // неизвестно
            packet.Data[6] = 0b00000000; // неизвестно
            packet.Data[7] = 0b10100000; // неизвестно

            var packet2 = _scheduler.GetPacket(0x0F6);
            packet2.Data[0] = 0b00001000; // зажигание (выкл - 0b10000110)
            packet2.Data[1] = (byte)(0 + 39); // температура двигателя

            //packet2.Data[2] = mileage1; // пробег 1 байт
            //packet2.Data[3] = mileage2; // пробег 2 байт
            //packet2.Data[4] = mileage3; //  пробег 3 байт 
            // не хочу видеть одни 999999

            packet2.Data[5] = 0b10001110; // неизвестно
            packet2.Data[6] = (byte)((35 + 39.5) * 2); // температура воздуха - фикс 35
            packet2.Data[7] = 0b00000000; // задний ход и поворотники - хз????

            var packet3 = _scheduler.GetPacket(0x0B6);
            ushort tachoValue = (ushort)(0 << 3);
            ushort speedValue = (ushort)(0 * 100);
            ushort odoValue = (ushort)(0);
            packet3.Data[0] = (byte)(tachoValue >> 8); // обороты мотора байт 1 со сдвигом на 3
            packet3.Data[1] = (byte)tachoValue; // обороты мотора
            packet3.Data[2] = (byte)(speedValue >> 8); // скорость в кмч 
            packet3.Data[3] = (byte)speedValue; // скорость в кмч
            packet3.Data[4] = (byte)(odoValue >> 8); // суточный пробег
            packet3.Data[5] = (byte)odoValue; // суточный пробег не работает!
            packet3.Data[6] = (byte)12; // потрбление топлива
            packet3.Data[7] = 0b11010000; // возможно это трансмиссия

            var packet4 = _scheduler.GetPacket(0x161);
            packet4.Data[0] = 0xFF;
            packet4.Data[1] = 0xFF;
            packet4.Data[2] = 0xFF;
            packet4.Data[3] = (byte)0; 
            packet4.Data[4] = 0xFF;
            packet4.Data[5] = 0xFF;
            packet4.Data[6] = 0xFF;

            var packet5 = _scheduler.GetPacket(0x128);
            packet5.Data[0] = 0x00;
            packet5.Data[1] = 0x00;
            packet5.Data[3] = 0x00;
            packet5.Data[4] = 0x00;
            packet5.Data[4] |= 0b10000000; 
            packet5.Data[5] = 0x00;
            packet5.Data[5] |= 0xFF; 
            packet5.Data[6] = 0x00; 
            packet5.Data[7] = 0b01010101;

            var packet6 = _scheduler.GetPacket(0x168);
            packet6.Data[0] = 0x00;
            packet6.Data[3] = 0x00;
            packet6.Data[3] |= 0b00000010;
            packet6.Data[4] |= 0b00001000;
            // packet6.Data[4] = 0x00;

            var packet7 = _scheduler.GetPacket(0x1A8);
            var odoBytes = DailyMileagePacket.ConvertToBytes((0 * 100f));
            packet7.Data[5] = odoBytes[0];
            packet7.Data[6] = odoBytes[1];
            packet7.Data[7] = odoBytes[2];
        }

        public static void UpdateCluster(int rpm, int speed, int temp, int gas, bool rturn, bool lturn, bool lbeam, bool hbeam, bool stop, bool esp, float odo, bool oilwarn, bool parking, bool check, bool abs, bool battery, int gear, bool fog, bool gearsport, bool gearauto)
        {
            rpm = Math.Abs(rpm);
            speed = Math.Abs(speed);
            if (rpm >= 7100) rpm = 7000;
            if (speed >= 270) speed = 260;
            
            var packet = _scheduler.GetPacket(0x036);
            packet.Data[0] = 0b00001110; // неизвестно
            packet.Data[1] = 0b00000000; // неизвестно
            packet.Data[2] = 0x00; // eco, 0b10000000 for on

            packet.Data[3] = (byte)(displaycontrast >> 4); // подсветка приборки и торпеды (0b00100000, dark mode - 0b00010000)
            packet.Data[3] |= 0b00100000;

            packet.Data[4] = 0b00000001;
            // зажигаение вкл (выкл -  0b00000010)

            packet.Data[5] = 0b00000000; // неизвестно
            packet.Data[6] = 0b00000000; // неизвестно
            packet.Data[7] = 0b10100000; // неизвестно

            var packet2 = _scheduler.GetPacket(0x0F6);
            packet2.Data[0] = 0b00001000; // зажигание (выкл - 0b10000110)
            packet2.Data[1] = (byte)(temp + 39); // температура двигателя

            //packet2.Data[2] = mileage1; // пробег 1 байт
            //packet2.Data[3] = mileage2; // пробег 2 байт
            //packet2.Data[4] = mileage3; //  пробег 3 байт 
            // не хочу видеть одни 999999

            packet2.Data[5] = 0b10001110; // неизвестно
            packet2.Data[6] = (byte)((35 + 39.5) * 2); // температура воздуха - фикс 35
            packet2.Data[7] = 0b00000000; // задний ход и поворотники - хз????

            var packet3 = _scheduler.GetPacket(0x0B6);
            ushort tachoValue = (ushort)(rpm << 3);
            ushort speedValue = (ushort)(speed * 100);
            ushort odoValue = (ushort)(odo);
            packet3.Data[0] = (byte)(tachoValue >> 8); // обороты мотора байт 1 со сдвигом на 3
            packet3.Data[1] = (byte)tachoValue; // обороты мотора
            packet3.Data[2] = (byte)(speedValue >> 8); // скорость в кмч 
            packet3.Data[3] = (byte)speedValue; // скорость в кмч
            packet3.Data[4] = (byte)(odoValue >> 8); // суточный пробег
            packet3.Data[5] = (byte)odoValue; // суточный пробег не работает!
            packet3.Data[6] = (byte)12; // потрбление топлива
            packet3.Data[7] = 0b11010000; // возможно это трансмиссия

            var packet4 = _scheduler.GetPacket(0x161);
            packet4.Data[0] = 0xFF;
            packet4.Data[1] = 0xFF;
            packet4.Data[2] = 0xFF;
            packet4.Data[3] = (byte)gas; // уровень топлива в %
            packet4.Data[4] = 0xFF;
            packet4.Data[5] = 0xFF;
            packet4.Data[6] = 0xFF;

            var packet5 = _scheduler.GetPacket(0x128);
            packet5.Data[0] = 0x00;
            if (gas < 20) packet5.Data[0] |= 0b00010000; // (спираль 0b00000100, ремень 0b01000000)

            packet5.Data[1] = 0x00;
            if (temp >= 120 || oilwarn || check) packet5.Data[1] |= 0b10000000; // знак внимание на экране
            if (stop) packet5.Data[1] |= 0b01000000; // stop
            // (открытая дверь 0b00010000)

            packet5.Data[3] = 0x00;
            if(rpm >= 6500) packet5.Data[3] |= 0b00000100; // (работа с дисплеем - тапок отожми!)
            // 0b00000100 - минающий тапок отожми
            // 0b00000010 - вкл тапок

            packet5.Data[4] = 0x00;
            packet5.Data[4] |= 0b10000000; // габариты
            if (lturn) packet5.Data[4] |= 0b00000010; // левый поворотник
            if (rturn) packet5.Data[4] |= 0b00000100; // правый поворотник 
            if (lbeam) packet5.Data[4] |= 0b01000000; // ближний свет
            if (hbeam) packet5.Data[4] |= 0b00100000; // дальний свет 
            if (fog) packet5.Data[4] |= 0b00010000;
            // (задние противотуманки 0b00001000, передние противотуманки 0b00010000)

            packet5.Data[5] = 0x00;
            packet5.Data[5] |= 0b10000000; // показывать пробег и суточный

            packet5.Data[6] = 0x00; // трансмиссия 
            switch (gear) {
                case -1: packet5.Data[6] |= 0b00010000; break;
                case 0: packet5.Data[6] |= 0b00100000; break;
                case 1: packet5.Data[6] |= 0b10010000; break;
                case 2: packet5.Data[6] |= 0b10000000; break;
                case 3: packet5.Data[6] |= 0b1111000; break;
                case 4: packet5.Data[6] |= 0b01100000; break;
                case 5: packet5.Data[6] |= 0b1011000; break;
                case 6: packet5.Data[6] |= 0b01000000; break;
                default: packet5.Data[6] |= 0b11000000; break;
            }
            // прочерк - 0b11000000
            // N - 0b00100000, R - 0b00010000/0b00011000, 2 - 0b10000000/0b10001000, 6 - 0b01000000/0b1001100, 4 - 0b01100000
            // D - 0b00110000, 1 - 0b10010000, 3 - 0b1111000, 5 - 0b1011000, 
            // для моргания скорости записать ласт бит в 1 -  0b00000001

            packet5.Data[7] = 0x00;
            if (gearauto) packet5.Data[7] |= 0b00000010;
            if (gearsport) packet5.Data[7] |= 0b00100000;
            // packet5.Data[7] |= 0b00110000; // неизвестно (работа с дислпеем)
            // 0b00100000 - S у скорости
            // 0b00000010/0b00000110 - auto
            // 0b00000001 - отключает скорость
            // 0b01100000 - звездочка

            var packet6 = _scheduler.GetPacket(0x168);
            packet6.Data[0] = 0x00;
            if (temp >= 120) packet6.Data[0] |= 0b10000000; // перегрев
            if (oilwarn) packet6.Data[0] |= 0b00001000; // значок масла

            packet6.Data[3] = 0x00;
            if (check) packet6.Data[3] |= 0b00000010; // чек
            if (parking) packet6.Data[3] |= 0b10000000;
            if (gas < 20) packet6.Data[3] |= 0b00000001; // топливо 
            if (abs) packet6.Data[3] |= 0b00100000; // абс
            if (esp) packet6.Data[3] |= 0b00010000; // esp
            packet6.Data[4] |= 0b00001000;

            packet6.Data[4] = 0x00;
            if (battery) packet6.Data[4] |= 0b00001100; // батарея

            // 3 бит
            //0b00100000 - esp горит
            //0b00001000; - моргает спорт и звездочка

            var packet7 = _scheduler.GetPacket(0x1A8);
            var odoBytes = DailyMileagePacket.ConvertToBytes((odo * 100f));
            packet7.Data[5] = odoBytes[0];
            packet7.Data[6] = odoBytes[1];
            packet7.Data[7] = odoBytes[2];

           /* var packet8 = _scheduler.GetPacket(0x361);
            packet8.Data[2] = 0x00;
            packet8.Data[0] = 0xFF;
            packet8.Data[1] = 0xFF;
            packet8.Data[2] = 0xFF;
            packet8.Data[3] = 0xFF;
            packet8.Data[4] = 0xFF;
            packet8.Data[5] = 0xFF;*/
        }
    }

    

    public class DailyMileagePacket
    {
        private const double ScaleFactor = 0.1; 
        private const int MaxKm = 1677721; 

        public static byte[] ConvertToBytes(float kilometers)
        {
            if (kilometers < 0 || kilometers > MaxKm)
                throw new ArgumentException($"Value must be between 0 and {MaxKm}");

            uint value = (uint)(kilometers / ScaleFactor);
            return new byte[]
            {
            (byte)((value >> 16) & 0xFF), 
            (byte)((value >> 8) & 0xFF),  
            (byte)(value & 0xFF)        
            };
        }
    }

    public class Laputa : WebSocketBehavior
    {
        protected override void OnMessage(MessageEventArgs e)
        {
            try
            {
                Program.isData = true;
                dynamic data = JsonConvert.DeserializeObject(e.Data);

                CarData.Rpm = data.rpm;
                CarData.Speed = data.speed;
                CarData.Temp = data.temp;
                CarData.Gas = data.gas;
                // CarData.Odo = data.odo;
                CarData.RTurn = data.rturn;
                CarData.LTurn = data.lturn;
                CarData.LBeam = data.lbeam;
                CarData.HBeam = data.hbeam;
                CarData.Stop = data.stop;
                CarData.OilWarn = data.oilwarn;
                CarData.Parking = data.parking;
                CarData.Check = data.check;
                CarData.Abs = data.abs;
                CarData.Battery = data.battery;
                CarData.Esp = data.esp;
                CarData.Gear = data.gear;
                CarData.Fog = data.fog;
                CarData.GearSport = data.gearsport;
                CarData.GearAuto = data.gearauto;

                Program.UpdateCluster(
                    CarData.Rpm,
                    CarData.Speed,
                    CarData.Temp,
                    CarData.Gas,
                    CarData.RTurn,
                    CarData.LTurn,
                    CarData.LBeam,
                    CarData.HBeam,
                    CarData.Stop,
                    CarData.Esp,
                    Program.dailymileage,
                    CarData.OilWarn,
                    CarData.Parking,
                    CarData.Check,
                    CarData.Abs,
                    CarData.Battery,
                    CarData.Gear,
                    CarData.Fog,
                    CarData.GearSport,
                    CarData.GearAuto
            );

                // Program.isData = true;

            }
            catch (Exception ex)
            {
                // Обработка ошибок парсинга
                Console.WriteLine($"Error parsing JSON: {ex.Message}");
            }
        }
    }
}
