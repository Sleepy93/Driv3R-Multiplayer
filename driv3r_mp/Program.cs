using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using System.Drawing;
using System.Windows.Forms;
using System.Reflection;
using System.Runtime.InteropServices;

namespace driv3r_mp
{
    class Form1 : Form
    {
        public const string TITLE = "Driv3r Multiplayer Client 0.1 Alpha";
        public const string game_exe = "Driv3r.exe";
        const string lastip_file = "driv3r_mp.txt";
        const int PORT = 7777;
        //Client recieve
        const int BUFFER_PED = 12;
        const int BUFFER_VEH = 64;
        const int BUFFER_SIZE = BUFFER_LEN + 1;
        const int BUFFER_LEN = BUFFER_PED + BUFFER_VEH;
        //Client send
        const int THD_SLEEP = 40; //25 FPS
        //Player
        const uint PTR_PLR_PED = 0x008B85D8;
        const uint PTR_PLR_VEH = 0x008B8560;
        //Pointer offsets
        static uint[] OFS_PLR_PED = { 0x10, 0x3B0 };
        static uint[] OFS_PLR_VEH = { 0x4, 0x18, 0x440, 0x634, 0x80 };
        //Network
        //const uint PTR_NET_PED = 0x07F0A88;
        //const uint PTR_NET_VEH = 0x07F49A8;
        uint PTR_NET_PED;
        uint PTR_NET_VEH;
        //Pointer offsets
        const uint OFS_NET_PED = 0x3B0;
        const uint OFS_NET_VEH = 0x80;
        //Offsets
        const uint OFS_PED = 0x1908;
        const uint OFS_VEH = 0x1E0;
        const uint OFS_ONF = 0x1B8;
        //Draw distance
        const float DD_PED = 65.0f;
        const float DD_VEH = 120.0f;
        //
        TextBox tb_ip;
        Button bt_connect;
        //
        StreamReader sr;
        StreamWriter sw;
        //
        TcpClient client;
        Socket sock;
        Thread thd, thd2;
        MemoryEdit.Memory mem;
        Process game;
        byte id;


        static byte[] inj_ptr_res =
        {
            0xE9, 0x00, 0x00, 0x00, 0x00,       //jmp dynamic address
            0x90                                //nop
        };

        static byte[] inj_ptr_ped =
        {
            0x90, 0x90,                         //nop
            0xE9, 0x00, 0x00, 0x00, 0x00,       //jmp dynamic address
            0x90                                //nop
        };

        static byte[] inj_ptr_car =
        {
            0xE9, 0x00, 0x00, 0x00, 0x00,       //jmp dynamic address
            0x90                                //nop
        };

        static byte[] inj_ptr_trc =
        {
            0x89, 0x98, 0x38, 0x0A, 0x7F, 0x00,                         //mov [eax+007F0A38],ebx
            0x89, 0x1D, 0x00, 0x00, 0x00, 0x00,                         //mov [dynamic],ebx - zero ped
            0x89, 0x1D, 0x00, 0x00, 0x00, 0x00,                         //mov [dynamic+4],ebx - zero car
            0xE9, 0x00, 0x00, 0x00, 0x00,                               //jmp 00448386 - zero ptr
            0x89, 0x0E,                                                 //mov [esi],ecx
            0x0F, 0x8B, 0x00, 0x00, 0x00, 0x00,                         //jnp 0050171F
            0x81, 0x3D, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, //cmp [dynamic],0
            0x75, 0x06,                                                 //jne +6
            0x89, 0x0D, 0x00, 0x00, 0x00, 0x00,                         //mov [dynamic],ecx - ptr ped
            0xE9, 0x00, 0x00, 0x00, 0x00,                               //jmp 005014A3 - player
            0x89, 0x8E, 0xA8, 0x49, 0x7F, 0x00,                         //mov [esi+007F49A8],ecx
            0x81, 0x3D, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, //cmp [dynamic+4],0
            0x75, 0x06,                                                 //jne +6
            0x89, 0x0D, 0x00, 0x00, 0x00, 0x00,                         //mov [dynamic+4],ecx - ptr car
            0xE9, 0x00, 0x00, 0x00, 0x00                                //jmp 004E24C9 - car
        };

        static uint[] offs_jmp = { 19, 50, 79 };
        const uint offs_jnp = 27;
        static uint[] offs_cmp = { 8, 14, 33, 45, 62, 74 };

        //uint[] offs_jmp = { 13, 42 };
        //uint[] offs_cmp = { 8, 25, 37 };

        const uint inj_ofs_ped = 23;
        const uint inj_ofs_car = 54;
        const uint LEN_JMP = 5;


