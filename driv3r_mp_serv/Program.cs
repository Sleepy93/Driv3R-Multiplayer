using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace driv3r_mp_serv
{
    class Program
    {
        const string TITLE = "Driv3r Multiplayer 0.1 Alpha - Server";
        const short PORT = 7777;
        const int BUFFER_SIZE = 76;

        static TcpListener serv;
        static byte slots, count;
        static Socket[] plsck;
        static Thread[] plthd;
        static string[] plip;

        static void Main()
        {
            Console.Title = TITLE;
            Console.Write("Players:");
            if (!byte.TryParse(Console.ReadLine(), out slots))
                slots = 4;
            if (slots < 2) slots = 2;
            else if (slots > 8) slots = 8;
            Console.Title = TITLE + " (0/" + slots + ")";
            Console.WriteLine("Server running");
            serv = new TcpListener(System.Net.IPAddress.Any, PORT);
            serv.Start();
            ConnectCheck();
        }

        static void ConnectCheck()
        {
            plsck = new Socket[slots];
            plthd = new Thread[slots];
            plip = new string[slots];
            count = 0;
            string ip;
            Socket tmp;
            byte i;
            while (true)
            {
                if (count >= slots) continue;
                tmp = serv.AcceptSocket();
                ip = ((System.Net.IPEndPoint)tmp.RemoteEndPoint).Address.ToString();
                for (i = 0; i < slots; i++)
                {
                    if (plsck[i] == null)
                    {
                        Console.WriteLine("Player connected id:" + i + " (" + ip + ")");
                        plip[i] = ip;
                        ip = null;
                        plsck[i] = tmp;
                        tmp = null;
                        try
                        {
                            plsck[i].Send(new byte[] { i });
                        }
                        catch { }
                        plthd[i] = new Thread(() => NetTrans(i));
                        plthd[i].Start();
                        count++;
                        Console.Title = TITLE + " (" + count + "/" + slots + ")";
                        break;
                    }
                }
            }
        }

        static void NetTrans(byte id)
        {
            try
            {
                byte[] buffer = new byte[BUFFER_SIZE];
                byte[] output = new byte[BUFFER_SIZE + 1];
                byte i;
                while (true)
                {
                    plsck[id].Receive(buffer);
                    Array.Copy(buffer, 0, output, 1, BUFFER_SIZE);
                    output[0] = id;
                    for (i = 0; i < slots; i++)
                        if (plsck[i] != null && i != id)
                            plsck[i].Send(output);
                }
            }
            catch
            {
                Console.WriteLine("Player disconnected id:" + id + " (" + plip[id] + ")");
                count--;
                Console.Title = TITLE + " (" + count + "/" + slots + ")";
                plsck[id].Close();
                plsck[id] = null;
                plthd[id].Abort();
                plthd[id] = null;
                plip[id] = null;
                GC.Collect();
            }
        }
    }
}