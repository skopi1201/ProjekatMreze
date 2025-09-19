using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace PotapanjePodmornicaClient
{
    internal class Program
    {
        // Maksimalan broj ćelija (iz setup poruke).
        static volatile int cellsMax = 9;

        // Tokovi unosa.
        static volatile bool awaitingShips = false;     // unos brodova
        static volatile bool awaitingRestart = false;   // unos "DA/NE" posle game over-a
        static volatile bool promptAfterBoard = false;  // kada dobijemo "your turn", čekamo board pa prompt

        // Id i akcent boja klijenta (1=zeleno, 2=magenta).
        static volatile int playerId = 0;
        static ConsoleColor Accent => (playerId == 1) ? ConsoleColor.Green : ConsoleColor.Magenta;

        static void Main(string[] args)
        {
            const string ServerIp = "127.0.0.1";
            const int UdpPort = 9000;

            // === 1) UDP prijava ===
            var udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            udp.Bind(new IPEndPoint(IPAddress.Any, 0));
            EndPoint serverUdp = new IPEndPoint(IPAddress.Parse(ServerIp), UdpPort);

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("Pritisni Enter za prijavu: ");
            Console.ReadLine();
            Console.ResetColor();

            udp.SendTo(Encoding.UTF8.GetBytes("PRIJAVA"), serverUdp);

            var buf = new byte[256];
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            int n = udp.ReceiveFrom(buf, ref remote);
            string reg = Encoding.UTF8.GetString(buf, 0, n).Trim();
            ParseRegistration(reg, out playerId, out int tcpPort);
            udp.Close();

            // === 2) TCP konekcija i identifikacija ===
            var tcp = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            tcp.Connect(new IPEndPoint(IPAddress.Parse(ServerIp), tcpPort));
            tcp.NoDelay = true;

            SendString(tcp, $"hello {playerId}");

            Console.ForegroundColor = Accent;
            Console.WriteLine($"[TCP] Connected as Player {playerId}.");
            Console.ResetColor();

            // === 3) Receiver nit (parsira i obrađuje sve poruke) ===
            var recvThread = new Thread(() => ReceiverLoop(tcp)) { IsBackground = true };
            recvThread.Start();

            // === 4) Sender petlja (korisnički unos) ===
            while (true)
            {
                string line = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.Equals("/quit", StringComparison.OrdinalIgnoreCase)) break;

                if (awaitingRestart)
                {
                    // Server očekuje "DA" za restart ili bilo šta drugo za kraj.
                    SendString(tcp, line.Trim());
                    continue;
                }

                if (awaitingShips)
                {
                    // Slati tačno onako kako server očekuje.
                    SendString(tcp, "ships: " + line.Trim());
                    continue;
                }

                // Inače: gađanje – mora ceo broj u opsegu [1..cellsMax]
                string trimmed = line.Trim();
                if (int.TryParse(trimmed, out int cell))
                {
                    if (cell < 1 || cell > cellsMax)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"[CLIENT] Unesi broj 1..{cellsMax}.");
                        Console.ResetColor();
                        continue;
                    }
                    SendString(tcp, trimmed);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[CLIENT] Unesi ceo broj (bez teksta).");
                    Console.ResetColor();
                }
            }

            SafeClose(tcp);
        }

        /// <summary>
        /// Prima TCP fragmente, deli ih na pojedinačne "server:" poruke
        /// i svaku prosleđuje na obradu. Radi i kad više poruka stigne u jednoj porciji.
        /// </summary>
        static void ReceiverLoop(Socket tcp)
        {
            var rbuf = new byte[8192];

            try
            {
                while (true)
                {
                    int m = tcp.Receive(rbuf);
                    if (m <= 0) break;
                    string raw = Encoding.UTF8.GetString(rbuf, 0, m);

                    foreach (var msg in SplitServerMessages(raw))
                        HandleServerMessage(msg);
                }
            }
            catch { }
            finally
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[TCP] Disconnected.");
                Environment.Exit(0);
                Console.ResetColor();
            }
        }

        /// <summary>
        /// Obrada jedne "server:" poruke.
        /// </summary>
        static void HandleServerMessage(string raw)
        {
            string line = raw.Trim();

            // --- Board: uvek iscrtavamo, pa tek onda prompt (ako ga čekamo) ---
            if (line.StartsWith("server: board", StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine();
                Console.WriteLine("Opponent board  (_=unknown  M=miss  H=hit)");
                var lines = line.Split('\n');
                for (int i = 1; i < lines.Length; i++)
                {
                    var row = lines[i].TrimEnd('\r');
                    if (!string.IsNullOrWhiteSpace(row)) Console.WriteLine(row);
                }
                Console.ResetColor();

                if (promptAfterBoard)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write($"Cell (1..{cellsMax}): ");
                    Console.ResetColor();
                    promptAfterBoard = false;
                }
                return;
            }

            // --- Setup: "server: setup R C K MISS_LIMIT" ---
            // --- Setup: "server: setup R C K MISS_LIMIT" ---
            if (line.StartsWith("server: setup", StringComparison.OrdinalIgnoreCase))
            {
                // Reset state for new game
                awaitingShips = false;
                awaitingRestart = false;
                promptAfterBoard = false;

                Console.Clear(); // ocisti ekran za novu partiju (opciono)

                var parts = line.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 6 &&
                    int.TryParse(parts[2], out int R) &&
                    int.TryParse(parts[3], out int C) &&
                    int.TryParse(parts[4], out int K) &&
                    int.TryParse(parts[5], out int missLimit))
                {
                    cellsMax = R * C;
                    Console.ForegroundColor = Accent;
                    Console.WriteLine($"[SERVER] Nova igra! Board: {R}x{C}, ships: {K}, miss limit: {missLimit}");
                    Console.WriteLine($"Unesi tacno {K} brojeva (1..{cellsMax}), npr: 2 5 9");
                    Console.ResetColor();
                }
                return;
            }


            // --- Server traži brodove ---
            if (line.StartsWith("server: send-ships", StringComparison.OrdinalIgnoreCase))
            {
                awaitingShips = true;
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("Ships: ");
                Console.ResetColor();
                return;
            }

            // --- Setup završen ---
            if (line.StartsWith("server: setup-ok", StringComparison.OrdinalIgnoreCase))
            {
                awaitingShips = false;
                Console.ForegroundColor = Accent;
                Console.WriteLine("[SERVER] Ship placement accepted.");
                Console.ResetColor();
                return;
            }

            // --- Na potezu / čekanje (crveni tekst, bez pozadine). Prompt ide posle board-a. ---
            if (line.StartsWith("server: your turn", StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("YOUR TURN");
                Console.ResetColor();
                promptAfterBoard = true; // očekujemo da server odmah potom pošalje board
                return;
            }
            if (line.StartsWith("server: wait", StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("WAIT YOUR TURN");
                Console.ResetColor();
                promptAfterBoard = false;
                return;
            }

            // --- Restart prompt ---
            if (line.StartsWith("server: nova-igra?", StringComparison.OrdinalIgnoreCase))
            {
                awaitingRestart = true;
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("Nova igra? (DA/NE): ");
                Console.ResetColor();
                return;
            }
            if (line.StartsWith("server: kraj", StringComparison.OrdinalIgnoreCase))
            {
                awaitingRestart = false;
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine("KRAJ");
                Console.ResetColor();
                return;
            }

            // --- Informacije / rezultati / upozorenja ---
            if (line.StartsWith("server: P", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("server: invalid", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("server: vec gadjano", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("server: connected as player", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("server: match ready", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("server: upozorenje", StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = Accent;
                Console.WriteLine(line.Replace("server:", "[SERVER]").Trim());
                Console.ResetColor();
                return;
            }

            // --- Game over ---
            if (line.StartsWith("server: GAME OVER", StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = line.Contains("POBEDILI") ? ConsoleColor.Green : ConsoleColor.Red;
                Console.WriteLine("\n" + line.Replace("server:", "[GAME]").Trim());
                Console.ResetColor();
                promptAfterBoard = false;
                return;
            }

            // --- Fallback ---
            Console.ForegroundColor = Accent;
            Console.WriteLine(line.Replace("server:", "[SERVER]").Trim());
            Console.ResetColor();
        }

        /// <summary>
        /// Deli TCP chunk na pojedinačne "server:" poruke.
        /// Radi i kada više poruka stigne u istom paketu.
        /// </summary>
        static IEnumerable<string> SplitServerMessages(string chunk)
        {
            const string tag = "server:";
            var idxs = new List<int>();
            int i = 0;
            while (true)
            {
                int p = chunk.IndexOf(tag, i, StringComparison.OrdinalIgnoreCase);
                if (p < 0) break;
                idxs.Add(p);
                i = p + tag.Length;
            }

            if (idxs.Count == 0)
            {
                yield return tag + " " + chunk.Trim();
                yield break;
            }
            if (idxs.Count == 1)
            {
                yield return chunk.Substring(idxs[0]);
                yield break;
            }

            for (int k = 0; k < idxs.Count; k++)
            {
                int start = idxs[k];
                int end = (k + 1 < idxs.Count) ? idxs[k + 1] : chunk.Length;
                yield return chunk.Substring(start, end - start);
            }
        }

        /// <summary> Parsira "registered &lt;id&gt;; connect-tcp &lt;port&gt;" iz UDP odgovora. </summary>
        static void ParseRegistration(string reg, out int pId, out int tcpPort)
        {
            pId = -1; tcpPort = -1;
            var parts = reg.Split(new[] { ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4 && int.TryParse(parts[1], out var id) && int.TryParse(parts[3], out var port))
            { pId = id; tcpPort = port; }
        }

        /// <summary> Šalje UTF-8 tekst serveru. </summary>
        static void SendString(Socket s, string text)
        {
            var data = Encoding.UTF8.GetBytes(text);
            s.Send(data);
        }

        /// <summary> Bezbedno gašenje soketa. </summary>
        static void SafeClose(Socket s)
        {
            try { s?.Shutdown(SocketShutdown.Both); } catch { }
            try { s?.Close(); } catch { }
        }
    }
}