        public Form1()
        {
            Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            Text = TITLE;
            ClientSize = new Size(320, 48);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            tb_ip = new TextBox();
            tb_ip.Bounds = new Rectangle(12, 12, 128, 24);
            tb_ip.MaxLength = 15;
            if (File.Exists(lastip_file))
            {
                sr = new StreamReader(lastip_file);
                tb_ip.Text = sr.ReadLine();
                sr.Close();
            }
            Controls.Add(tb_ip);
            bt_connect = new Button();
            bt_connect.Text = "Connect";
            bt_connect.Bounds = new Rectangle(ClientRectangle.Right - 140, 12, 128, 24);
            bt_connect.Click += bt_connect_Click;
            Controls.Add(bt_connect);
        }

        protected override void OnClosed(EventArgs e)
        {
            if (game != null && !game.HasExited)
                game.Kill();
            Environment.Exit(0);
            base.OnClosed(e);
        }

        void bt_connect_Click(object sender, EventArgs e)
        {
            bt_connect.Enabled = false;
            tb_ip.Enabled = false;
            try
            {
                byte[] buffer = new byte[1];
                client = new TcpClient(tb_ip.Text, PORT);
                client.Client.Receive(buffer);
                id = buffer[0];
                sw = new StreamWriter(lastip_file, false, Encoding.Default);
                sw.Write(tb_ip.Text);
                sw.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Source + " - " + ex.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                bt_connect.Enabled = true;
                tb_ip.Enabled = true;
                return;
            }
            game = Process.Start(game_exe);
            mem = new MemoryEdit.Memory();
            while (!mem.Attach(game, 0x001F0FFF)) ;
            //Code injection
            uint addr;
            IntPtr tmp = mem.Allocate((uint)inj_ptr_trc.Length);
            mem.WriteByte((uint)tmp, inj_ptr_trc, inj_ptr_trc.Length);
            //Pointer reset
            addr = 0x00448380;
            mem.WriteByte(addr, inj_ptr_res, inj_ptr_res.Length);
            mem.WriteByte(addr + 1, BitConverter.GetBytes((uint)tmp - (addr + LEN_JMP)), 4);
            //Ped pointer
            addr = 0x0050149B;
            mem.WriteByte(addr, inj_ptr_ped, inj_ptr_ped.Length);
            mem.WriteByte(addr + 3, BitConverter.GetBytes(((uint)tmp + inj_ofs_ped) - (addr + 2 + LEN_JMP)), 4);
            //Car pointer
            addr = 0x004E24C3;
            mem.WriteByte(addr, inj_ptr_car, inj_ptr_car.Length);
            mem.WriteByte(addr + 1, BitConverter.GetBytes(((uint)tmp + inj_ofs_car) - (addr + LEN_JMP)), 4);
            //JMP
            mem.WriteByte((uint)tmp + offs_jmp[0], BitConverter.GetBytes(0x00448386 - ((uint)tmp + offs_jmp[0] + 4)), 4);
            mem.WriteByte((uint)tmp + offs_jmp[1], BitConverter.GetBytes(0x005014A3 - ((uint)tmp + offs_jmp[1] + 4)), 4);
            mem.WriteByte((uint)tmp + offs_jmp[2], BitConverter.GetBytes(0x004E24C9 - ((uint)tmp + offs_jmp[2] + 4)), 4);
            //JNP
            mem.WriteByte((uint)tmp + offs_jnp, BitConverter.GetBytes(0x0050171F - ((uint)tmp + offs_jnp + 4)), 4);
            //CMP, MOV
            PTR_NET_PED = (uint)((uint)tmp + inj_ptr_trc.Length);
            PTR_NET_VEH = PTR_NET_PED + 4;
            mem.WriteByte((uint)tmp + offs_cmp[0], BitConverter.GetBytes(PTR_NET_PED), 4);
            mem.WriteByte((uint)tmp + offs_cmp[1], BitConverter.GetBytes(PTR_NET_VEH), 4);
            mem.WriteByte((uint)tmp + offs_cmp[2], BitConverter.GetBytes(PTR_NET_PED), 4);
            mem.WriteByte((uint)tmp + offs_cmp[3], BitConverter.GetBytes(PTR_NET_PED), 4);
            mem.WriteByte((uint)tmp + offs_cmp[4], BitConverter.GetBytes(PTR_NET_VEH), 4);
            mem.WriteByte((uint)tmp + offs_cmp[5], BitConverter.GetBytes(PTR_NET_VEH), 4);
            //Code injection end
            sock = client.Client;
            thd = new Thread(new ThreadStart(NetRec));
            thd.Start();
            thd2 = new Thread(new ThreadStart(NetSend));
            thd2.Start();
        }

