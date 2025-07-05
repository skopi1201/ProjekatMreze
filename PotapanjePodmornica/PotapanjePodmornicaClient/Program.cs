using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace PotapanjePodmornicaClient
{
    internal class Program
    {
        static void Main(string[] args)
        {
            int localPort = 9001; // Change to 9001 for Client 1
            int serverPort = 9000;
            string serverIP = "127.0.0.1";

            bool isMyTurn = false;

            using (UdpClient client = new UdpClient(localPort))
            {
                IPEndPoint serverEP = new IPEndPoint(IPAddress.Parse(serverIP), serverPort);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write("Write something to connect: ");
                string connectText = Console.ReadLine();
                Console.ResetColor();

                byte[] connectMsg = Encoding.UTF8.GetBytes("connect");
                client.Send(connectMsg, connectMsg.Length, serverEP);

                Thread receiveThread = new Thread(() =>
                {
                    while (true)
                    {
                        IPEndPoint remote = null;
                        byte[] data = client.Receive(ref remote);
                        string msg = Encoding.UTF8.GetString(data);

                        if (msg.StartsWith("Server: Your turn"))
                        {
                            isMyTurn = true;
                            Console.ForegroundColor = ConsoleColor.White;
                            Console.Write("Aim: ");
                            Console.ResetColor();
                        }
                        else if (msg.StartsWith("Server: Wait!"))
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("Wait Your Turn:");
                            Console.ResetColor();
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.WriteLine($"Other Player's Aim: {msg}");
                            Console.ResetColor();
                        }
                    }
                });

                receiveThread.IsBackground = true;
                receiveThread.Start();

                while (true)
                {
                    if (!isMyTurn)
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    string input = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(input)) continue;

                    byte[] toSend = Encoding.UTF8.GetBytes(input);
                    client.Send(toSend, toSend.Length, serverEP);

                    isMyTurn = false;
                }
            }
        }
    }
}
