using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Server
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            int port = 8888;
            Server server = new Server(port);
            await Task.Run(async () => await server.StartAsync());

        }
    }
}
