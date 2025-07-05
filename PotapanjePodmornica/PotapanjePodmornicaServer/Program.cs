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
            int serverPort = 9000;

            using (UdpClient udpServer = new UdpClient(serverPort))
            {
                Console.WriteLine("Server started on port 9000...");

                IPEndPoint player1 = null;
                IPEndPoint player2 = null;

                bool p1Turn = true;
                bool p2Turn = false;
                bool gameStarted = false;

                while (true)
                {
                    IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = udpServer.Receive(ref remoteEP);
                    string message = Encoding.UTF8.GetString(data);

                    // Assign players
                    if (player1 == null)
                    {
                        player1 = remoteEP;
                        Console.WriteLine($"Registered Player 1: {player1}");
                        continue;
                    }
                    if (player2 == null && !remoteEP.Equals(player1))
                    {
                        player2 = remoteEP;
                        Console.WriteLine($"Registered Player 2: {player2}");

                        // Game start — Player 1 goes first
                        byte[] startMsg = Encoding.UTF8.GetBytes("Server: Your turn");
                        udpServer.Send(startMsg, startMsg.Length, player1);

                        gameStarted = true;
                        Console.WriteLine("Game started. Player 1 goes first.");
                        continue;
                    }

                    if (!gameStarted) continue;

                    bool isPlayer1 = remoteEP.Equals(player1);
                    bool isPlayer2 = remoteEP.Equals(player2);

                    if (isPlayer1 && p1Turn)
                    {
                        Console.WriteLine($"Player 1: {message}");
                        byte[] toP2 = Encoding.UTF8.GetBytes(message);
                        udpServer.Send(toP2, toP2.Length, player2);

                        byte[] turnMsg = Encoding.UTF8.GetBytes("Server: Your turn");
                        udpServer.Send(turnMsg, turnMsg.Length, player2);

                        p1Turn = false;
                        p2Turn = true;
                    }
                    else if (isPlayer2 && p2Turn)
                    {
                        Console.WriteLine($"Player 2: {message}");
                        byte[] toP1 = Encoding.UTF8.GetBytes(message);
                        udpServer.Send(toP1, toP1.Length, player1);

                        byte[] turnMsg = Encoding.UTF8.GetBytes("Server: Your turn");
                        udpServer.Send(turnMsg, turnMsg.Length, player1);

                        p2Turn = false;
                        p1Turn = true;
                    }
                    else
                    {
                        byte[] waitMsg = Encoding.UTF8.GetBytes("Server: Wait! It's not your turn.");
                        udpServer.Send(waitMsg, waitMsg.Length, remoteEP);
                    }
                }
            }
        }
    }
}