        void NetRec()
        {
            try
            {
                byte[] buffer = new byte[BUFFER_SIZE];
                byte[] ped = new byte[BUFFER_PED];
                byte[] veh = new byte[BUFFER_VEH];
                float x, y, z;
                uint tmp;
                while (true)
                {
                    sock.Receive(buffer);
                    Array.Copy(buffer, 1, ped, 0, BUFFER_PED);
                    Array.Copy(buffer, 1 + BUFFER_PED, veh, 0, BUFFER_VEH);
                    if (buffer[0] >= id)
                        buffer[0]--;
                    //Ped
                    x = BitConverter.ToSingle(ped, 0);
                    if (x != 0)
                    {
                        y = BitConverter.ToSingle(ped, 4);
                        z = BitConverter.ToSingle(ped, 8);
                        if (GetDistance(ref x, ref y, ref z) > DD_PED) continue;
                        tmp = (uint)(mem.Read(PTR_NET_PED) + OFS_NET_PED);
                        mem.WriteByte(tmp + OFS_PED * buffer[0], ped, BUFFER_PED);
                        continue;
                    }
                    //Vehicle
                    x = BitConverter.ToSingle(veh, 0);
                    if (x != 0)
                    {
                        y = BitConverter.ToSingle(veh, 4);
                        z = BitConverter.ToSingle(veh, 8);
                        if (GetDistance(ref x, ref y, ref z) > DD_VEH) continue;
                        tmp = (uint)(mem.Read(PTR_NET_VEH) + OFS_NET_VEH);
                        mem.WriteByte(tmp + OFS_VEH * (buffer[0] + 1u), veh, BUFFER_VEH);
                    }
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Source + " - " + e.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (game != null && !game.HasExited)
                    game.Kill();
                Environment.Exit(0);
            }
        }

        float GetDistance(ref float x, ref float y, ref float z)
        {
            float px, py, pz;
            uint tmp;
            tmp = (uint)(mem.Read(PTR_PLR_PED) + OFS_PLR_PED[0]);
            tmp = (uint)(mem.Read(tmp) + OFS_PLR_PED[1]);
            if (mem.ReadByte(tmp - OFS_ONF) == 0)
            {
                tmp = (uint)(mem.Read(PTR_PLR_VEH) + OFS_PLR_VEH[0]);
                for (int i = 1; i < 5; i++)
                    tmp = (uint)(mem.Read(tmp) + OFS_PLR_VEH[i]);
            }
            px = mem.ReadFloat(tmp);
            py = mem.ReadFloat(tmp + 4);
            pz = mem.ReadFloat(tmp + 8);
            return (float)Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y) + (pz - z) * (pz - z));
        }

        void NetSend()
        {
            try
            {
                int i;
                uint tmp;
                byte[] buffer = new byte[BUFFER_LEN];
                byte[] nul = new byte[4];
                while (true)
                {
                    Thread.Sleep(THD_SLEEP);
                    //Ped
                    tmp = (uint)(mem.Read(PTR_PLR_PED) + OFS_PLR_PED[0]);
                    tmp = (uint)(mem.Read(tmp) + OFS_PLR_PED[1]);
                    if (mem.ReadByte(tmp - OFS_ONF) == 1)
                    {
                        Array.Copy(nul, 0, buffer, BUFFER_PED, 4);
                        mem.ReadBytes(tmp, BUFFER_PED);
                        Array.Copy(mem.ReadBytes(tmp, BUFFER_PED), buffer, BUFFER_PED);
                    }
                    //Vehicle
                    else
                    {
                        tmp = (uint)(mem.Read(PTR_PLR_VEH) + OFS_PLR_VEH[0]);
                        for (i = 1; i < 5; i++)
                            tmp = (uint)(mem.Read(tmp) + OFS_PLR_VEH[i]);
                        Array.Copy(mem.ReadBytes(tmp, BUFFER_VEH), 0, buffer, BUFFER_PED, BUFFER_VEH);
                        Array.Copy(nul, buffer, 4);
                    }
                    sock.Send(buffer);
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Source + " - " + e.Message, Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                if (game != null && !game.HasExited)
                    game.Kill();
                Environment.Exit(0);
            }
        }

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            this.SuspendLayout();
        }
    }

    class Progam
    {
        [STAThread]
        static void Main()
        {
            if (!File.Exists(Form1.game_exe))
            {
                MessageBox.Show("Game not found! (" + Form1.game_exe + ")",
                    Form1.TITLE, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }
    }
}