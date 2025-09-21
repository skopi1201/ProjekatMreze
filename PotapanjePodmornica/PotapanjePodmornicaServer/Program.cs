using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;


namespace PotapanjePodmornicaServer
{
    internal class Program
    {
        // ===== dinamika table i brodova (podesava se na startu) =====
        static int ROWS, COLS, CELLS, SHIPS_PER_PLAYER;

        // ===== limit promasaja
        static int MISS_LIMIT;
       
        class Player
        {
            public int Id;              // 1 ili 2
            public Socket Tcp;          // TCP konekcija
            public bool[,] Ships;       // true ako postoji brod u [r,c]
            public char[,] Board;       // pogled napadača: '_' nepoznato, 'M' promašaj, 'H' pogodak
            public int RemainingShips;  // preostali brodovi
            public int Misses;          // broj promašaja (za limit)
        }

        static void Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Cyan; // jedinstvena boja servera

            const int UdpPort = 9000;
            const int TcpPort = 50001;

          
            // === 0) Izbor dimenzija table ===
            Console.Write("Unesi velicinu table (3 ili 4): ");
            if (!int.TryParse(Console.ReadLine(), out int size) || (size != 3 && size != 4))
            {
                Console.WriteLine("Nepoznata velicina. Default = 3x3.");
                size = 3;
            }
            ROWS = COLS = size;
            CELLS = ROWS * COLS;

            if (size == 3)
            {
                SHIPS_PER_PLAYER = 3;
                MISS_LIMIT = 5;
            }
            else // size == 4
            {
                SHIPS_PER_PLAYER = 5;
                MISS_LIMIT = 8;
            }

          
            SHIPS_PER_PLAYER = (size == 3) ? 3 : 5;
            Console.WriteLine($"[SERVER] Mapa: {ROWS}x{COLS}, Brodovi po igracu: {SHIPS_PER_PLAYER}, Limit promasaja: {MISS_LIMIT}");

            Process.Start(@"..\..\..\PotapanjePodmornicaClient\bin\Debug\PotapanjePodmornicaClient.exe");
            Process.Start(@"..\..\..\PotapanjePodmornicaClient\bin\Debug\PotapanjePodmornicaClient.exe");



            // === 1) TCP listener (pre UDP da izbegnemo "refused") ===
            var tcpListener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            tcpListener.Bind(new IPEndPoint(IPAddress.Any, TcpPort));
            tcpListener.Listen(2);
            Console.WriteLine($"[TCP] Listening on :{TcpPort}");

            // === 2) UDP registracija: očekujemo 2x "PRIJAVA" ===
            var udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            udp.Bind(new IPEndPoint(IPAddress.Any, UdpPort));
            Console.WriteLine($"[UDP] Listening on :{UdpPort} for 'PRIJAVA'…");

