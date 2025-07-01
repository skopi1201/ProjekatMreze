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
            string serverIP = "127.0.0.1"; //server ip
            int serverUdpPort = 9000;      //server port
            int clientUdpPort = 9001;      //klijent 1 port


            using (UdpClient clientOne = new UdpClient(clientUdpPort))   //klijent objekat i stavljamo adresu 9001
            {
                IPEndPoint server = new IPEndPoint(IPAddress.Parse(serverIP), serverUdpPort); //server ovjekat kreira se sa ip i port sa adresom

                while (true)
                {
                    // Klijent šalje poruku prvi
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("You say: ");
                    string messageToSend = Console.ReadLine();
                    byte[] dataToSend = Encoding.UTF8.GetBytes(messageToSend);
                    clientOne.Send(dataToSend, dataToSend.Length, server);
                    Console.ResetColor();

                    // Čekaj odgovor od servera
                    IPEndPoint fromEP = new IPEndPoint(IPAddress.Any, 0);
                    byte[] receivedData = clientOne.Receive(ref fromEP);
                    string receivedMessage = Encoding.UTF8.GetString(receivedData);
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Server says: {receivedMessage}");
                    Console.ResetColor() ;
                }
            }
        }
    }
}
