using System;
using XI.Host.Common;

namespace XI.Host.Login
{
    internal class CredentialContainer
    {
        public string Username { get; private set; }
        public string Password { get; private set; }
        public string PasswordChange { get; set; }

        [Custom("Useful for some custom server implementations.")]
        public string MAC { get; set; }

        public CredentialContainer(string username, string password)
        {
            Username = username;
            Password = password;
        }

        public CredentialContainer(string username, string password, string mac)
        {
            Username = username;
            Password = password;
            MAC = mac;
        }
    }
}
