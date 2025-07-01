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
            string serverIP = "127.0.0.1";
            int uPortSr = 9000;
            int uPortC1 = 9001;
            int uPortC2 = 9002;

            bool c1IsActive = true;
            bool c2IsActive = true;

            //test

            using (UdpClient udpServer = new UdpClient(uPortSr))   //server objekat
            {
                IPEndPoint clientOne = new IPEndPoint(IPAddress.Any, 0);   //client1 objekat
                string receivedMessage1 = null;

                while (c1IsActive)
                {
                    //recieve
                    byte[] receivedData1 = udpServer.Receive(ref clientOne);
                    receivedMessage1 = Encoding.UTF8.GetString(receivedData1);

                    string kojiClient = receivedMessage1.Substring(0,2);
                    receivedMessage1 = receivedMessage1.Substring(3);

                    if (kojiClient == "c1")
                        Console.WriteLine($"Client 1 says: {receivedMessage1}");
                    else if (kojiClient == "c2")
                        Console.WriteLine($"Client 2 says: {receivedMessage1}");
                }            
            }
        }
    }
}
