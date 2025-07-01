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
            int ServerUdpPort = 9000;
            int clientOneUdpPort = 9001;
           

            using (UdpClient udpServer = new UdpClient(ServerUdpPort))   // server na mrezi porta 9000 server objekat
            {
                IPEndPoint clientOne = new IPEndPoint(IPAddress.Any, clientOneUdpPort);   //bilo koji klijent jer prima od bilo koga
                string receivedMessage = null;

                while (true)
                {
                    // Čekaj poruku od klijenta
                    byte[] receivedData = udpServer.Receive(ref clientOne);    // server reciveuje poruku od klijenta
                    receivedMessage = Encoding.UTF8.GetString(receivedData);  //poruka se iz byte pretvara u string
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Client says: {receivedMessage}");
                    Console.ResetColor();

                    // Sada je tvoj red za slanje
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write("You say: ");
                    string messageToSend = Console.ReadLine();              //poruka
                    byte[] sendData = Encoding.UTF8.GetBytes(messageToSend);//poruka se encodeuje u byte tip
                    udpServer.Send(sendData, sendData.Length, clientOne);    //poruka se salje na server (port 9000)
                    Console.ResetColor();

                }
            }
        }
    }
}