            var playersUdp = new Dictionary<int, EndPoint>();
            while (playersUdp.Count < 2)
            {
                byte[] buf = new byte[1024];
                EndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                int n = udp.ReceiveFrom(buf, ref sender);
                string msg = Encoding.UTF8.GetString(buf, 0, n).Trim();

                if (!msg.Equals("PRIJAVA", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[UDP] Ignored '{msg}' from {sender}");
                    continue;
                }

                int assignedId;
                if (!playersUdp.ContainsKey(1)) assignedId = 1;
                else if (!playersUdp.ContainsKey(2) && !EndPointsEqual(playersUdp[1], sender)) assignedId = 2;
                else assignedId = -1;
                if (assignedId == -1) continue;

                playersUdp[assignedId] = sender;
                udp.SendTo(Encoding.UTF8.GetBytes($"registered {assignedId}; connect-tcp {TcpPort}"), sender);
                Console.WriteLine($"[UDP] Registered Player {assignedId}: {sender}");
            }

            Console.WriteLine("[UDP] Two players registered. Proceeding with TCP…");

            // === 3) TCP prihvat (hello <id>) ===
            Player P1 = null, P2 = null;
            while (P1 == null || P2 == null)
            {
                Socket s = tcpListener.Accept();
                s.NoDelay = true;
                int id = ReadHelloId(s);
                if (id == 1 && P1 == null) P1 = NewPlayer(1, s);
                else if (id == 2 && P2 == null) P2 = NewPlayer(2, s);
                else s.Close();
            }

            // Sve partije u jednoj petlji (restart podrška)
            bool continuePlaying = true;
            while (continuePlaying)
            {
                // === 4) Setup s(aljemo dimenzije i broj brodova, pa trzimo raspored) ===
                SendString(P1.Tcp, $"server: setup {ROWS} {COLS} {SHIPS_PER_PLAYER} {MISS_LIMIT}");
                SendString(P2.Tcp, $"server: setup {ROWS} {COLS} {SHIPS_PER_PLAYER} {MISS_LIMIT}");

                ResetPlayerForNewMatch(P1);
                ResetPlayerForNewMatch(P2);

                RequestShips(P1);
                RequestShips(P2);

                SendString(P1.Tcp, "server: setup-ok");
                SendString(P2.Tcp, "server: setup-ok");

                // === 5) Game loop ===
                bool p1Turn = true;
                bool gameOver = false;
                var recvBuf = new byte[2048];

                // Start: samo napadac dobija tablu
                SendString(P1.Tcp, "server: match ready; your turn");
                SendBoardToAttacker(P1.Tcp, P2);
                SendString(P2.Tcp, "server: match ready; wait");

                try
                {
                    while (!gameOver)
                    {
                        Player attacker = p1Turn ? P1 : P2;
                        Player defender = p1Turn ? P2 : P1;

                        int n = attacker.Tcp.Receive(recvBuf);
                        if (n <= 0) break;

                        // parsiramo prvi int iz bilo cega sto stigne
                        string raw = Encoding.UTF8.GetString(recvBuf, 0, n);
                        if (!TryExtractFirstInt(raw, out int cell))
                        {
                            SendString(attacker.Tcp, "server: invalid input (unesi broj).");
                            continue;
                        }
                        if (cell < 1 || cell > CELLS)
                        {
                            SendString(attacker.Tcp, $"server: invalid cell (opseg 1..{CELLS}).");
                            continue;
                        }

                        CellToRC(cell, COLS, out int r, out int c);

                        // ako je vec gadjano
                        if (defender.Board[r, c] == 'M' || defender.Board[r, c] == 'H')
                        {
                            SendString(attacker.Tcp, "server: vec gadjano to polje. Izaberi drugo.");
                            continue;
                        }

                        // primeni pogodak / promasaj
                        if (defender.Ships[r, c])
                        {
                            defender.Ships[r, c] = false;
                            defender.Board[r, c] = 'H';
                            defender.RemainingShips--;

                            // evidencija (log)
                            Console.WriteLine($"[Igrač {attacker.Id}] -> [Igrač {defender.Id}]: polje {cell}, POGODIO");

                            // obavesti oba
                            SendString(P1.Tcp, $"server: P{attacker.Id} gadja {cell} -> POGODIO");
                            SendString(P2.Tcp, $"server: P{attacker.Id} gadja {cell} -> POGODIO");

                            // game over po brodu
                            if (defender.RemainingShips == 0)
                            {
                                SendString(attacker.Tcp, "server: GAME OVER - POBEDILI STE");
                                SendString(defender.Tcp, "server: GAME OVER - IZGUBILI STE");
                                gameOver = true;
                                break;
                            }
                        }
                        else
                        {
                            defender.Board[r, c] = 'M';
                            attacker.Misses++;

                            // evidencija (log)
                            Console.WriteLine($"[Igrač {attacker.Id}] -> [Igrač {defender.Id}]: polje {cell}, PROMAŠIO (#{attacker.Misses})");

                            // obavesti oba
                            SendString(P1.Tcp, $"server: P{attacker.Id} gadja {cell} -> PROMASIO");
                            SendString(P2.Tcp, $"server: P{attacker.Id} gadja {cell} -> PROMASIO");

                            // upozorenje kad ostanu jos 2 do limita
                            int remaining = MISS_LIMIT - attacker.Misses;
                            if (remaining == 2)
                                SendString(attacker.Tcp, $"server: upozorenje: imate jos 2 promasaja pre poraza (limit {MISS_LIMIT}).");

                            // poraz po limitu promasaja
                            if (attacker.Misses >= MISS_LIMIT)
                            {
                                SendString(attacker.Tcp, "server: GAME OVER - IZGUBILI STE (limit promasaja)");
                                SendString(defender.Tcp, "server: GAME OVER - POBEDILI STE (protivnik dostigao limit promasaja)");
                                gameOver = true;
                                break;
                            }
                        }

                        // sledeci potez: samo buduci napadac dobija tablu
                        p1Turn = !p1Turn;
                        if (p1Turn)
                        {
                            SendString(P1.Tcp, "server: your turn");
                            SendBoardToAttacker(P1.Tcp, P2); // osvezi napadacu
                            SendString(P2.Tcp, "server: wait");
                        }
                        else
                        {
                            SendString(P2.Tcp, "server: your turn");
                            SendBoardToAttacker(P2.Tcp, P1);
                            SendString(P1.Tcp, "server: wait");
                        }
                    }
                }
                finally
                {
                    // === 6) Restart prompt ===
                    SendString(P1.Tcp, "server: nova-igra? (DA/NE)");
                    SendString(P2.Tcp, "server: nova-igra? (DA/NE)");

                    bool p1Yes = ReceiveYesNo(P1.Tcp);
                    bool p2Yes = ReceiveYesNo(P2.Tcp);

                    if (p1Yes && p2Yes)
                    {
                        Console.WriteLine("[SERVER] Nova partija krece…");
                        continuePlaying = true;
                    }
                    else
                    {
                        continuePlaying = false;
                        SendString(P1.Tcp, "server: kraj");
                        SendString(P2.Tcp, "server: kraj");
                    }
                }
            } // end while(continuePlaying)

            // === kraj ===
            SafeClose(P1.Tcp);
            SafeClose(P2.Tcp);
            Console.WriteLine("[SERVER] Shutdown.");
        }

