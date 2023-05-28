using System;
using XI.Host.Common;
using XI.Host.Message;
//using System.Security.Principal;

namespace XI.Host.World.App
{
    class Program
    {
        static void Main(string[] args)
        {
            bool runAsAdministrator = true;// false;

            //using (WindowsIdentity wi = WindowsIdentity.GetCurrent())
            //{
            //    WindowsPrincipal wp = new WindowsPrincipal(wi);

            //    runAsAdministrator = wp.IsInRole(WindowsBuiltInRole.Administrator);
            //}

            if (runAsAdministrator)
            {
                using (var messageServer = new MessageServer())
                {
                    Console.WriteLine(string.Concat(Logger.SOURCE, ' ', Logger.VERSION));
                    Console.WriteLine("Press the ESCape key to exit... ");
                    while (Console.ReadKey(true).Key != ConsoleKey.Escape) { }
                }
            }
            else
            {
                Console.WriteLine("Must be run as Administrator.");
                Console.WriteLine("Press any key to exit... ");
                Console.ReadKey(true);
            }
        }
    }
}
