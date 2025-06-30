using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PotapanjePodmornicaServer
{
    internal class Program
    {
        static void Main(string[] args)
        {
            int udpPort = 9000;
           

            using (UdpClient udpServer = new UdpClient(udpPort))
            {
                IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

                

                string receivedMessage = null;

                while (true)
                {
                    // Čekaj poruku od klijenta
                    byte[] receivedData = udpServer.Receive(ref remoteEP);
                    receivedMessage = Encoding.UTF8.GetString(receivedData);

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Client says: {receivedMessage}");
                    Console.ResetColor();

                    // Sada je tvoj red za slanje
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write("You say: ");
                    string messageToSend = Console.ReadLine();
                    byte[] sendData = Encoding.UTF8.GetBytes(messageToSend);
                    udpServer.Send(sendData, sendData.Length, remoteEP);
                    Console.ResetColor();

                }
            }
        }
    }
}
