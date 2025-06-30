using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PotapanjePodmornicaClient
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string serverIP = "127.0.0.1";
            int serverUdpPort = 9000;

            
            using (UdpClient udpClient = new UdpClient())
            {
                IPEndPoint serverEP = new IPEndPoint(IPAddress.Parse(serverIP), serverUdpPort);

                while (true)
                {
                    // Klijent šalje poruku prvi
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("You say: ");
                    string messageToSend = Console.ReadLine();
                    byte[] dataToSend = Encoding.UTF8.GetBytes(messageToSend);
                    udpClient.Send(dataToSend, dataToSend.Length, serverEP);
                    Console.ResetColor();

                    // Čekaj odgovor od servera
                    IPEndPoint fromEP = new IPEndPoint(IPAddress.Any, 0);
                    byte[] receivedData = udpClient.Receive(ref fromEP);
                    string receivedMessage = Encoding.UTF8.GetString(receivedData);
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Server says: {receivedMessage}");
                    Console.ResetColor() ;
                }
            }
        }
    }
}
