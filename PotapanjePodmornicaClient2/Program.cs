using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PotapanjePodmornicaClient2
{
    internal class Program
    {
        static void Main(string[] args)
        {
            string serverIP = "127.0.0.1";
            int uPortSr = 9000;
            int uPortC1 = 9001;
            int uPortC2 = 9002;


            using (UdpClient clientTwo = new UdpClient(uPortC2))   //objekat client1
            {
                IPEndPoint server = new IPEndPoint(IPAddress.Parse(serverIP), uPortSr); //objekat server

                while (true)
                {
                    //send
                    Console.Write("You say: ");
                    string messageToSend = Console.ReadLine();
                    messageToSend = "c2 " + messageToSend;
                    byte[] dataToSend = Encoding.UTF8.GetBytes(messageToSend);
                    clientTwo.Send(dataToSend, dataToSend.Length, server);

                   

                }
            }
        }
    }
}
