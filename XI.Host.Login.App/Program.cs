using System;
using XI.Host.Common;
//using System.Security.Principal;

namespace XI.Host.Login.App
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
                using (var loginServer = new LoginServer())
                {
                    Console.WriteLine(string.Concat(Logger.SOURCE, ' ', Logger.VERSION));
                    Console.WriteLine("Press the ESCape key to exit and clear the sessions table, otherwise X key... ");

                    ConsoleKey key = ConsoleKey.NoName;

                    do
                    {
                        key = Console.ReadKey(true).Key;
                    }
                    while (key != ConsoleKey.Escape && key != ConsoleKey.X);

                    loginServer.ClearSessionsOnDispose = (key == ConsoleKey.Escape);
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
