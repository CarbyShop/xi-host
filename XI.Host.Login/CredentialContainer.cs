namespace XI.Host.Login
{
    internal class CredentialContainer
    {
        public string Username { get; private set; }
        public string Password { get; private set; }

        /// <summary>
        /// Change password.
        /// </summary>
        public string Change { get; private set; }
        //public byte[] Hash { get; set; }

        public CredentialContainer(string username, string password)
        {
            Username = username;
            Password = password;
        }

        public CredentialContainer(string username, string password, string change)
        {
            Username = username;
            Password = password;
            Change = change;
        }
    }
}