        // ========================= Helpers =========================

        // kreira i inicijalizuje igraca (bez resetovanja za novu partiju)
        static Player NewPlayer(int id, Socket s)
        {
            SendString(s, $"server: connected as player {id}");
            return new Player
            {
                Id = id,
                Tcp = s
            };
        }

        // resetuje stanje igraca za novu partiju (table, brodovi, brojaci)
        static void ResetPlayerForNewMatch(Player p)
        {
            p.Ships = new bool[ROWS, COLS];
            p.Board = NewCharGrid('_');
            p.RemainingShips = SHIPS_PER_PLAYER;
            p.Misses = 0;
        }

        // kreira novu ROWS×COLS matricu popunjenu zadatim znakom
        static char[,] NewCharGrid(char fill)
        {
            var g = new char[ROWS, COLS];
            for (int i = 0; i < ROWS; i++)
                for (int j = 0; j < COLS; j++)
                    g[i, j] = fill;
            return g;
        }

       
        // trazi od igraca da posalje koordinate brodova dok ne dobijemo ispravan raspored
       
        static void RequestShips(Player p)
        {
            while (true)
            {
                SendString(p.Tcp, $"server: send-ships {SHIPS_PER_PLAYER}");
                string line = ReceiveLine(p.Tcp);
                if (!line.StartsWith("ships:", StringComparison.OrdinalIgnoreCase))
                {
                    SendString(p.Tcp, "server: expected 'ships: <brojevi>'");
                    continue;
                }

                var parts = line.Substring(6)
                                .Replace("\0", "").Replace("\r", "\n")
                                .Split(new[] { ' ', ',', ';', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length != SHIPS_PER_PLAYER)
                {
                    SendString(p.Tcp, $"server: treba tacno {SHIPS_PER_PLAYER} broja.");
                    continue;
                }

                // reset polja
                for (int i = 0; i < ROWS; i++)
                    for (int j = 0; j < COLS; j++)
                        p.Ships[i, j] = false;

                var used = new bool[CELLS + 1];
                bool ok = true;

                foreach (var tok in parts)
                {
                    if (!int.TryParse(tok, out int cell) || cell < 1 || cell > CELLS || used[cell])
                    { ok = false; break; }

                    used[cell] = true;
                    CellToRC(cell, COLS, out int r, out int c);
                    p.Ships[r, c] = true;
                }

                if (ok)
                {
                    p.Board = NewCharGrid('_');
                    p.RemainingShips = SHIPS_PER_PLAYER;
                    return;
                }

                SendString(p.Tcp, $"server: neispravan unos (opseg 1..{CELLS}, bez duplikata). Pokusaj ponovo.");
            }
        }

        //prima jedan TCP paket i vraca ga kao trimovan string
        static string ReceiveLine(Socket s)
        {
            var buf = new byte[4096];
            int n = s.Receive(buf);
            if (n <= 0) return "";
            return Encoding.UTF8.GetString(buf, 0, n).Trim();
        }

        // prima "DA" ili "NE" (case-insensitive) vraca true samo za "DA"
        static bool ReceiveYesNo(Socket s)
        {
            string line = ReceiveLine(s);
            return line.Equals("DA", StringComparison.OrdinalIgnoreCase);
        }

        // salje protivnicku tablu napadacu (simboli _, M, H)
        static void SendBoardToAttacker(Socket attackerSock, Player defender)
        {
            var sb = new StringBuilder();
            sb.AppendLine("server: board");
            for (int i = 0; i < ROWS; i++)
            {
                for (int j = 0; j < COLS; j++)
                {
                    sb.Append(defender.Board[i, j]);
                    if (j < COLS - 1) sb.Append(' ');
                }
                sb.AppendLine();
            }
            SendString(attackerSock, sb.ToString());
        }

        // Ekstrahuje prvi celobrojni token iz stringa (tolerantno na CR/LF/null/visak teksta)
        static bool TryExtractFirstInt(string s, out int value)
        {
            value = -1;
            var parts = s.Replace("\0", "").Replace("\r", "\n")
                         .Split(new[] { ' ', ',', ';', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var tok in parts)
                if (int.TryParse(tok, out value)) return true;
            return false;
        }

        // Prevodi 1..(ROWS*COLS) u par [r,c]
        static void CellToRC(int cell1Based, int cols, out int r, out int c)
        {
            int idx = cell1Based - 1;
            r = idx / cols;
            c = idx % cols;
        }

        // Prima "hello N" i vraća N, ili -1 ako je neispravno
        static int ReadHelloId(Socket s)
        {
            var buf = new byte[128];
            int n = s.Receive(buf);
            var text = Encoding.UTF8.GetString(buf, 0, n).Trim();
            if (text.StartsWith("hello", StringComparison.OrdinalIgnoreCase))
            {
                var parts = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(parts[1], out int id)) return id;
            }
            return -1;
        }

        // salje UTF-8 paket 
        static void SendString(Socket s, string text)
        {
            var data = Encoding.UTF8.GetBytes(text);
            s.Send(data);
        }

        //poredi IPEndPoint adrese i portove. 
        static bool EndPointsEqual(EndPoint a, EndPoint b)
        {
            var ia = (IPEndPoint)a; var ib = (IPEndPoint)b;
            return ia.Address.Equals(ib.Address) && ia.Port == ib.Port;
        }

        // gasi soket
        static void SafeClose(Socket s)
        {
            try { s?.Shutdown(SocketShutdown.Both); } catch { }
            try { s?.Close(); } catch { }
        }
    }
}
